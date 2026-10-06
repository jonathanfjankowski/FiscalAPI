using Fiscal.Api.Authentication;
using Fiscal.Api.Infrastructure;
using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

public record PerfilTenantRequest(
    string? InscricaoEstadual,
    string? InscricaoMunicipal,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cep,
    string? NomeMunicipio,
    string? CscId,
    string? Csc);

public record WebhooksTenantRequest(
    string? WebhookUrl,
    string? WebhookSecret);

[ApiController]
[Route("v1/tenants")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.SchemeName)]
public class TenantsController : ControllerBase
{
    private readonly FiscalDbContext _db;
    private readonly ICertificadoStore _certStore;
    private readonly IRepositorioAuditoria _auditoria;

    public TenantsController(FiscalDbContext db, ICertificadoStore certStore, IRepositorioAuditoria auditoria)
    {
        _db = db;
        _certStore = certStore;
        _auditoria = auditoria;
    }

    /// <summary>Perfil fiscal do tenant (dados do emitente usados na NFe/NFC-e).</summary>
    [HttpGet("perfil")]
    public async Task<IActionResult> ObterPerfil(CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        return Ok(new
        {
            tenant.Cnpj,
            tenant.RazaoSocial,
            tenant.Uf,
            tenant.CodigoMunicipioIbge,
            tenant.RegimeTributario,
            tenant.InscricaoEstadual,
            tenant.InscricaoMunicipal,
            tenant.Logradouro,
            tenant.Numero,
            tenant.Complemento,
            tenant.Bairro,
            tenant.Cep,
            tenant.NomeMunicipio,
            tenant.CscId,
            cscCadastrado = tenant.CscCriptografado is not null,
        });
    }

    /// <summary>
    /// Atualiza o perfil fiscal do emitente (exigido para emissão real) e,
    /// opcionalmente, o CSC/IdCSC da NFC-e (o CSC é armazenado cifrado com a KEK).
    /// Campos nulos não são alterados.
    /// </summary>
    [HttpPut("perfil")]
    public async Task<IActionResult> AtualizarPerfil([FromBody] PerfilTenantRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        tenant.InscricaoEstadual = req.InscricaoEstadual ?? tenant.InscricaoEstadual;
        tenant.InscricaoMunicipal = req.InscricaoMunicipal ?? tenant.InscricaoMunicipal;
        tenant.Logradouro = req.Logradouro ?? tenant.Logradouro;
        tenant.Numero = req.Numero ?? tenant.Numero;
        tenant.Complemento = req.Complemento ?? tenant.Complemento;
        tenant.Bairro = req.Bairro ?? tenant.Bairro;
        tenant.Cep = req.Cep ?? tenant.Cep;
        tenant.NomeMunicipio = req.NomeMunicipio ?? tenant.NomeMunicipio;

        if (req.Csc is not null)
        {
            if (string.IsNullOrWhiteSpace(req.CscId))
                return Problem(statusCode: 422, title: "Informe cscId junto com o csc.");

            tenant.CscCriptografado = await _certStore.CifrarTextoAsync(req.Csc, ct);
            tenant.CscId = req.CscId;
        }
        else if (req.CscId is not null)
        {
            return Problem(statusCode: 422, title: "Informe o csc junto com o cscId.");
        }

        await _db.SaveChangesAsync(ct);
        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenantId,
            Acao = "PERFIL_TENANT_ATUALIZADO",
            RecursoId = tenantId,
            Detalhe = $"{{\"cscAtualizado\":{(req.Csc is not null ? "true" : "false").ToLowerInvariant()}}}"
        }, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new { atualizado = true });
    }

    /// <summary>
    /// Configuração de webhook do tenant (self-service — dispensa o painel admin).
    /// O segredo nunca é devolvido; só um booleano indica que existe.
    /// </summary>
    [HttpGet("webhooks")]
    public async Task<IActionResult> ObterWebhooks(CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        return Ok(new
        {
            tenant.WebhookUrl,
            webhookSecretCadastrado =
                tenant.WebhookSecretCriptografado is not null || tenant.WebhookSecret is not null,
        });
    }

    /// <summary>
    /// Define webhookUrl e webhookSecret (HMAC-SHA256 das entregas, janela
    /// anti-replay de 5 min). Campos nulos não são alterados. O segredo é
    /// armazenado cifrado com a KEK (mesmo envelope do CSC e dos certificados).
    /// </summary>
    [HttpPut("webhooks")]
    public async Task<IActionResult> AtualizarWebhooks([FromBody] WebhooksTenantRequest req, CancellationToken ct)
    {
        if (req.WebhookUrl is null && req.WebhookSecret is null)
            return Problem(statusCode: 400, title: "Informe webhookUrl e/ou webhookSecret.");

        var tenantId = HttpContext.GetTenantId();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        if (req.WebhookUrl is not null)
        {
            // HTTPS só é exigido para tenants fora do sandbox (Tenant.Sandbox).
            var problema = ValidadorWebhookUrl.Validar(req.WebhookUrl, tenant.Sandbox);
            if (problema is not null)
                return Problem(statusCode: 422, title: "webhookUrl inválida", detail: problema);
            var url = req.WebhookUrl.Trim();
            tenant.WebhookUrl = url.Length == 0 ? null : url;
        }

        if (req.WebhookSecret is not null)
        {
            var segredo = req.WebhookSecret.Trim();
            if (segredo.Length > 200)
                return Problem(statusCode: 422, title: "webhookSecret excede 200 caracteres.");
            if (segredo.Length < 16)
                return Problem(statusCode: 422, title: "webhookSecret muito curto",
                    detail: "Use ao menos 16 caracteres — o segredo assina HMAC-SHA256 as entregas.");
            tenant.WebhookSecretCriptografado = await _certStore.CifrarTextoAsync(segredo, ct);
            tenant.WebhookSecret = null;
        }

        await _db.SaveChangesAsync(ct);
        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenantId,
            Acao = "WEBHOOK_TENANT_ATUALIZADO",
            RecursoId = tenantId,
            Detalhe = $"{{\"urlAtualizada\":{(req.WebhookUrl is not null ? "true" : "false").ToLowerInvariant()}," +
                      $"\"segredoAtualizado\":{(req.WebhookSecret is not null ? "true" : "false").ToLowerInvariant()}}}"
        }, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            atualizado = true,
            tenant.WebhookUrl,
            webhookSecretCadastrado =
                tenant.WebhookSecretCriptografado is not null || tenant.WebhookSecret is not null,
        });
    }
}
