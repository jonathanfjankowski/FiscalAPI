using Fiscal.Api.Authentication;
using Fiscal.Persistence;
using Hangfire;
using Fiscal.Worker.Jobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers.Admin;

/// <summary>
/// Visão admin das entregas de webhook de um tenant (outbox) — mesmo modelo do
/// WebhooksController (escopo tenant), mas operado pelo painel via JWT admin.
/// </summary>
[ApiController]
[Route("v1/admin/tenants/{tenantId:guid}/webhooks/entregas")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminWebhooksController : ControllerBase
{
    private const int PageSizeMax = 200;

    private readonly FiscalDbContext _db;
    private readonly IBackgroundJobClient _jobs;

    public AdminWebhooksController(FiscalDbContext db, IBackgroundJobClient jobs)
    {
        _db = db;
        _jobs = jobs;
    }

    public record WebhookEntregaItem(
        Guid Id, string TipoEvento, string Status, Guid? DocumentoId, int Tentativas,
        int? UltimoStatusCode, string? UltimoErro, DateTimeOffset? ProximaTentativaEm,
        DateTimeOffset? EntregueEm, DateTimeOffset CriadoEm);

    /// <summary>
    /// Lista as entregas de webhook do tenant, mais recentes primeiro.
    /// Query: page (1), pageSize (default 50, máx 200), status (PENDENTE |
    /// ENTREGANDO | ENTREGUE | FALHA).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        Guid tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > PageSizeMax) pageSize = PageSizeMax;

        string? statusFiltro = null;
        var statusValidos = new[] { "PENDENTE", "ENTREGANDO", "ENTREGUE", "FALHA" };
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusFiltro = status.Trim().ToUpperInvariant();
            if (!statusValidos.Contains(statusFiltro))
                return Problem(statusCode: 422, title: "Status inválido.",
                    detail: $"Use um status válido: {string.Join(", ", statusValidos)}.");
        }

        var query = _db.WebhooksEntrega.AsNoTracking().Where(w => w.TenantId == tenantId);
        if (statusFiltro is not null)
            query = query.Where(w => w.Status == statusFiltro);

        var ordenada = _db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? query.OrderBy(w => w.Id)
            : query.OrderByDescending(w => w.CriadoEm);

        var itens = await ordenada
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new WebhookEntregaItem(
                w.Id, w.TipoEvento, w.Status, w.DocumentoId, w.Tentativas,
                w.UltimoStatusCode, w.UltimoErro, w.ProximaTentativaEm,
                w.EntregueEm, w.CriadoEm))
            .ToListAsync(ct);

        return Ok(new { page, pageSize, itens });
    }

    /// <summary>
    /// Reenvio manual: FALHA volta a PENDENTE com ciclo novo; PENDENTE tem a
    /// próxima tentativa adiantada. ENTREGUE/ENTREGANDO não reenviam (409).
    /// </summary>
    [HttpPost("{id:guid}/reenviar")]
    public async Task<IActionResult> Reenviar(Guid tenantId, Guid id, CancellationToken ct)
    {
        var entrega = await _db.WebhooksEntrega
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId, ct);
        if (entrega is null) return NotFound();
        var eraFalha = entrega.Status == "FALHA";

        switch (entrega.Status)
        {
            case "ENTREGUE":
                return Problem(statusCode: 409, title: "Entrega já concluída.",
                    detail: $"Entrega {id} foi ENTREGUE em {entrega.EntregueEm:O} — reenvio manual não se aplica.");
            case "ENTREGANDO":
                return Problem(statusCode: 409, title: "Entrega em andamento.",
                    detail: "Aguarde o ciclo atual terminar antes de solicitar reenvio.");
        }

        entrega.Status = "PENDENTE";
        entrega.ProximaTentativaEm = DateTimeOffset.UtcNow;
        if (eraFalha) entrega.Tentativas = 0; // ciclo novo completo
        entrega.UltimoErro = "Reenvio manual solicitado (admin).";
        await _db.SaveChangesAsync(ct);

        _jobs.Enqueue<ProcessarWebhookJob>(j => j.ExecutarAsync(entrega.Id, CancellationToken.None));

        return Accepted($"/v1/webhooks/{entrega.Id}", new
        {
            entregaId = entrega.Id,
            tipoEvento = entrega.TipoEvento,
            status = entrega.Status,
            tentativas = entrega.Tentativas,
            proximaTentativaEm = entrega.ProximaTentativaEm
        });
    }
}
