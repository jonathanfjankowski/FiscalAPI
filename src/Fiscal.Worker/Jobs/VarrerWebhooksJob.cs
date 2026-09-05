using Fiscal.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Reenfileira entregas de webhook PENDENTES cuja proxima_tentativa_em venceu.
/// </summary>
public class VarrerWebhooksJob
{
    private readonly FiscalDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<VarrerWebhooksJob> _logger;

    public VarrerWebhooksJob(FiscalDbContext db, IBackgroundJobClient jobs, ILogger<VarrerWebhooksJob> logger)
    {
        _db = db;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        var ids = await _db.WebhooksEntrega
            .Where(w => w.Status == "PENDENTE" && w.ProximaTentativaEm != null && w.ProximaTentativaEm <= agora)
            .OrderBy(w => w.ProximaTentativaEm)
            .Take(100)
            .Select(w => w.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
            _jobs.Enqueue<ProcessarWebhookJob>(j => j.ExecutarAsync(id, CancellationToken.None));

        if (ids.Count > 0)
            _logger.LogInformation("VarrerWebhooksJob reenfileirou {Quantidade} entrega(s).", ids.Count);
    }
}
