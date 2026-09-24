using Fiscal.Api.Authentication;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fiscal.Api.Controllers;

[ApiController]
[Route("v1/api-keys")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.SchemeName)]
public class ApiKeysController : ControllerBase
{
    private readonly IRepositorioApiKey _repo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly FiscalDbContext _db;

    public ApiKeysController(IRepositorioApiKey repo, IRepositorioAuditoria auditoria, FiscalDbContext db)
    {
        _repo = repo;
        _auditoria = auditoria;
        _db = db;
    }

    public record CreateApiKeyRequest(string? Descricao, string Ambiente);

    public record ApiKeyResponse(Guid Id, string Prefixo, string? Descricao, string Ambiente, bool Ativa, DateTimeOffset CriadoEm);

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CreateApiKeyRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Ambiente))
            return Problem(statusCode: 400, title: "Ambiente é obrigatório (producao|homologacao).");

        if (req.Ambiente != "producao" && req.Ambiente != "homologacao")
            return Problem(statusCode: 422, title: "Ambiente inválido", detail: "Use 'producao' ou 'homologacao'.");

        // Separação de privilégio entre ambientes: key de homologação vazada não
        // pode mintar key de produção do tenant. Só admin (endpoint próprio) cria
        // chave de produção sem ter uma em mãos.
        if (req.Ambiente == "producao" && HttpContext.GetAmbiente() != Ambiente.Producao)
            return Problem(statusCode: 403,
                title: "API Key de homologação não pode criar chave de produção.",
                detail: "Use uma chave de produção já existente ou solicite ao administrador via painel.");

        var key = ApiKeyAuthenticationHandler.GenerateKey(req.Ambiente);
        var hash = ApiKeyAuthenticationHandler.HashKey(key);
        var apiKey = new ApiKey
        {
            TenantId = HttpContext.GetTenantId(),
            Prefixo = key[..12],
            KeyHash = hash,
            Descricao = req.Descricao,
            Ambiente = (short)(req.Ambiente == "producao" ? Ambiente.Producao : Ambiente.Homologacao),
            Ativa = true,
            CriadoEm = DateTimeOffset.UtcNow
        };

        await _repo.AdicionarAsync(apiKey, ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = apiKey.TenantId,
            ApiKeyId = apiKey.Id,
            Acao = "API_KEY_CRIADA",
            RecursoId = apiKey.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        // BUG corrigido (pego pelos testes de segurança): AdicionarAsync não
        // persiste sozinho — sem SaveChanges a chave respondida em 201 nunca
        // existia no banco.
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Listar), new { }, new
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

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var chaves = await _repo.ListarPorTenantAsync(tenantId, ct);
        var resp = chaves.Select(k => new ApiKeyResponse(
            k.Id, k.Prefixo, k.Descricao,
            k.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
            k.Ativa && k.RevogadoEm is null, k.CriadoEm));
        return Ok(resp);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revogar(Guid id, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var existente = (await _repo.ListarPorTenantAsync(tenantId, ct)).FirstOrDefault(k => k.Id == id);
        if (existente is null) return NotFound();

        await _repo.RevogarAsync(id, ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenantId,
            ApiKeyId = id,
            Acao = "API_KEY_REVOCADA",
            RecursoId = id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
