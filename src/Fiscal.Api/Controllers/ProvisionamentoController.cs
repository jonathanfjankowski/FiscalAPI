using System.Security.Cryptography;
using System.Text;
using Fiscal.Api.Authentication;
using Fiscal.Api.Infrastructure;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

public record ProvisionamentoEmpresaRequest(
    string? Cnpj,
    string? RazaoSocial,
    string? NomeFantasia,
    string? InscricaoEstadual,
    string? Uf,
    string? Ambiente,
    string? WebhookUrl);

public record ProvisionamentoEmpresaResponse(
    Guid Id,
    string ApiKey,
    string WebhookToken);

/// <summary>
/// Provisionamento automático pelo ERP Okto (fiscal v2): cria o tenant
/// idempotente por CNPJ e EMITE as credenciais — nova API key do ambiente
/// (com rotação: as anteriores do mesmo ambiente são revogadas) e novo
/// token de webhook. O ERP grava tudo cifrado; usuário nenhum digita segredo.
///
/// Autenticação machine-to-machine por segredo compartilhado no header
/// `X-Bootstrap-Key` (config Fiscal:BootstrapToken) — o tenant ainda não
/// existe na primeira chamada, então não há API key ainda; o segredo é
/// comparação em tempo constante e a rota é desabilitada sem configuração.
/// </summary>
[ApiController]
[Route("v1/empresas")]
public class ProvisionamentoController : ControllerBase
{
    private readonly FiscalDbContext _db;
    private readonly ICertificadoStore _certStore;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IConfiguration _configuration;
    private readonly bool _sandbox;

    public ProvisionamentoController(
        FiscalDbContext db,
        ICertificadoStore certStore,
        IRepositorioAuditoria auditoria,
        IConfiguration configuration)
    {
        _db = db;
        _certStore = certStore;
        _auditoria = auditoria;
        _configuration = configuration;
        _sandbox = configuration.GetValue("Fiscal:ModoSandbox", true);
    }

    [HttpPost]
    public async Task<IActionResult> Provisionar([FromBody] ProvisionamentoEmpresaRequest req, CancellationToken ct)
    {
        var bootstrapToken = _configuration["Fiscal:BootstrapToken"];
        if (string.IsNullOrEmpty(bootstrapToken))
            return Problem(statusCode: 503, title: "Provisionamento desabilitado",
                detail: "Defina Fiscal:BootstrapToken para habilitar o provisionamento automático.");

        if (! BootstrapKeyValida(Request.Headers["X-Bootstrap-Key"].FirstOrDefault(), bootstrapToken))
            return Unauthorized();

        if (req is null) return Problem(statusCode: 400, title: "Corpo da requisição é obrigatório.");

        var cnpj = SomenteDigitos(req.Cnpj);
        var ambiente = req.Ambiente?.Trim().ToLowerInvariant();
        if (cnpj.Length != 14)
            return Problem(statusCode: 422, title: "CNPJ inválido", detail: "Informe os 14 dígitos do CNPJ.");
        if (string.IsNullOrWhiteSpace(req.Uf) || req.Uf!.Trim().Length != 2)
            return Problem(statusCode: 422, title: "UF inválida", detail: "Informe a UF com 2 letras (ex.: PR).");
        if (ambiente is not ("producao" or "homologacao"))
            return Problem(statusCode: 422, title: "Ambiente inválido", detail: "Use 'producao' ou 'homologacao'.");
        if (string.IsNullOrWhiteSpace(req.RazaoSocial))
            return Problem(statusCode: 422, title: "RazaoSocial é obrigatória.");
        var problemaWebhook = ValidadorWebhookUrl.Validar(req.WebhookUrl, _sandbox);
        if (problemaWebhook is not null)
            return Problem(statusCode: 422, title: "WebhookUrl inválida", detail: problemaWebhook);

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Cnpj == cnpj, ct);
        var criouTenant = tenant is null;
        if (criouTenant)
        {
            tenant = new Tenant
            {
                Cnpj = cnpj,
                RazaoSocial = req.RazaoSocial!.Trim(),
                Uf = req.Uf.Trim().ToUpperInvariant(),
                RegimeTributario = 3, // regime normal por padrão; ERP ajusta pelo perfil
                AmbientePadrao = ambiente == "producao" ? (short)Ambiente.Producao : (short)Ambiente.Homologacao,
                InscricaoEstadual = req.InscricaoEstadual,
                WebhookUrl = req.WebhookUrl,
                Ativo = true,
                CriadoEm = DateTimeOffset.UtcNow
            };
            _db.Tenants.Add(tenant);
        }
        else
        {
            // Provisionamento é auto-healing: dados básicos e webhook seguem o ERP.
            tenant!.RazaoSocial = req.RazaoSocial!.Trim();
            tenant.Uf = req.Uf.Trim().ToUpperInvariant();
            tenant.InscricaoEstadual = req.InscricaoEstadual ?? tenant.InscricaoEstadual;
            tenant.WebhookUrl = req.WebhookUrl;
            tenant.Ativo = true;
        }

        // ROTAÇÃO de credenciais: cada provisionamento emite chave nova do
        // ambiente (revogando as anteriores do mesmo ambiente) e token de
        // webhook novo — a resposta SEMPRE contém credenciais válidas.
        var ambienteShort = ambiente == "producao" ? (short)Ambiente.Producao : (short)Ambiente.Homologacao;
        var chavesAnteriores = await _db.ApiKeys
            .Where(k => k.TenantId == tenant.Id && k.Ambiente == ambienteShort && k.Ativa && k.RevogadoEm == null)
            .ToListAsync(ct);
        foreach (var anterior in chavesAnteriores)
        {
            anterior.Ativa = false;
            anterior.RevogadoEm = DateTimeOffset.UtcNow;
        }

        var chaveEmClaro = ApiKeyAuthenticationHandler.GenerateKey(ambiente);
        var apiKey = new ApiKey
        {
            TenantId = tenant.Id,
            Prefixo = ApiKeyAuthenticationHandler.PrefixoDa(chaveEmClaro),
            KeyHash = ApiKeyAuthenticationHandler.HashKey(chaveEmClaro),
            Descricao = "Provisionamento automático (ERP Okto)",
            Ambiente = ambienteShort,
            Ativa = true,
            CriadoEm = DateTimeOffset.UtcNow
        };
        _db.ApiKeys.Add(apiKey);

        var webhookToken = GerarTokenWebhook();
        tenant.WebhookSecretCriptografado = await _certStore.CifrarTextoAsync(webhookToken, ct);

        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            Acao = criouTenant ? "TENANT_PROVISIONADO_ERP" : "TENANT_REPROVISIONADO_ERP",
            TenantId = tenant.Id,
            RecursoId = apiKey.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress,
            Detalhe = $"{{\"cnpj\":\"{tenant.Cnpj}\",\"ambiente\":\"{ambiente}\",\"chavesRevogadas\":{chavesAnteriores.Count}}}"
        }, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new ProvisionamentoEmpresaResponse(tenant.Id, chaveEmClaro, webhookToken));
    }

    /// <summary>Comparação em tempo constante do segredo de bootstrap.</summary>
    private static bool BootstrapKeyValida(string? recebida, string esperada)
    {
        if (string.IsNullOrEmpty(recebida)) return false;

        var a = Encoding.UTF8.GetBytes(recebida);
        var b = Encoding.UTF8.GetBytes(esperada);

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Token de webhook: 32 bytes de entropia, base64url (43 chars).</summary>
    private static string GerarTokenWebhook() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private static string SomenteDigitos(string? valor) =>
        new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());
}
