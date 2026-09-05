using System.Security.Cryptography.X509Certificates;
using Fiscal.Adapters.Unimake;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Pega um documento PENDENTE (ou CONTINGENCIA) e tenta transmitir à SEFAZ via IEmissorFiscal.
/// Persiste o resultado, escreve auditoria, e em caso de erro de transmissão agenda retry
/// (status CONTINGENCIA + ProximaTentativaEm) — sem mexer em REJEITADA (essa não é reprocessada).
/// Em ModoSandbox (EmissorMock) o certificado é opcional — o mock não assina nada.
/// </summary>
public class ProcessarDocumentoJob
{
    private static readonly TimeSpan[] Backoff = new[]
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
    };

    private readonly FiscalDbContext _db;
    private readonly IRepositorioDocumentoFiscal _docRepo;
    private readonly IRepositorioCertificado _certRepo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IEnumerable<IEmissorFiscal> _emissores;
    private readonly ICertificadoStore _certStore;
    private readonly bool _sandbox;
    private readonly ILogger<ProcessarDocumentoJob> _logger;

    public ProcessarDocumentoJob(
        FiscalDbContext db,
        IRepositorioDocumentoFiscal docRepo,
        IRepositorioCertificado certRepo,
        IRepositorioAuditoria auditoria,
        IEnumerable<IEmissorFiscal> emissores,
        ICertificadoStore certStore,
        IConfiguration configuration,
        ILogger<ProcessarDocumentoJob> logger)
    {
        _db = db;
        _docRepo = docRepo;
        _certRepo = certRepo;
        _auditoria = auditoria;
        _emissores = emissores;
        _certStore = certStore;
        _sandbox = configuration.GetValue("Fiscal:ModoSandbox", false);
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 1)]
    public async Task ExecutarAsync(Guid documentoId, CancellationToken ct)
    {
        var doc = await _docRepo.ObterPorIdAsync(documentoId, await TenantIdDoDocumento(documentoId, ct), ct);
        if (doc is null)
        {
            _logger.LogWarning("DocumentoFiscal {Id} não encontrado — descartando job.", documentoId);
            return;
        }

        if (doc.Status is StatusDocumento.AUTORIZADA or StatusDocumento.REJEITADA
            or StatusDocumento.CANCELADA or StatusDocumento.DENEGADA
            or StatusDocumento.ERRO_INTERNO)
        {
            _logger.LogInformation("DocumentoFiscal {Id} já em status terminal {Status} — nada a fazer.", doc.Id, doc.Status);
            return;
        }

        var cert = await _certRepo.ObterAtivoPorTenantAsync(doc.TenantId, ct);
        if (cert is null && !_sandbox)
        {
            // Produção não tem como assinar sem certificado — falha não recuperável.
            doc.Status = StatusDocumento.ERRO_INTERNO;
            doc.MotivoStatus = "Nenhum certificado ativo para o tenant.";
            doc.AtualizadoEm = DateTimeOffset.UtcNow;
            await _docRepo.AtualizarAsync(doc, ct);
            await _db.SaveChangesAsync(ct);
            return;
        }
        // Sandbox sem certificado segue: o EmissorMock não usa o X509.

        doc.Status = StatusDocumento.PROCESSANDO;
        doc.AtualizadoEm = DateTimeOffset.UtcNow;
        await _docRepo.AtualizarAsync(doc, ct);
        await _db.SaveChangesAsync(ct);

        if (cert is not null)
        {
            await _auditoria.RegistrarAsync(new Auditoria
            {
                TenantId = doc.TenantId,
                Acao = "CERTIFICADO_DESCRIPTOGRAFADO",
                RecursoId = cert.Id,
                Detalhe = $"{{\"documentoId\":\"{doc.Id}\"}}"
            }, ct);
        }

        var emissor = ResolverEmissor(doc.Tipo);

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == doc.TenantId, ct);
        if (tenant is null)
        {
            await MarcarErroInternoAsync(doc, $"Tenant {doc.TenantId} não encontrado.", ct);
            return;
        }

        ResultadoEmissao resultado;
        try
        {
            using var x509 = cert is null ? null : await _certStore.CarregarAsync(cert, ct);
            resultado = await emissor.EmitirAsync(doc, tenant, x509!, (Ambiente)doc.Ambiente, ct);
        }
        catch (ErroNaoRecuperavelException ex)
        {
            // Falha que retry não resolve (mapeamento, config faltante, certificado
            // inválido). Não é erro de transmissão — não entra em CONTINGENCIA.
            _logger.LogError(ex, "Erro não recuperável ao emitir documento {Id}.", doc.Id);
            await MarcarErroInternoAsync(doc, ex.Message, ct);
            return;
        }
        catch (NotImplementedException ex)
        {
            // Adapter do tipo de documento não implementado ainda. Não é erro de
            // transmissão — é bug de configuração. Não reprocessa sozinho.
            _logger.LogError(ex, "Emissor {Tipo} lançou NotImplementedException para documento {Id}.",
                doc.Tipo, doc.Id);
            await MarcarErroInternoAsync(doc, ex.Message, ct);
            return;
        }
        catch (Exception ex)
        {
            // Erro de transmissão genuíno (timeout, SEFAZ fora, etc.) → CONTINGENCIA + backoff.
            resultado = new ResultadoEmissao(
                ResultadoEmissaoStatus.ErroTransmissao,
                ChaveAcesso: null,
                ProtocoloAutorizacao: null,
                XmlAssinado: null,
                XmlRetornoSefaz: null,
                Motivo: $"Erro de transmissão: {ex.GetType().Name}: {ex.Message}");
        }

        doc.Tentativas += 1;
        // Só sobrescreve o que o emissor de fato devolveu — no fluxo assíncrono da
        // NFe (consulta de recibo) a segunda passada não tem XML novo.
        if (resultado.ChaveAcesso is not null) doc.ChaveAcesso = resultado.ChaveAcesso;
        if (resultado.ProtocoloAutorizacao is not null) doc.ProtocoloAutorizacao = resultado.ProtocoloAutorizacao;
        if (resultado.XmlAssinado is not null) doc.XmlAssinado = resultado.XmlAssinado;
        if (resultado.XmlRetornoSefaz is not null) doc.XmlRetornoSefaz = resultado.XmlRetornoSefaz;
        if (resultado.XmlGerado is not null) doc.XmlGerado = resultado.XmlGerado;
        if (resultado.ReciboLote is not null) doc.ReciboLote = resultado.ReciboLote;
        doc.MotivoStatus = resultado.Motivo;
        doc.AtualizadoEm = DateTimeOffset.UtcNow;

        switch (resultado.Status)
        {
            case ResultadoEmissaoStatus.Autorizada:
                doc.Status = StatusDocumento.AUTORIZADA;
                doc.ProximaTentativaEm = null;
                break;

            case ResultadoEmissaoStatus.Rejeitada:
                // Regra de negócio — não reprocessar sozinho.
                doc.Status = StatusDocumento.REJEITADA;
                doc.ProximaTentativaEm = null;
                break;

            case ResultadoEmissaoStatus.Denegada:
                doc.Status = StatusDocumento.DENEGADA;
                doc.ProximaTentativaEm = null;
                break;

            case ResultadoEmissaoStatus.ErroTransmissao:
                doc.Status = StatusDocumento.CONTINGENCIA;
                doc.ProximaTentativaEm = DateTimeOffset.UtcNow + ProximoBackoff(doc.Tentativas);
                break;

            default:
                doc.Status = StatusDocumento.ERRO_INTERNO;
                doc.ProximaTentativaEm = null;
                break;
        }

        EnfileirarWebhook(doc, tenant);

        await _docRepo.AtualizarAsync(doc, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("DocumentoFiscal {Id} processado → {Status} (tentativa {Tentativa})",
            doc.Id, doc.Status, doc.Tentativas);
    }

    /// <summary>
    /// Outbox de webhook na mesma transação da mudança de status (padrão
    /// outbox — entrega é feita pelo VarrerWebhooksJob com retry próprio).
    /// </summary>
    private void EnfileirarWebhook(DocumentoFiscal doc, Tenant tenant)
    {
        var tipo = doc.Status switch
        {
            StatusDocumento.AUTORIZADA => Webhooks.EventoAutorizado,
            StatusDocumento.REJEITADA => Webhooks.EventoRejeitado,
            StatusDocumento.DENEGADA => Webhooks.EventoDenegado,
            _ => null,
        };
        if (tipo is null || tenant.WebhookUrl is null) return;

        _db.WebhooksEntrega.Add(new WebhookEntrega
        {
            TenantId = tenant.Id,
            DocumentoId = doc.Id,
            TipoEvento = tipo,
            Payload = Webhooks.PayloadPara(doc, tipo, DateTimeOffset.UtcNow),
            Status = "PENDENTE",
            ProximaTentativaEm = DateTimeOffset.UtcNow,
        });
    }

    private IEmissorFiscal ResolverEmissor(TipoDocumento tipo)
    {
        // Ordem de resolução:
        // 1) EmissorMock (registrado em ModoSandbox=true) — cobre NFE e NFCE.
        // 2) Emissor específico pelo TipoDocumento (NFE → EmissorNFe, NFCE → EmissorNFCe).
        // 3) Qualquer outro emissor registrado (testes podem injetar um único fake).
        var mock = _emissores.OfType<EmissorMock>().FirstOrDefault();
        if (mock is not null) return mock;

        return tipo switch
        {
            TipoDocumento.NFE => _emissores.OfType<EmissorNFe>().FirstOrDefault() ?? _emissores.First(),
            TipoDocumento.NFCE => _emissores.OfType<EmissorNFCe>().FirstOrDefault() ?? _emissores.First(),
            TipoDocumento.NFSE => _emissores.OfType<EmissorNFSe>().FirstOrDefault() ?? _emissores.First(),
            _ => _emissores.First(),
        };
    }

    private async Task<Guid> TenantIdDoDocumento(Guid documentoId, CancellationToken ct) =>
        await _db.DocumentosFiscais
            .Where(d => d.Id == documentoId)
            .Select(d => d.TenantId)
            .FirstAsync(ct);

    private static TimeSpan ProximoBackoff(int tentativa) =>
        tentativa <= 0 ? Backoff[0] : Backoff[Math.Min(tentativa - 1, Backoff.Length - 1)];

    private async Task MarcarErroInternoAsync(DocumentoFiscal doc, string motivo, CancellationToken ct)
    {
        doc.Status = StatusDocumento.ERRO_INTERNO;
        doc.MotivoStatus = motivo;
        doc.Tentativas += 1;
        doc.AtualizadoEm = DateTimeOffset.UtcNow;
        doc.ProximaTentativaEm = null;
        await _docRepo.AtualizarAsync(doc, ct);
        await _db.SaveChangesAsync(ct);
    }
}
