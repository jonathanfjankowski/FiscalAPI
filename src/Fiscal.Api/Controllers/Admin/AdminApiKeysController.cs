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

/// <summary>
/// Gestão de API keys pelo painel admin (escopo cross-tenant).
/// Mesma semântica do ApiKeysController do tenant: a chave completa é
/// retornada uma única vez na criação.
/// </summary>
[ApiController]
[Route("v1/admin/tenants/{tenantId:guid}/api-keys")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminApiKeysController : ControllerBase
{
    private readonly FiscalDbContext _db;
    private readonly IRepositorioAuditoria _auditoria;

    public AdminApiKeysController(FiscalDbContext db, IRepositorioAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    public record CreateApiKeyRequest(string? Descricao, string Ambiente);

    public record AdminApiKeyResponse(
        Guid Id, string Prefixo, string? Descricao, string Ambiente, bool Ativa, DateTimeOffset CriadoEm);

    [HttpGet]
    public async Task<IActionResult> Listar(Guid tenantId, CancellationToken ct)
    {
        if (!await TenantExisteAsync(tenantId, ct)) return NotFound("Tenant não encontrado.");
        var chaves = await _db.ApiKeys.AsNoTracking()
            .Where(k => k.TenantId == tenantId)
            // SQLite (testes) não ordena por DateTimeOffset; ativas primeiro.
            .OrderByDescending(k => k.Ativa)
            .ThenBy(k => k.Ambiente)
            .Select(k => new AdminApiKeyResponse(
                k.Id, k.Prefixo, k.Descricao,
                k.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
                k.Ativa && k.RevogadoEm == null, k.CriadoEm))
            .ToListAsync(ct);
        return Ok(chaves);
    }

    [HttpPost]
    public async Task<IActionResult> Criar(Guid tenantId, [FromBody] CreateApiKeyRequest req, CancellationToken ct)
    {
        if (!await TenantExisteAsync(tenantId, ct)) return NotFound("Tenant não encontrado.");
        if (req is null || string.IsNullOrWhiteSpace(req.Ambiente))
            return Problem(statusCode: 400, title: "Ambiente é obrigatório (producao|homologacao).");
        if (req.Ambiente != "producao" && req.Ambiente != "homologacao")
            return Problem(statusCode: 422, title: "Ambiente inválido", detail: "Use 'producao' ou 'homologacao'.");

        var key = ApiKeyAuthenticationHandler.GenerateKey(req.Ambiente);
        var apiKey = new ApiKey
        {
            TenantId = tenantId,
            Prefixo = key[..12],
            KeyHash = ApiKeyAuthenticationHandler.HashKey(key),
            Descricao = req.Descricao,
            Ambiente = (short)(req.Ambiente == "producao" ? Ambiente.Producao : Ambiente.Homologacao),
            Ativa = true,
            CriadoEm = DateTimeOffset.UtcNow
        };
        await _db.ApiKeys.AddAsync(apiKey, ct);
        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenantId,
            ApiKeyId = apiKey.Id,
            Acao = "API_KEY_CRIADA",
            RecursoId = apiKey.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Listar), new { tenantId }, new
        {
            id = apiKey.Id,
            chave = key,
            prefixo = apiKey.Prefixo,
            descricao = apiKey.Descricao,
            ambiente = req.Ambiente,
            criadoEm = apiKey.CriadoEm,
            aviso = "Esta é a única vez que a chave completa é exibida. Guarde-a em local seguro."
        });
    }

    [HttpDelete("{keyId:guid}")]
    public async Task<IActionResult> Revogar(Guid tenantId, Guid keyId, CancellationToken ct)
    {
        var apiKey = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.TenantId == tenantId, ct);
        if (apiKey is null) return NotFound();

        await _db.ApiKeys
            .Where(k => k.Id == keyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(k => k.Ativa, false)
                .SetProperty(k => k.RevogadoEm, DateTimeOffset.UtcNow), ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenantId,
            ApiKeyId = keyId,
            Acao = "API_KEY_REVOCADA",
            RecursoId = keyId,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private async Task<bool> TenantExisteAsync(Guid tenantId, CancellationToken ct) =>
        await _db.Tenants.AnyAsync(t => t.Id == tenantId, ct);
}
