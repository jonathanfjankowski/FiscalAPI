using Fiscal.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>Reenfileira manifestações PENDENTES com tentativas anteriores.</summary>
public class VarrerManifestacoesJob
{
    private readonly FiscalDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<VarrerManifestacoesJob> _logger;

    public VarrerManifestacoesJob(FiscalDbContext db, IBackgroundJobClient jobs, ILogger<VarrerManifestacoesJob> logger)
    {
        _db = db;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken ct)
    {
        var ids = await _db.Manifestacoes
            .Where(m => m.Status == "PENDENTE" && m.Tentativas > 0)
            .Take(100)
            .Select(m => m.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
            _jobs.Enqueue<ProcessarManifestacaoJob>(j => j.ExecutarAsync(id, CancellationToken.None));

        if (ids.Count > 0)
            _logger.LogInformation("VarrerManifestacoesJob reenfileirou {Quantidade} manifestação(ões).", ids.Count);
    }
}
