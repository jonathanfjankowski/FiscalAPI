using Fiscal.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Reenfileira eventos PENDENTES: os com proxima_tentativa_em vencida (retry de
/// transmissão com backoff — mesmo padrão do VarrerContingenciaJob para documentos)
/// e os sem agenda (redes de segurança para eventos criados cujo enqueue se perdeu,
/// ex.: restart do Hangfire entre o SaveChanges e o Enqueue).
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

        // SQLite (suíte de testes) não traduz comparação de DateTimeOffset —
        // o resgate de órfãos e a varredura com agenda só rodam no Postgres.
        var ehSqlite = _db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        if (!ehSqlite)
        {
            // Órfãos: crash entre o claim (PROCESSANDO + lease) e o save final.
            var orfaos = await _db.EventosFiscais
                .Where(e => e.Status == "PROCESSANDO" && e.ProximaTentativaEm <= agora)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, "PENDENTE")
                    .SetProperty(e => e.ProximaTentativaEm, (DateTimeOffset?)null), ct);
            if (orfaos > 0)
                _logger.LogWarning("VarrerEventosJob resgatou {Quantidade} evento(s) órfão(s) em PROCESSANDO.", orfaos);
        }

        // Sem agenda (ProximaTentativaEm == null) cobre eventos cujo enqueue se
        // perdeu; com agenda vencida cobre o backoff de retry.
        var baseQuery = _db.EventosFiscais.Where(e => e.Status == "PENDENTE" && e.ProximaTentativaEm == null);
        if (!ehSqlite)
            baseQuery = _db.EventosFiscais.Where(e => e.Status == "PENDENTE"
                && (e.ProximaTentativaEm == null || e.ProximaTentativaEm <= agora));

        var ordenada = ehSqlite
            ? baseQuery.OrderBy(e => e.Id) // SQLite não traduz ORDER BY DateTimeOffset
            : baseQuery.OrderBy(e => e.ProximaTentativaEm);

        var ids = await ordenada
            .Take(100)
            .Select(e => e.Id)
            .ToListAsync(ct);

        foreach (var id in ids)
            _jobs.Enqueue<ProcessarEventoJob>(j => j.ExecutarAsync(id, CancellationToken.None));

        if (ids.Count > 0)
            _logger.LogInformation("VarrerEventosJob reenfileirou {Quantidade} evento(s).", ids.Count);
    }
}
