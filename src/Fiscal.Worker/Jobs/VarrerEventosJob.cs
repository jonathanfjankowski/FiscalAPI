using Fiscal.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Reenfileira eventos PENDENTES cuja proxima_tentativa_em venceu (retry de
/// transmissão de cancelamento/CC-e/inutilização com backoff — mesmo padrão
/// do VarrerContingenciaJob para documentos).
/// </summary>
public class VarrerEventosJob
{
    private readonly FiscalDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<VarrerEventosJob> _logger;

    public VarrerEventosJob(FiscalDbContext db, IBackgroundJobClient jobs, ILogger<VarrerEventosJob> logger)
    {
        _db = db;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        var ids = await _db.EventosFiscais
            .Where(e => e.Status == "PENDENTE" && e.ProximaTentativaEm != null && e.ProximaTentativaEm <= agora)
            .OrderBy(e => e.ProximaTentativaEm)
            .Take(100)
            .Select(e => e.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
            _jobs.Enqueue<ProcessarEventoJob>(j => j.ExecutarAsync(id, CancellationToken.None));

        if (ids.Count > 0)
            _logger.LogInformation("VarrerEventosJob reenfileirou {Quantidade} evento(s).", ids.Count);
    }
}
