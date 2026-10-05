using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Varre documentos em CONTINGENCIA cuja próxima tentativa já venceu e reenfileira
/// ProcessarDocumentoJob. Não toca REJEITADA — regra de negócio não é reprocessada.
/// </summary>
public class VarrerContingenciaJob
{
    private readonly FiscalDbContext _db;
    private readonly IFilaEmissao _fila;
    private readonly ILogger<VarrerContingenciaJob> _logger;

    public VarrerContingenciaJob(
        FiscalDbContext db,
        IFilaEmissao fila,
        ILogger<VarrerContingenciaJob> logger)
    {
        _db = db;
        _fila = fila;
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;

        // Órfãos: crash entre o claim (PROCESSANDO + lease) e o save final —
        // voltam a PENDENTE e vão para a fila novamente.
        var orfaos = await _db.DocumentosFiscais
            .Where(d => d.Status == StatusDocumento.PROCESSANDO
                        && d.ProximaTentativaEm != null
                        && d.ProximaTentativaEm <= agora)
            .Select(d => d.Id)
            .Take(100)
            .ToListAsync(ct);
        if (orfaos.Count > 0)
        {
            _logger.LogWarning("VarrerContingencia: resgatando {Count} documento(s) órfão(s) em PROCESSANDO.", orfaos.Count);
            await _db.DocumentosFiscais
                .Where(d => orfaos.Contains(d.Id) && d.Status == StatusDocumento.PROCESSANDO)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, StatusDocumento.PENDENTE)
                    .SetProperty(d => d.ProximaTentativaEm, (DateTimeOffset?)null), ct);
            foreach (var id in orfaos)
                await _fila.EnfileirarAsync(id, ct);
        }

        var pendentes = await _db.DocumentosFiscais
            .Where(d => d.Status == StatusDocumento.CONTINGENCIA
                        && d.ProximaTentativaEm != null
                        && d.ProximaTentativaEm <= agora)
            .Select(d => d.Id)
            .Take(100)
            .ToListAsync(ct);

        if (pendentes.Count == 0) return;

        _logger.LogInformation("VarrerContingencia: reenfileirando {Count} documento(s).", pendentes.Count);
        foreach (var id in pendentes)
        {
            await _fila.EnfileirarAsync(id, ct);
        }
    }
}
