using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

/// <summary>
/// Trilha de auditoria — <b>somente leitura</b>.
///
/// A tabela tem trigger de append-only no banco (migration
/// <c>EnforceAuditoriaAppendOnly</c>): não existe update nem delete. Este
/// controller não escreve — a escrita acontece nos pontos de ação, via
/// <see cref="IRepositorioAuditoria"/>.
/// </summary>
[ApiController]
[Route("v1/admin/auditoria")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminAuditoriaController : ControllerBase
{
    private const int PageSizeMax = 200;

    private readonly IRepositorioAuditoria _auditoria;
    private readonly FiscalDbContext _db;

    public AdminAuditoriaController(IRepositorioAuditoria auditoria, FiscalDbContext db)
    {
        _auditoria = auditoria;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? acao,
        [FromQuery] string? recursoId,
        [FromQuery] DateTimeOffset? de,
        [FromQuery] DateTimeOffset? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > PageSizeMax) pageSize = PageSizeMax;

        var (total, itens) = await _auditoria.ListarAsync(
            tenantId, acao, recursoId, de, ate, page, pageSize, ct);

        // Nome da empresa numa segunda consulta restrita aos tenants da página
        // (evita JOIN que o SQLite não traduz e não infla a query principal).
        var tenantIds = itens.Where(i => i.TenantId is not null)
            .Select(i => i.TenantId!.Value).Distinct().ToList();
        var nomes = await _db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.RazaoSocial })
            .ToDictionaryAsync(t => t.Id, t => t.RazaoSocial, ct);

        return Ok(new
        {
            total,
            page,
            pageSize,
            itens = itens.Select(a => new
            {
                a.Id,
                a.TenantId,
                tenantRazaoSocial = a.TenantId is not null && nomes.TryGetValue(a.TenantId.Value, out var nome)
                    ? nome
                    : null,
                a.ApiKeyId,
                a.Acao,
                a.RecursoId,
                ipOrigem = a.IpOrigem?.ToString(),
                a.Detalhe,
                a.CriadoEm,
            }),
        });
    }
}