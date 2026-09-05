using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;
using NFeAutorizacao = Unimake.Business.DFe.Servicos.NFe.Autorizacao;
using NFeRetAutorizacao = Unimake.Business.DFe.Servicos.NFe.RetAutorizacao;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Adapter real de NF-e (modelo 55) via Unimake.DFe. Fluxo assíncrono
/// clássico: NFeAutorizacao (lote) → recibo → NFeRetAutorizacao. Se o
/// documento já tem ReciboLote, consulta o processamento em vez de reenviar
/// o lote (evita duplicar NF-e com o mesmo número).
///
/// Contingência: em falha ambígua de transmissão (timeout/rede), consulta o
/// protocolo pela chave determinística — SEFAZ autorizou? recuperamos sem
/// reenviar. Não autorizou e contingência habilitada? marca
/// documento.ModoContingencia ("SVCAN"/"SVCRS") — o retry reemite via SVC
/// (tpEmis 6/7) e a config da Unimake roteia ao webservice certo.
/// </summary>
public class EmissorNFe : IEmissorFiscal
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IConsultaProtocolo _consultaProtocolo;
    private readonly bool _contingenciaHabilitada;
    private readonly string _modoContingencia;

    public EmissorNFe(IConsultaProtocolo consultaProtocolo, IConfiguration configuration)
    {
        _consultaProtocolo = consultaProtocolo;
        _contingenciaHabilitada = configuration.GetValue("Fiscal:Contingencia:Habilitada", false);
        _modoContingencia = configuration.GetValue("Fiscal:Contingencia:Modo", "SVCAN")!;
    }

    public Task<ResultadoEmissao> EmitirAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<EmissaoRequest>(documento.PayloadEntrada, JsonOpts)
            ?? throw new ErroNaoRecuperavelException("PayloadEntrada não pôde ser deserializado como EmissaoRequest.");

        var config = ConfiguracaoUnimakeFactory.Criar(
            modelo: 55, tenant, certificado, ambiente,
            modoContingencia: documento.ModoContingencia);

        return string.IsNullOrEmpty(documento.ReciboLote)
            ? TransmitirLoteAsync(documento, tenant, request, certificado, ambiente, config, cancellationToken)
            : ConsultarReciboAsync(documento, ambiente, config);
    }

    private async Task<ResultadoEmissao> TransmitirLoteAsync(
        DocumentoFiscal doc, Tenant tenant, EmissaoRequest request,
        X509Certificate2 certificado, Ambiente ambiente,
        Configuracao config, CancellationToken ct)
    {
        var envi = MapperEnviNFe.Criar(doc, tenant, request, ambiente);
        var xmlGerado = envi.GerarXML().OuterXml;
        // Chave determinística (dhEmi = CriadoEm, cNF = id): consulta de
        // protocolo e retransmissão em SVC usam a MESMA chave.
        var chaveOriginal = envi.NFe[0].InfNFeField.Chave;

        try
        {
            using var servico = new NFeAutorizacao(envi, config);
            RetornoUnimake.Executar(servico);

            var ret = servico.Result; // RetEnviNFe
            var xmlAssinado = servico.ConteudoXMLAssinado?.OuterXml;
            var xmlRetorno = servico.RetornoWSString;

            // Lote recebido — guarda o recibo; o retry (VarrerContingenciaJob) consulta
            // NFeRetAutorizacao sem reenviar o lote.
            if (ret.CStat == 103 && ret.InfRec is not null)
            {
                return new ResultadoEmissao(
                    ResultadoEmissaoStatus.ErroTransmissao,
                    ChaveAcesso: null, ProtocoloAutorizacao: null,
                    XmlAssinado: xmlAssinado, XmlRetornoSefaz: xmlRetorno,
                    Motivo: $"Lote recebido pela SEFAZ (recibo {ret.InfRec.NRec}); aguardando processamento.",
                    XmlGerado: xmlGerado, ReciboLote: ret.InfRec.NRec);
            }

            if (ret.ProtNFe?.InfProt is not null)
                return RetornoUnimake.InterpretarProt(ret.ProtNFe.InfProt, xmlAssinado, xmlRetorno, xmlGerado);

            if (ret.CStat >= 200)
            {
                // Lote rejeitado (erro de estrutura/dados antes do processamento).
                return new ResultadoEmissao(
                    ResultadoEmissaoStatus.Rejeitada,
                    ChaveAcesso: null, ProtocoloAutorizacao: null,
                    XmlAssinado: xmlAssinado, XmlRetornoSefaz: xmlRetorno,
                    Motivo: $"Rejeição {ret.CStat}: {ret.XMotivo}",
                    XmlGerado: xmlGerado);
            }

            return new ResultadoEmissao(
                ResultadoEmissaoStatus.ErroTransmissao,
                ChaveAcesso: null, ProtocoloAutorizacao: null,
                XmlAssinado: xmlAssinado, XmlRetornoSefaz: xmlRetorno,
                Motivo: $"Retorno inesperado da SEFAZ (cStat {ret.CStat}): {ret.XMotivo}",
                XmlGerado: xmlGerado);
        }
        catch (ErroNaoRecuperavelException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await FalhaTransmissaoAsync(doc, tenant, certificado, chaveOriginal, ambiente, xmlGerado, ex, ct);
        }
    }

    /// <summary>
    /// Falha ambígua de rede/SEFAZ: consulta o protocolo da chave original —
    /// se a SEFAZ processou a tempo, recuperamos; senão, sinaliza SVC.
    /// </summary>
    private async Task<ResultadoEmissao> FalhaTransmissaoAsync(
        DocumentoFiscal doc, Tenant tenant, X509Certificate2 certificado, string chaveOriginal,
        Ambiente ambiente, string xmlGerado, Exception ex, CancellationToken ct)
    {
        // Recuperação por protocolo só faz sentido sem contingência ativa (na
        // SVC a chave muda de tpEmis).
        if (doc.ModoContingencia is null)
        {
            try
            {
                var consultado = await _consultaProtocolo.ConsultarAsync(
                    tenant, 55, chaveOriginal, ambiente, certificado, ct);
                if (consultado is not null)
                {
                    return new ResultadoEmissao(
                        ResultadoEmissaoStatus.Autorizada,
                        consultado.ChNFe, consultado.NProt,
                        XmlAssinado: null, XmlRetornoSefaz: null,
                        Motivo: null,
                        XmlGerado: xmlGerado,
                        ReciboLote: null);
                }
            }
            catch (Exception consultEx)
            {
                // Consulta falhou também — segue o caminho da contingência.
                ex = new Exception($"{ex.Message} (consulta de protocolo: {consultEx.Message})", ex);
            }

            if (_contingenciaHabilitada && doc.ModoContingencia is null)
                doc.ModoContingencia = _modoContingencia;
        }

        return new ResultadoEmissao(
            ResultadoEmissaoStatus.ErroTransmissao,
            ChaveAcesso: null, ProtocoloAutorizacao: null,
            XmlAssinado: null, XmlRetornoSefaz: null,
            Motivo: $"Erro de transmissão: {ex.GetType().Name}: {ex.Message}",
            XmlGerado: xmlGerado);
    }

    private static async Task<ResultadoEmissao> ConsultarReciboAsync(
        DocumentoFiscal doc, Ambiente ambiente, Configuracao config)
    {
        var cons = new ConsReciNFe
        {
            Versao = "4.00",
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            NRec = doc.ReciboLote!,
        };

        using var servico = new NFeRetAutorizacao(cons, config);
        RetornoUnimake.Executar(servico);

        var ret = servico.Result; // RetConsReciNFe
        var xmlRetorno = servico.RetornoWSString;

        // 105 = lote em processamento; 106 = recibo ainda não visível (consistência
        // eventual). Ambos viram erro de transmissão: o retry reconsulta o recibo.
        if (ret.CStat is 105 or 106)
        {
            return new ResultadoEmissao(
                ResultadoEmissaoStatus.ErroTransmissao,
                ChaveAcesso: null, ProtocoloAutorizacao: null,
                XmlAssinado: null, XmlRetornoSefaz: xmlRetorno,
                Motivo: $"cStat {ret.CStat}: {ret.XMotivo} — nova consulta do recibo {doc.ReciboLote} em seguida.");
        }

        if (ret.CStat >= 200)
        {
            return new ResultadoEmissao(
                ResultadoEmissaoStatus.Rejeitada,
                ChaveAcesso: null, ProtocoloAutorizacao: null,
                XmlAssinado: null, XmlRetornoSefaz: xmlRetorno,
                Motivo: $"Rejeição {ret.CStat} na consulta do lote: {ret.XMotivo}");
        }

        var prot = ret.ProtNFe?.FirstOrDefault(p => p.InfProt is not null);
        if (prot is null)
        {
            return new ResultadoEmissao(
                ResultadoEmissaoStatus.ErroTransmissao,
                ChaveAcesso: null, ProtocoloAutorizacao: null,
                XmlAssinado: null, XmlRetornoSefaz: xmlRetorno,
                Motivo: $"Lote processado (cStat {ret.CStat}) sem protNFe: {ret.XMotivo}");
        }

        return RetornoUnimake.InterpretarProt(prot.InfProt, xmlAssinado: null, xmlRetorno, xmlGerado: null);
    }
}
