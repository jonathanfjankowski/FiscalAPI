using Fiscal.Core.Interfaces;
using Fiscal.Worker.Jobs;
using Hangfire;

namespace Fiscal.Worker.Fila;

/// <summary>
/// IFilaEmissao via Hangfire. Enfileira ProcessarDocumentoJob sem delay adicional
/// (o delay mora no documento.ProximaTentativaEm, controlado pelo VarrerContingenciaJob).
/// </summary>
public class HangfireFilaEmissao : IFilaEmissao
{
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<HangfireFilaEmissao> _logger;

    public HangfireFilaEmissao(IBackgroundJobClient jobs, ILogger<HangfireFilaEmissao> logger)
    {
        _jobs = jobs;
        _logger = logger;
    }

    public Task EnfileirarAsync(Guid documentoId, CancellationToken cancellationToken)
    {
        var jobId = _jobs.Enqueue<ProcessarDocumentoJob>(j => j.ExecutarAsync(documentoId, CancellationToken.None));
        _logger.LogInformation("Enfileirado ProcessarDocumentoJob {JobId} para documento {DocumentoId}.", jobId, documentoId);
        return Task.CompletedTask;
    }
}
