using Fiscal.Api.Authentication;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

public record AdminTenantRequest(
    string? Cnpj,
    string? RazaoSocial,
    string? Uf,
    string? CodigoMunicipioIbge,
    short? RegimeTributario,
    string? AmbientePadrao,
    string? InscricaoEstadual,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cep,
    string? NomeMunicipio,
    string? WebhookUrl,
    string? WebhookSecret,
    bool? Ativo,
    // Opcional: "producao"|"homologacao" cria a primeira API key junto com o
    // tenant (mesma transação). Ausente/null mantém o fluxo em 2 chamadas.
    string? CriarApiKey = null);

public record AdminTenantResponse(
    Guid Id,
    string Cnpj,
    string RazaoSocial,
    string Uf,
    string? CodigoMunicipioIbge,
    short RegimeTributario,
    string AmbientePadrao,
    string? InscricaoEstadual,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cep,
    string? NomeMunicipio,
    string? WebhookUrl,
    bool Ativo,
    DateTimeOffset CriadoEm,
    int ApiKeysAtivas,
    int CertificadosAtivos);

/// <summary>
/// Primeira API key criada junto com o tenant (criarApiKey). Mesma garantia
/// do endpoint de keys: a chave completa aparece uma única vez, aqui.
/// </summary>
public record AdminTenantApiKeyCriadaResponse(
    Guid Id,
    string Chave,
    string Prefixo,
    string Ambiente,
    string Aviso);

/// <summary>Resposta do POST /v1/admin/tenants quando criarApiKey é informado.</summary>
public record AdminTenantCriadoComApiKeyResponse(AdminTenantResponse Tenant, AdminTenantApiKeyCriadaResponse ApiKey);

