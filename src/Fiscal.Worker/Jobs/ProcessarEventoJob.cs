using Fiscal.Adapters.Unimake;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Transmite um EventoFiscal (cancelamento/CC-e/inutilização) à SEFAZ via
/// ITransmissorEventoFiscal. Sucesso: evento PROCESSADO e, no cancelamento,
/// documento → CANCELADA. Rejeição: evento REJEITADO e documento →
/// ERRO_CANCELAMENTO. Erro de transmissão: volta a PENDENTE com backoff
/// (VarrerEventosJob reenfileira).
/// </summary>
public class ProcessarEventoJob
{
    private static readonly TimeSpan[] Backoff = new[]
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
    };

    /// <summary>Teto de tentativas (~8h de backoff máx. 10 min): esgotou → ERRO
    /// (terminal) — nunca retry infinito.</summary>
    private const int MaxTentativas = 48;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly FiscalDbContext _db;
    private readonly IRepositorioCertificado _certRepo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IEnumerable<ITransmissorEventoFiscal> _transmissores;
    private readonly ICertificadoStore _certStore;
    private readonly MetricasFiscais _metricas;
    private readonly bool _sandbox;
    private readonly ILogger<ProcessarEventoJob> _logger;

    public ProcessarEventoJob(
        FiscalDbContext db,
        IRepositorioCertificado certRepo,
        IRepositorioAuditoria auditoria,
        IEnumerable<ITransmissorEventoFiscal> transmissores,
        ICertificadoStore certStore,
        MetricasFiscais metricas,
        IConfiguration configuration,
        ILogger<ProcessarEventoJob> logger)
    {
        _db = db;
        _certRepo = certRepo;
        _auditoria = auditoria;
        _transmissores = transmissores;
        _certStore = certStore;
        _metricas = metricas;
        _sandbox = configuration.GetValue("Fiscal:ModoSandbox", false);
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 1)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecutarAsync(Guid eventoId, CancellationToken ct)
    {
        // Claim atômico (mesmo padrão do ProcessarDocumentoJob): assume o evento
        // apenas se ainda estiver PENDENTE. Se 0 linhas, outra execução já assumiu
        // ou o evento chegou a status terminal — abortar sem retransmitir.
        // ProximaTentativaEm vira lease (+15 min): o VarrerEventosJob resgata
        // órfãos em PROCESSANDO quando ela vence.
        DateTimeOffset? leaseAte = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(15);
        var claimado = await _db.EventosFiscais
            .Where(e => e.Id == eventoId && e.Status == "PENDENTE")
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, "PROCESSANDO")
                .SetProperty(e => e.ProximaTentativaEm, leaseAte), ct);
        if (claimado == 0)
        {
            _logger.LogInformation("EventoFiscal {Id} não está mais PENDENTE — nada a fazer.", eventoId);
            return;
        }

        var evento = await _db.EventosFiscais.FirstOrDefaultAsync(e => e.Id == eventoId, ct);
        if (evento is null)
        {
            _logger.LogWarning("EventoFiscal {Id} não encontrado após claim — descartando job.", eventoId);
            return;
        }

        DocumentoFiscal? doc = null;
        if (evento.DocumentoId is { } docId)
            doc = await _db.DocumentosFiscais.FirstOrDefaultAsync(d => d.Id == docId, ct);

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == evento.TenantId, ct);
        if (tenant is null)
        {
            await MarcarErroAsync(evento, doc, $"Tenant {evento.TenantId} não encontrado.", ct);
            return;
        }

        var cert = await _certRepo.ObterAtivoPorTenantAsync(evento.TenantId, ct);
        if (cert is null && !_sandbox)
        {
            // Produção não assina o evento sem certificado — falha não recuperável.
            await MarcarErroAsync(evento, doc, "Nenhum certificado ativo para o tenant.", ct);
            return;
        }
        // Sandbox sem certificado segue: o TransmissorEventoMock não usa o X509.

        evento.Status = "PROCESSANDO";
        await _db.SaveChangesAsync(ct);

        ResultadoEvento resultado;
        try
        {
            using var x509 = cert is null ? null : await _certStore.CarregarAsync(cert, ct);
            resultado = await ResolverTransmissor().TransmitirAsync(
                evento, doc, tenant, x509!, ResolverAmbiente(evento, doc), ct);
        }
        catch (ErroNaoRecuperavelException ex)
        {
            _logger.LogError(ex, "Erro não recuperável ao transmitir evento {Id} ({Tipo}).",
                evento.Id, evento.TipoEvento);
            await MarcarErroAsync(evento, doc, ex.Message, ct);
            return;
        }
        catch (Exception ex)
        {
            resultado = new ResultadoEvento(
                ResultadoEventoStatus.ErroTransmissao,
                Protocolo: null, XmlRetorno: null,
                Motivo: $"Erro de transmissão: {ex.GetType().Name}: {ex.Message}");
        }

        evento.Tentativas += 1;
        if (resultado.Protocolo is not null) evento.Protocolo = resultado.Protocolo;
        if (resultado.XmlRetorno is not null) evento.XmlRetorno = resultado.XmlRetorno;
        evento.MotivoStatus = resultado.Motivo;

        switch (resultado.Status)
        {
            case ResultadoEventoStatus.Processado:
                evento.Status = "PROCESSADO";
                evento.ProximaTentativaEm = null;
                if (doc is not null && evento.TipoEvento == "CANCELAMENTO")
                {
                    doc.Status = StatusDocumento.CANCELADA;
                    doc.AtualizadoEm = DateTimeOffset.UtcNow;
                }
                if (doc is not null && evento.TipoEvento is "CANCELAMENTO" or "CCE")
                {
                    EnfileirarWebhook(doc, evento, tenant, evento.TipoEvento == "CANCELAMENTO"
                        ? Webhooks.EventoCancelado
                        : Webhooks.EventoCartaCorrecao);
                }
                await _auditoria.RegistrarAsync(new Auditoria
                {
                    TenantId = evento.TenantId,
                    Acao = "EVENTO_PROCESSADO",
                    RecursoId = evento.Id,
                    Detalhe = $"{{\"tipo\":\"{evento.TipoEvento}\",\"protocolo\":\"{evento.Protocolo}\"}}"
                }, ct);
                break;

            case ResultadoEventoStatus.Rejeitado:
                evento.Status = "REJEITADO";
                evento.ProximaTentativaEm = null;
                if (doc is not null && evento.TipoEvento == "CANCELAMENTO")
                {
                    doc.Status = StatusDocumento.ERRO_CANCELAMENTO;
                    doc.AtualizadoEm = DateTimeOffset.UtcNow;
                }
                await _auditoria.RegistrarAsync(new Auditoria
                {
                    TenantId = evento.TenantId,
                    Acao = "EVENTO_REJEITADO",
                    RecursoId = evento.Id,
                    Detalhe = $"{{\"tipo\":\"{evento.TipoEvento}\",\"motivo\":\"{Resumo(evento.MotivoStatus)}\"}}"
                }, ct);
                break;

            default:
                if (evento.Tentativas >= MaxTentativas)
                {
                    // Esgotou — terminal; cancelamento rejeitado por esgotamento
                    // deixa o documento em ERRO_CANCELAMENTO.
                    evento.Status = "ERRO";
                    evento.ProximaTentativaEm = null;
                    if (doc is not null && evento.TipoEvento == "CANCELAMENTO")
                    {
                        doc.Status = StatusDocumento.ERRO_CANCELAMENTO;
                        doc.AtualizadoEm = DateTimeOffset.UtcNow;
                    }
                    _logger.LogError("EventoFiscal {Id} esgotou {Max} tentativas de transmissão — ERRO.",
                        evento.Id, MaxTentativas);
                }
                else
                {
                    // Erro de transmissão — volta a PENDENTE; VarrerEventosJob reenfileira.
                    evento.Status = "PENDENTE";
                    evento.ProximaTentativaEm = DateTimeOffset.UtcNow + ProximoBackoff(evento.Tentativas);
                }
                break;
        }

        await _db.SaveChangesAsync(ct);
        _metricas.EventoProcessado(evento.TipoEvento, evento.Status);
        _logger.LogInformation("EventoFiscal {Id} ({Tipo}) processado → {Status} (tentativa {Tentativa})",
            evento.Id, evento.TipoEvento, evento.Status, evento.Tentativas);
    }

    private ITransmissorEventoFiscal ResolverTransmissor()
    {
        // Mesma ordem do ResolverEmissor: mock (sandbox) → Unimake → qualquer.
        var mock = _transmissores.OfType<TransmissorEventoMock>().FirstOrDefault();
        if (mock is not null) return mock;
        return _transmissores.OfType<TransmissorEventoUnimake>().FirstOrDefault() ?? _transmissores.First();
    }

    private static Ambiente ResolverAmbiente(EventoFiscal evento, DocumentoFiscal? doc)
    {
        if (evento.TipoEvento == "INUTILIZACAO" && evento.DadosEvento is not null)
        {
            var dados = JsonSerializer.Deserialize<InutilizacaoDados>(evento.DadosEvento, JsonOpts);
            if (dados is not null)
                return dados.Ambiente == "producao" ? Ambiente.Producao : Ambiente.Homologacao;
        }

        if (doc is not null) return (Ambiente)doc.Ambiente;
        return Ambiente.Homologacao;
    }

    private static TimeSpan ProximoBackoff(int tentativa) =>
        tentativa <= 0 ? Backoff[0] : Backoff[Math.Min(tentativa - 1, Backoff.Length - 1)];

    private async Task MarcarErroAsync(EventoFiscal evento, DocumentoFiscal? doc, string motivo, CancellationToken ct)
    {
        evento.Status = "ERRO";
        evento.MotivoStatus = motivo;
        evento.Tentativas += 1;
        evento.ProximaTentativaEm = null;
        if (doc is not null && evento.TipoEvento == "CANCELAMENTO")
        {
            doc.Status = StatusDocumento.ERRO_CANCELAMENTO;
            doc.AtualizadoEm = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string Resumo(string? texto) =>
        texto is null ? "" : texto.Length <= 80 ? texto : texto[..80].Replace("\"", "'");

    /// <summary>Outbox de webhook na mesma SaveChanges do resultado do evento.</summary>
    private void EnfileirarWebhook(DocumentoFiscal doc, EventoFiscal evento, Tenant tenant, string tipo)
    {
        if (tenant.WebhookUrl is null) return;

        _db.WebhooksEntrega.Add(new WebhookEntrega
        {
            TenantId = tenant.Id,
            DocumentoId = doc.Id,
            TipoEvento = tipo,
            Payload = Webhooks.PayloadEventoPara(doc, evento, tipo, DateTimeOffset.UtcNow),
            Status = "PENDENTE",
            ProximaTentativaEm = DateTimeOffset.UtcNow,
        });
    }
}
