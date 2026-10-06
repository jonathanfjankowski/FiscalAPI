using Fiscal.Core.Entities;
using Fiscal.Adapters.Unimake;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Transmite uma Manifestação do Destinatário à SEFAZ. PROCESSADO grava o
/// protocolo, atualiza a nota e dispara webhook `manifestacao.processada`
/// (outbox). Erro de transmissão volta a PENDENTE (VarrerManifestacoesJob).
/// </summary>
public class ProcessarManifestacaoJob
{
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];
    private const int MaxTentativas = 6;

    private readonly FiscalDbContext _db;
    private readonly IRepositorioCertificado _certRepo;
    private readonly IEnumerable<ITransmissorManifestacao> _transmissores;
    private readonly ICertificadoStore _certStore;
    private readonly ILogger<ProcessarManifestacaoJob> _logger;

    public ProcessarManifestacaoJob(
        FiscalDbContext db,
        IRepositorioCertificado certRepo,
        IEnumerable<ITransmissorManifestacao> transmissores,
        ICertificadoStore certStore,
        ILogger<ProcessarManifestacaoJob> logger)
    {
        _db = db;
        _certRepo = certRepo;
        _transmissores = transmissores;
        _certStore = certStore;
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 1)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecutarAsync(Guid manifestacaoId, CancellationToken ct)
    {
        // Claim atômico (mesmo padrão do ProcessarDocumentoJob): assume apenas
        // se ainda PENDENTE — evita retransmissão concorrente da manifestação.
        // ProximaTentativaEm vira lease (+15 min): o VarrerManifestacoesJob
        // resgata órfãos em PROCESSANDO quando ela vence.
        DateTimeOffset? leaseAte = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(15);
        var claimado = await _db.Manifestacoes
            .Where(m => m.Id == manifestacaoId && m.Status == "PENDENTE")
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, "PROCESSANDO")
                .SetProperty(m => m.ProximaTentativaEm, leaseAte), ct);
        if (claimado == 0) return;

        var manif = await _db.Manifestacoes.FirstOrDefaultAsync(m => m.Id == manifestacaoId, ct);
        if (manif is null) return;

        var nota = await _db.NotasRecebidas.FirstOrDefaultAsync(n => n.Id == manif.NotaRecebidaId, ct);
        if (nota is null)
        {
            manif.Status = "REJEITADO";
            manif.MotivoStatus = "Nota recebida não encontrada.";
            await _db.SaveChangesAsync(ct);
            return;
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == manif.TenantId, ct);
        var cert = await _certRepo.ObterAtivoPorTenantAsync(manif.TenantId, ct);
        if (tenant is null || (cert is null && !tenant.Sandbox))
        {
            manif.Status = "REJEITADO";
            manif.MotivoStatus = "Tenant/certificado indisponível para transmissão.";
            await _db.SaveChangesAsync(ct);
            return;
        }

        manif.Status = "PROCESSANDO";
        await _db.SaveChangesAsync(ct);

        ResultadoEvento resultado;
        try
        {
            using var x509 = cert is null ? null : await _certStore.CarregarAsync(cert, ct);
            resultado = await ResolverTransmissor(tenant.Sandbox).TransmitirAsync(
                manif, nota, tenant, x509, (Ambiente)nota.Ambiente, ct);
        }
        catch (ErroNaoRecuperavelException ex)
        {
            manif.Status = "REJEITADO";
            manif.MotivoStatus = ex.Message;
            await _db.SaveChangesAsync(ct);
            return;
        }
        catch (Exception ex)
        {
            resultado = new ResultadoEvento(
                ResultadoEventoStatus.ErroTransmissao, null, null,
                $"Erro de transmissão: {ex.GetType().Name}: {ex.Message}");
        }

        manif.Tentativas += 1;
        if (resultado.Protocolo is not null) manif.Protocolo = resultado.Protocolo;
        manif.MotivoStatus = resultado.Motivo;

        switch (resultado.Status)
        {
            case ResultadoEventoStatus.Processado:
                manif.Status = "PROCESSADO";
                manif.ProximaTentativaEm = null;
                nota.ManifestacaoAtual = NomeAmigavel(manif.Tipo);
                if (tenant.WebhookUrl is not null)
                {
                    _db.WebhooksEntrega.Add(new WebhookEntrega
                    {
                        TenantId = tenant.Id,
                        DocumentoId = null,
                        TipoEvento = Webhooks.ManifestacaoProcessada,
                        Payload = Webhooks.PayloadManifestacao(nota, manif, DateTimeOffset.UtcNow),
                        Status = "PENDENTE",
                        ProximaTentativaEm = DateTimeOffset.UtcNow,
                    });
                }
                break;

            case ResultadoEventoStatus.Rejeitado:
                manif.Status = "REJEITADO";
                manif.ProximaTentativaEm = null;
                break;

            default:
                if (manif.Tentativas >= MaxTentativas)
                {
                    manif.Status = "REJEITADO"; // esgotou — sem status terminal FALHA separado
                    manif.ProximaTentativaEm = null;
                }
                else
                {
                    // Backoff real: o varredor só reenfileira quando vencer.
                    manif.Status = "PENDENTE";
                    manif.ProximaTentativaEm = DateTimeOffset.UtcNow + ProximoBackoff(manif.Tentativas);
                }
                break;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Manifestação {Id} ({Tipo}) → {Status} (tentativa {Tentativa})",
            manif.Id, manif.Tipo, manif.Status, manif.Tentativas);
    }

    private ITransmissorManifestacao ResolverTransmissor(bool sandbox) =>
        sandbox
            ? _transmissores.OfType<TransmissorManifestacaoMock>().FirstOrDefault()
                ?? (ITransmissorManifestacao?)_transmissores.OfType<TransmissorManifestacaoUnimake>().FirstOrDefault()
                ?? _transmissores.First()
            : _transmissores.OfType<TransmissorManifestacaoUnimake>().FirstOrDefault()
                ?? _transmissores.First();

    private static TimeSpan ProximoBackoff(int tentativa) =>
        tentativa <= 0 ? Backoff[0] : Backoff[Math.Min(tentativa - 1, Backoff.Length - 1)];

    private static string NomeAmigavel(string codigo) => codigo switch
    {
        "210200" => "confirmacao",
        "210210" => "ciencia",
        "210220" => "desconhecimento",
        "210240" => "nao_realizacao",
        _ => codigo,
    };
}