[ApiController]
[Route("v1/admin/tenants")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminTenantsController : ControllerBase
{
    private readonly FiscalDbContext _db;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly ICertificadoStore _certStore;
    private readonly bool _sandbox;

    public AdminTenantsController(
        FiscalDbContext db, IRepositorioAuditoria auditoria, ICertificadoStore certStore, IConfiguration configuration)
    {
        _db = db;
        _auditoria = auditoria;
        _certStore = certStore;
        _sandbox = configuration.GetValue("Fiscal:ModoSandbox", true);
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .OrderBy(t => t.RazaoSocial)
            .Select(t => new AdminTenantResponse(
                t.Id, t.Cnpj, t.RazaoSocial, t.Uf, t.CodigoMunicipioIbge,
                t.RegimeTributario,
                t.AmbientePadrao == (short)Ambiente.Producao ? "producao" : "homologacao",
                t.InscricaoEstadual, t.Logradouro, t.Numero, t.Complemento, t.Bairro,
                t.Cep, t.NomeMunicipio, t.WebhookUrl, t.Ativo, t.CriadoEm,
                t.ApiKeys.Count(k => k.Ativa && k.RevogadoEm == null),
                t.Certificados.Count(c => c.Ativo)))
            .ToListAsync(ct);
        return Ok(tenants);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new AdminTenantResponse(
                t.Id, t.Cnpj, t.RazaoSocial, t.Uf, t.CodigoMunicipioIbge,
                t.RegimeTributario,
                t.AmbientePadrao == (short)Ambiente.Producao ? "producao" : "homologacao",
                t.InscricaoEstadual, t.Logradouro, t.Numero, t.Complemento, t.Bairro,
                t.Cep, t.NomeMunicipio, t.WebhookUrl, t.Ativo, t.CriadoEm,
                t.ApiKeys.Count(k => k.Ativa && k.RevogadoEm == null),
                t.Certificados.Count(c => c.Ativo)))
            .FirstOrDefaultAsync(ct);
        if (tenant is null) return NotFound();
        return Ok(tenant);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] AdminTenantRequest req, CancellationToken ct)
    {
        if (req is null) return Problem(statusCode: 400, title: "Corpo da requisição é obrigatório.");

        var cnpj = SomenteDigitos(req.Cnpj);
        if (string.IsNullOrWhiteSpace(req.RazaoSocial))
            return Problem(statusCode: 422, title: "RazaoSocial é obrigatória.");
        if (cnpj.Length != 14)
            return Problem(statusCode: 422, title: "CNPJ inválido", detail: "Informe os 14 dígitos do CNPJ.");
        if (string.IsNullOrWhiteSpace(req.Uf) || req.Uf!.Trim().Length != 2)
            return Problem(statusCode: 422, title: "UF inválida", detail: "Informe a UF com 2 letras (ex.: PR).");
        if (req.AmbientePadrao is not null && req.AmbientePadrao != "producao" && req.AmbientePadrao != "homologacao")
            return Problem(statusCode: 422, title: "AmbientePadrao inválido", detail: "Use 'producao' ou 'homologacao'.");
        if (req.CriarApiKey is not null && req.CriarApiKey != "producao" && req.CriarApiKey != "homologacao")
            return Problem(statusCode: 422, title: "CriarApiKey inválido",
                detail: "Use 'producao' ou 'homologacao' (ou omita o campo para não criar chave).");
        if (req.RegimeTributario is < 1 or > 4)
            return Problem(statusCode: 422, title: "RegimeTributario inválido", detail: "Use 1 (Simples), 2 (Simples exceto sublimite), 3 (Regime Normal) ou 4 (MEI).");
        var problemaWebhook = Infrastructure.ValidadorWebhookUrl.Validar(req.WebhookUrl, _sandbox);
        if (problemaWebhook is not null)
            return Problem(statusCode: 422, title: "WebhookUrl inválida", detail: problemaWebhook);

        if (await _db.Tenants.AnyAsync(t => t.Cnpj == cnpj, ct))
            return Problem(statusCode: 409, title: "Já existe um tenant com este CNPJ.");

        var tenant = new Tenant
        {
            Cnpj = cnpj,
            RazaoSocial = req.RazaoSocial!.Trim(),
            Uf = req.Uf.Trim().ToUpperInvariant(),
            CodigoMunicipioIbge = req.CodigoMunicipioIbge,
            RegimeTributario = req.RegimeTributario ?? 3,
            AmbientePadrao = req.AmbientePadrao == "producao" ? (short)Ambiente.Producao : (short)Ambiente.Homologacao,
            InscricaoEstadual = req.InscricaoEstadual,
            Logradouro = req.Logradouro,
            Numero = req.Numero,
            Complemento = req.Complemento,
            Bairro = req.Bairro,
            Cep = SomenteDigitos(req.Cep) is { Length: 8 } cep ? cep : req.Cep,
            NomeMunicipio = req.NomeMunicipio,
            WebhookUrl = req.WebhookUrl,
            WebhookSecretCriptografado = req.WebhookSecret is null
                ? null
                : await _certStore.CifrarTextoAsync(req.WebhookSecret, ct),
            Ativo = req.Ativo ?? true,
            CriadoEm = DateTimeOffset.UtcNow
        };

        _db.Tenants.Add(tenant);

        // Primeira API key (opcional, criarApiKey): mesma geração/semântica do
        // AdminApiKeysController, adicionada ao MESMO DbContext — tenant + key
        // vão ao banco no mesmo SaveChanges (atômico).
        ApiKey? apiKey = null;
        string? chaveEmClaro = null;
        if (req.CriarApiKey is not null)
        {
            chaveEmClaro = ApiKeyAuthenticationHandler.GenerateKey(req.CriarApiKey);
            apiKey = new ApiKey
            {
                TenantId = tenant.Id,
                Prefixo = chaveEmClaro[..12],
                KeyHash = ApiKeyAuthenticationHandler.HashKey(chaveEmClaro),
                Descricao = "Criada automaticamente junto com o tenant",
                Ambiente = (short)(req.CriarApiKey == "producao" ? Ambiente.Producao : Ambiente.Homologacao),
                Ativa = true,
                CriadoEm = DateTimeOffset.UtcNow
            };
            _db.ApiKeys.Add(apiKey);
        }

        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            Acao = "TENANT_CRIADO",
            TenantId = tenant.Id,
            RecursoId = tenant.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress,
            Detalhe = $"{{\"cnpj\":\"{tenant.Cnpj}\"}}"
        }, ct);
        if (apiKey is not null)
        {
            await _auditoria.RegistrarAsync(new Auditoria
            {
                TenantId = tenant.Id,
                ApiKeyId = apiKey.Id,
                Acao = "API_KEY_CRIADA",
                RecursoId = apiKey.Id,
                IpOrigem = HttpContext.Connection.RemoteIpAddress
            }, ct);
        }
        await _db.SaveChangesAsync(ct);

        // Fluxo legado (criarApiKey ausente): corpo = GUID do tenant, igual a antes.
        if (apiKey is null || chaveEmClaro is null)
            return CreatedAtAction(nameof(Obter), new { id = tenant.Id }, tenant.Id);

        var resposta = new AdminTenantCriadoComApiKeyResponse(
            new AdminTenantResponse(
                tenant.Id, tenant.Cnpj, tenant.RazaoSocial, tenant.Uf, tenant.CodigoMunicipioIbge,
                tenant.RegimeTributario,
                tenant.AmbientePadrao == (short)Ambiente.Producao ? "producao" : "homologacao",
                tenant.InscricaoEstadual, tenant.Logradouro, tenant.Numero, tenant.Complemento,
                tenant.Bairro, tenant.Cep, tenant.NomeMunicipio, tenant.WebhookUrl, tenant.Ativo,
                tenant.CriadoEm,
                ApiKeysAtivas: 1,   // tenant novo: só a chave que acabou de ser criada
                CertificadosAtivos: 0),
            new AdminTenantApiKeyCriadaResponse(
                apiKey.Id, chaveEmClaro, apiKey.Prefixo,
                apiKey.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
                "Esta é a única vez que a chave completa é exibida. Guarde-a em local seguro."));
        return CreatedAtAction(nameof(Obter), new { id = tenant.Id }, resposta);
    }

    /// <summary>Atualização parcial: campos nulos não são alterados.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AdminTenantRequest req, CancellationToken ct)
    {
        if (req is null) return Problem(statusCode: 400, title: "Corpo da requisição é obrigatório.");

        // criarApiKey só existe na criação — rejeita em vez de ignorar em silêncio.
        if (req.CriarApiKey is not null)
            return Problem(statusCode: 422, title: "CriarApiKey não é suportado na atualização",
                detail: "Use POST /v1/admin/tenants (com criarApiKey) ou POST /v1/admin/tenants/{tenantId}/api-keys para criar chaves.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        if (req.Cnpj is not null)
        {
            var cnpj = SomenteDigitos(req.Cnpj);
            if (cnpj.Length != 14)
                return Problem(statusCode: 422, title: "CNPJ inválido", detail: "Informe os 14 dígitos do CNPJ.");
            if (cnpj != tenant.Cnpj && await _db.Tenants.AnyAsync(t => t.Cnpj == cnpj, ct))
                return Problem(statusCode: 409, title: "Já existe um tenant com este CNPJ.");
            tenant.Cnpj = cnpj;
        }
        if (req.RazaoSocial is not null) tenant.RazaoSocial = req.RazaoSocial.Trim();
        if (req.Uf is not null)
        {
            if (req.Uf.Trim().Length != 2)
                return Problem(statusCode: 422, title: "UF inválida", detail: "Informe a UF com 2 letras (ex.: PR).");
            tenant.Uf = req.Uf.Trim().ToUpperInvariant();
        }
        if (req.CodigoMunicipioIbge is not null) tenant.CodigoMunicipioIbge = req.CodigoMunicipioIbge;
        if (req.RegimeTributario is not null)
        {
            if (req.RegimeTributario is < 1 or > 4)
                return Problem(statusCode: 422, title: "RegimeTributario inválido", detail: "Use 1, 2, 3 ou 4 (MEI).");
            tenant.RegimeTributario = req.RegimeTributario.Value;
        }
        if (req.AmbientePadrao is not null)
        {
            if (req.AmbientePadrao != "producao" && req.AmbientePadrao != "homologacao")
                return Problem(statusCode: 422, title: "AmbientePadrao inválido", detail: "Use 'producao' ou 'homologacao'.");
            tenant.AmbientePadrao = req.AmbientePadrao == "producao" ? (short)Ambiente.Producao : (short)Ambiente.Homologacao;
        }
        if (req.InscricaoEstadual is not null) tenant.InscricaoEstadual = req.InscricaoEstadual;
        if (req.Logradouro is not null) tenant.Logradouro = req.Logradouro;
        if (req.Numero is not null) tenant.Numero = req.Numero;
        if (req.Complemento is not null) tenant.Complemento = req.Complemento;
        if (req.Bairro is not null) tenant.Bairro = req.Bairro;
        if (req.Cep is not null) tenant.Cep = SomenteDigitos(req.Cep) is { Length: 8 } cep ? cep : req.Cep;
        if (req.NomeMunicipio is not null) tenant.NomeMunicipio = req.NomeMunicipio;
        if (req.WebhookUrl is not null)
        {
            var problema = Infrastructure.ValidadorWebhookUrl.Validar(req.WebhookUrl, _sandbox);
            if (problema is not null)
                return Problem(statusCode: 422, title: "WebhookUrl inválida", detail: problema);
            tenant.WebhookUrl = req.WebhookUrl.Trim() is { Length: > 0 } url ? url : null;
        }
        if (req.WebhookSecret is not null)
        {
            // Sempre cifrado em repouso (envelope KEK); texto plano legado é limpo.
            tenant.WebhookSecretCriptografado = await _certStore.CifrarTextoAsync(req.WebhookSecret, ct);
            tenant.WebhookSecret = null;
        }
        if (req.Ativo is not null) tenant.Ativo = req.Ativo.Value;

        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            Acao = "TENANT_ATUALIZADO",
            TenantId = tenant.Id,
            RecursoId = tenant.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Desativação soft: Ativo=false. Não apaga dados nem revoga keys.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        tenant.Ativo = false;
        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            Acao = "TENANT_DESATIVADO",
            TenantId = tenant.Id,
            RecursoId = tenant.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static string SomenteDigitos(string? valor) =>
        new string((valor ?? "").Where(char.IsDigit).ToArray());
}
