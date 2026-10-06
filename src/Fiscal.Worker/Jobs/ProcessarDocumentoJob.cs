using System.Security.Cryptography.X509Certificates;
using Fiscal.Adapters.Unimake;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Pega um documento PENDENTE (ou CONTINGENCIA) e tenta transmitir à SEFAZ via IEmissorFiscal.
/// Persiste o resultado, escreve auditoria, e em caso de erro de transmissão agenda retry
/// (status CONTINGENCIA + ProximaTentativaEm) — sem mexer em REJEITADA (essa não é reprocessada).
/// Tenant em sandbox (EmissorMock) dispensa certificado — o mock não assina nada.
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

    /// <summary>Teto de tentativas em CONTINGENCIA (~8h de backoff máx. 10 min):
    /// esgotou → FALHA_EMISSAO (terminal) + webhook — nunca retry infinito.</summary>
    private const int MaxTentativas = 48;


    private readonly FiscalDbContext _db;
    private readonly IRepositorioDocumentoFiscal _docRepo;
    private readonly IRepositorioCertificado _certRepo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IEnumerable<IEmissorFiscal> _emissores;
    private readonly IEnumerable<ITransmissorEpec> _transmissoresEpec;
    private readonly ICertificadoStore _certStore;
    private readonly MetricasFiscais _metricas;
    private readonly ILogger<ProcessarDocumentoJob> _logger;

    public ProcessarDocumentoJob(
        FiscalDbContext db,
        IRepositorioDocumentoFiscal docRepo,
        IRepositorioCertificado certRepo,
        IRepositorioAuditoria auditoria,
        IEnumerable<IEmissorFiscal> emissores,
        IEnumerable<ITransmissorEpec> transmissoresEpec,
        ICertificadoStore certStore,
        MetricasFiscais metricas,
        ILogger<ProcessarDocumentoJob> logger)
    {
        _db = db;
        _docRepo = docRepo;
        _certRepo = certRepo;
        _auditoria = auditoria;
        _emissores = emissores;
        _transmissoresEpec = transmissoresEpec;
        _certStore = certStore;
        _metricas = metricas;
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 1)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecutarAsync(Guid documentoId, CancellationToken ct)
    {
        // Claim atômico: assume o documento apenas se ainda estiver enfileirável
        // (PENDENTE/CONTINGENCIA). Se 0 linhas, outra execução já assumiu (corrida
        // varredor × replay × retry do Hangfire) ou o doc chegou a status terminal
        // — abortar sem retransmitir (evita duplicidade e sobrescrita de sucesso).
        // ProximaTentativaEm vira lease (+15 min): se a execução morrer no meio,
        // o VarrerContingenciaJob resgata o órfão em PROCESSANDO.
        DateTimeOffset? leaseAte = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(15);
        var claimado = await _db.DocumentosFiscais
            .Where(d => d.Id == documentoId
                && (d.Status == StatusDocumento.PENDENTE || d.Status == StatusDocumento.CONTINGENCIA))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, StatusDocumento.PROCESSANDO)
                .SetProperty(d => d.ProximaTentativaEm, leaseAte)
                .SetProperty(d => d.AtualizadoEm, DateTimeOffset.UtcNow), ct);
        if (claimado == 0)
        {
            _logger.LogInformation("DocumentoFiscal {Id} não está mais PENDENTE/CONTINGENCIA — nada a fazer.", documentoId);
            return;
        }

        var doc = await _db.DocumentosFiscais.FirstOrDefaultAsync(d => d.Id == documentoId, ct);
        if (doc is null)
        {
            _logger.LogWarning("DocumentoFiscal {Id} não encontrado após claim — descartando job.", documentoId);
            return;
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == doc.TenantId, ct);
        if (tenant is null)
        {
            await MarcarErroInternoAsync(doc, $"Tenant {doc.TenantId} não encontrado.", ct);
            return;
        }

        // Sandbox é por tenant (Tenant.Sandbox): mock não fala com SEFAZ e não
        // exige certificado — a escolha do emissor acontece em ResolverEmissor.
        var sandbox = tenant.Sandbox;

        // Janela de contingência: OFFLINE (NFC-e tpEmis 9) = 24h; demais
        // (SVC/EPEC) = 168h. Fora da janela a SEFAZ rejeita — falha alto
        // em vez de rejeição em loop.
        var janela = doc.ModoContingencia switch
        {
            "OFFLINE" => TimeSpan.FromHours(24),
            null => TimeSpan.Zero,
            _ => TimeSpan.FromHours(168),
        };
        if (janela > TimeSpan.Zero && doc.CriadoEm < DateTimeOffset.UtcNow - janela)
        {
            _logger.LogError("DocumentoFiscal {Id}: janela de contingência ({Modo}) expirada — marcando FALHA_EMISSAO.",
                doc.Id, doc.ModoContingencia);
            await MarcarErroTerminalAsync(doc,
                $"Janela de contingência ({doc.ModoContingencia}) expirada — reemita o documento com nova numeração.", ct);
            return;
        }

        // Contingência EPEC: o evento prévio (110140, SVRS) precisa ser
        // autorizado ANTES da transmissão da NF-e completa (tpEmis 4).
        // Sucesso → protocolo salvo; a NF-e vai no próximo ciclo.
        if (doc.ModoContingencia == "EPEC" && doc.EpecProtocolo is null && !sandbox)
        {
            var epecOk = await TransmitirEpecAsync(doc, ct);
            if (!epecOk) return; // CONTINGENCIA/ERRO já persistidos em TransmitirEpecAsync
            doc.Status = StatusDocumento.CONTINGENCIA;
            doc.ProximaTentativaEm = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
            await _docRepo.AtualizarAsync(doc, ct);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("DocumentoFiscal {Id}: EPEC autorizado — NF-e completa entra na fila.", doc.Id);
            return;
        }

        var cert = await _certRepo.ObterAtivoPorTenantAsync(doc.TenantId, ct);
        if (cert is null && !sandbox)
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

        var emissor = ResolverEmissor(doc.Tipo, sandbox);

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
            _logger.LogError(ex, "Erro de transmissão ao emitir documento {Id} (tentativa {Tentativa}).",
                doc.Id, doc.Tentativas);
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
                if (doc.Tentativas >= MaxTentativas)
                {
                    // Esgotou — terminal + webhook; não volta mais à fila sozinho.
                    doc.Status = StatusDocumento.FALHA_EMISSAO;
                    doc.ProximaTentativaEm = null;
                    _logger.LogError("DocumentoFiscal {Id} esgotou {Max} tentativas de transmissão — FALHA_EMISSAO.",
                        doc.Id, MaxTentativas);
                }
                else
                {
                    doc.Status = StatusDocumento.CONTINGENCIA;
                    doc.ProximaTentativaEm = DateTimeOffset.UtcNow + ProximoBackoff(doc.Tentativas);
                    _metricas.ContingenciaAcionada(doc.ModoContingencia ?? "fila");
                }
                break;

            default:
                doc.Status = StatusDocumento.ERRO_INTERNO;
                doc.ProximaTentativaEm = null;
                break;
        }

        EnfileirarWebhook(doc, tenant);

        await _docRepo.AtualizarAsync(doc, ct);
        await _db.SaveChangesAsync(ct);

        _metricas.DocumentoProcessado(doc.Tipo, doc.Status, tenant.Uf, doc.Ambiente == (short)Ambiente.Producao);
        if (doc.Status == StatusDocumento.AUTORIZADA)
        {
            _metricas.LatenciaAutorizacao(
                (doc.AtualizadoEm - doc.CriadoEm).TotalSeconds, doc.Tipo, tenant.Uf);
        }

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
            StatusDocumento.FALHA_EMISSAO => Webhooks.EventoFalhaEmissao,
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

    private IEmissorFiscal ResolverEmissor(TipoDocumento tipo, bool sandbox)
    {
        // Ordem de resolução:
        // 1) Tenant em sandbox → EmissorMock (cobre NFE e NFCE).
        // 2) Emissor específico pelo TipoDocumento (NFE → EmissorNFe, NFCE → EmissorNFCe).
        // 3) Qualquer outro emissor registrado (testes podem injetar um único fake).
        if (sandbox)
        {
            var mock = _emissores.OfType<EmissorMock>().FirstOrDefault();
            if (mock is not null) return mock;
        }

        return tipo switch
        {
            TipoDocumento.NFE => _emissores.OfType<EmissorNFe>().FirstOrDefault() ?? _emissores.First(),
            TipoDocumento.NFCE => _emissores.OfType<EmissorNFCe>().FirstOrDefault() ?? _emissores.First(),
            TipoDocumento.NFSE => _emissores.OfType<EmissorNFSe>().FirstOrDefault() ?? _emissores.First(),
            _ => _emissores.First(),
        };
    }

    private static TimeSpan ProximoBackoff(int tentativa) =>
        tentativa <= 0 ? Backoff[0] : Backoff[Math.Min(tentativa - 1, Backoff.Length - 1)];

    /// <summary>
    /// Envia o evento prévio EPEC (110140) para a SVRS. true = autorizado
    /// (protocolo salvo); false = falha já persistida (documento volta a
    /// CONTINGENCIA com backoff — ou ERRO_INTERNO nos casos não recuperáveis).
    /// </summary>
    private async Task<bool> TransmitirEpecAsync(DocumentoFiscal doc, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == doc.TenantId, ct);
        if (tenant is null)
        {
            await MarcarErroInternoAsync(doc, $"Tenant {doc.TenantId} não encontrado (EPEC).", ct);
            return false;
        }
        var cert = await _certRepo.ObterAtivoPorTenantAsync(doc.TenantId, ct);
        if (cert is null)
        {
            doc.MotivoStatus = "EPEC sem certificado ativo para o tenant.";
            doc.AtualizadoEm = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return false;
        }

        var transmissor = _transmissoresEpec.FirstOrDefault()
            ?? throw new ErroNaoRecuperavelException("Nenhum transmissor EPEC registrado.");
        doc.Status = StatusDocumento.CONTINGENCIA;
        doc.ProximaTentativaEm = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        try
        {
            using var x509 = await _certStore.CarregarAsync(cert, ct);
            var resultado = await transmissor.TransmitirAsync(doc, tenant, x509, (Ambiente)doc.Ambiente, ct);

            if (resultado.Status == ResultadoEventoStatus.Processado && resultado.Protocolo is not null)
            {
                doc.EpecProtocolo = resultado.Protocolo;
                doc.XmlRetornoSefaz = resultado.XmlRetorno;
                doc.MotivoStatus = $"EPEC autorizado ({resultado.Protocolo}); NF-e completa será transmitida em seguida.";
                doc.AtualizadoEm = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                _metricas.ContingenciaAcionada("epec");
                return true;
            }

            doc.Status = StatusDocumento.CONTINGENCIA;
            doc.ProximaTentativaEm = DateTimeOffset.UtcNow + ProximoBackoff(doc.Tentativas + 1);
            doc.MotivoStatus = resultado.Status == ResultadoEventoStatus.Rejeitado
                ? $"EPEC rejeitado: {resultado.Motivo}"
                : $"Erro de transmissão do EPEC: {resultado.Motivo}";
            doc.XmlRetornoSefaz = resultado.XmlRetorno;
            doc.AtualizadoEm = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return false;
        }
        catch (ErroNaoRecuperavelException ex)
        {
            _logger.LogError(ex, "Erro não recuperável no EPEC do documento {Id}.", doc.Id);
            await MarcarErroInternoAsync(doc, ex.Message, ct);
            return false;
        }
        catch (NotImplementedException ex)
        {
            await MarcarErroInternoAsync(doc, ex.Message, ct);
            return false;
        }
        catch (Exception ex)
        {
            doc.Status = StatusDocumento.CONTINGENCIA;
            doc.ProximaTentativaEm = DateTimeOffset.UtcNow + ProximoBackoff(doc.Tentativas + 1);
            doc.MotivoStatus = $"Erro de transmissão do EPEC: {ex.GetType().Name}: {ex.Message}";
            doc.AtualizadoEm = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return false;
        }
    }

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

    /// <summary>Terminal por regra de negócio (ex.: janela SVC) — webhook de falha.</summary>
    private async Task MarcarErroTerminalAsync(DocumentoFiscal doc, string motivo, CancellationToken ct)
    {
        doc.Status = StatusDocumento.FALHA_EMISSAO;
        doc.MotivoStatus = motivo;
        doc.Tentativas += 1;
        doc.AtualizadoEm = DateTimeOffset.UtcNow;
        doc.ProximaTentativaEm = null;

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == doc.TenantId, ct);
        if (tenant is not null) EnfileirarWebhook(doc, tenant);

        await _docRepo.AtualizarAsync(doc, ct);
        await _db.SaveChangesAsync(ct);
    }
}
