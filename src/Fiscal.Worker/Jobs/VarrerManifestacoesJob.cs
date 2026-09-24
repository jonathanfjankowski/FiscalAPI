using Fiscal.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Reenfileira manifestações PENDENTES cuja proxima_tentativa_em venceu (ou não
/// existe — cobre manifestação nova cujo enqueue inicial se perdeu num restart).
/// Também resgata órfãos em PROCESSANDO cujo lease de execução venceu.
/// </summary>
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
        var agora = DateTimeOffset.UtcNow;

        // Órfãos: crash entre o claim (PROCESSANDO + lease) e o save final.
        var orfaos = await _db.Manifestacoes
            .Where(m => m.Status == "PROCESSANDO" && m.ProximaTentativaEm <= agora)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, "PENDENTE")
                .SetProperty(m => m.ProximaTentativaEm, (DateTimeOffset?)null), ct);
        if (orfaos > 0)
            _logger.LogWarning("VarrerManifestacoesJob resgatou {Quantidade} manifestação(ões) órfã(s) em PROCESSANDO.", orfaos);

        var ids = await _db.Manifestacoes
            .Where(m => m.Status == "PENDENTE"
                && (m.ProximaTentativaEm == null || m.ProximaTentativaEm <= agora))
            .OrderBy(m => m.ProximaTentativaEm)
            .Take(100)
            .Select(m => m.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
            _jobs.Enqueue<ProcessarManifestacaoJob>(j => j.ExecutarAsync(id, CancellationToken.None));

        if (ids.Count > 0)
            _logger.LogInformation("VarrerManifestacoesJob reenfileirou {Quantidade} manifestação(ões).", ids.Count);
    }
}
