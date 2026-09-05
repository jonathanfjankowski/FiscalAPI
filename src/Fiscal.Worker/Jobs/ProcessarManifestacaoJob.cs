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
    private readonly bool _sandbox;
    private readonly ILogger<ProcessarManifestacaoJob> _logger;

    public ProcessarManifestacaoJob(
        FiscalDbContext db,
        IRepositorioCertificado certRepo,
        IEnumerable<ITransmissorManifestacao> transmissores,
        ICertificadoStore certStore,
        IConfiguration configuration,
        ILogger<ProcessarManifestacaoJob> logger)
    {
        _db = db;
        _certRepo = certRepo;
        _transmissores = transmissores;
        _certStore = certStore;
        _sandbox = configuration.GetValue("Fiscal:ModoSandbox", false);
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 1)]
    public async Task ExecutarAsync(Guid manifestacaoId, CancellationToken ct)
    {
        var manif = await _db.Manifestacoes.FirstOrDefaultAsync(m => m.Id == manifestacaoId, ct);
        if (manif is null || manif.Status is "PROCESSADO" or "REJEITADO")
            return;

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
        if (tenant is null || (cert is null && !_sandbox))
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
            resultado = await ResolverTransmissor().TransmitirAsync(
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
                break;

            default:
                if (manif.Tentativas >= MaxTentativas)
                    manif.Status = "REJEITADO"; // esgotou — sem status terminal FALHA separado
                else
                    manif.Status = "PENDENTE";
                break;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Manifestação {Id} ({Tipo}) → {Status} (tentativa {Tentativa})",
            manif.Id, manif.Tipo, manif.Status, manif.Tentativas);
    }

    private ITransmissorManifestacao ResolverTransmissor() =>
        _transmissores.OfType<TransmissorManifestacaoMock>().FirstOrDefault()
        ?? (ITransmissorManifestacao?)_transmissores.OfType<TransmissorManifestacaoUnimake>().FirstOrDefault()
        ?? _transmissores.First();

    private static string NomeAmigavel(string codigo) => codigo switch
    {
        "210200" => "confirmacao",
        "210210" => "ciencia",
        "210220" => "desconhecimento",
        "210240" => "nao_realizacao",
        _ => codigo,
    };
}
