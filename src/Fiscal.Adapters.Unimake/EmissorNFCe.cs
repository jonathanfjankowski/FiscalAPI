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
using NFCeAutorizacao = Unimake.Business.DFe.Servicos.NFCe.Autorizacao;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Adapter real de NFC-e (modelo 65) via Unimake.DFe. Autorização síncrona
/// (indSinc=1) — o protNFe vem no mesmo retorno. Exige CSC/IdCSC do tenant
/// (o QR code é montado pelo próprio adapter da Unimake a partir do CSC).
///
/// Contingência: em falha ambígua de transmissão, consulta o protocolo pela
/// chave determinística; se contingência habilitada, marca ModoContingencia
/// ("SVCAN"/"SVCRS") e o retry reemite via SVC (tpEmis 6/7).
/// </summary>
public class EmissorNFCe : IEmissorFiscal
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ICertificadoStore _cscStore;
    private readonly IConsultaProtocolo _consultaProtocolo;
    private readonly bool _contingenciaHabilitada;
    private readonly string _modoContingencia;
    private readonly MapperEnviNFe.RespTecDados? _respTec;

    public EmissorNFCe(ICertificadoStore cscStore, IConsultaProtocolo consultaProtocolo, IConfiguration configuration)
    {
        _cscStore = cscStore;
        _consultaProtocolo = consultaProtocolo;
        _contingenciaHabilitada = configuration.GetValue("Fiscal:Contingencia:Habilitada", false);
        _modoContingencia = configuration.GetValue("Fiscal:Contingencia:Modo", "SVCAN")!;
        _respTec = EmissorNFe.LerRespTec(configuration);
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

        var csc = tenant.CscCriptografado is null
            ? null
            : _cscStore.DecifrarTextoAsync(tenant.CscCriptografado, cancellationToken).GetAwaiter().GetResult();
        var config = ConfiguracaoUnimakeFactory.Criar(
            modelo: 65, tenant, certificado, ambiente,
            csc: csc, cscId: tenant.CscId,
            modoContingencia: documento.ModoContingencia);

        return EmitirSincrono(documento, tenant, request, certificado, ambiente, config, cancellationToken);
    }

    private async Task<ResultadoEmissao> EmitirSincrono(
        DocumentoFiscal doc, Tenant tenant, EmissaoRequest request,
        X509Certificate2 certificado, Ambiente ambiente,
        Configuracao config, CancellationToken ct)
    {
        var envi = MapperEnviNFe.Criar(doc, tenant, request, ambiente, _respTec);
        var xmlGerado = envi.GerarXML().OuterXml;
        var chaveOriginal = envi.NFe[0].InfNFeField.Chave;

        try
        {
            using var servico = new NFCeAutorizacao(envi, config);
            RetornoUnimake.Executar(servico);

            var ret = servico.Result; // RetEnviNFe
            var xmlRetorno = servico.RetornoWSString;

            if (ret.ProtNFe?.InfProt is not null)
            {
                // nfeProc (NFe assinada + protNFe) é o XML de distribuição do consumidor.
                var nfeProc = servico.NfeProcResult?.GerarXML().OuterXml
                    ?? servico.ConteudoXMLAssinado?.OuterXml;
                return RetornoUnimake.InterpretarProt(ret.ProtNFe.InfProt, nfeProc, xmlRetorno, xmlGerado);
            }

            if (ret.CStat >= 200)
            {
                return new ResultadoEmissao(
                    ResultadoEmissaoStatus.Rejeitada,
                    ChaveAcesso: null, ProtocoloAutorizacao: null,
                    XmlAssinado: servico.ConteudoXMLAssinado?.OuterXml, XmlRetornoSefaz: xmlRetorno,
                    Motivo: $"Rejeição {ret.CStat}: {ret.XMotivo}",
                    XmlGerado: xmlGerado);
            }

            return new ResultadoEmissao(
                ResultadoEmissaoStatus.ErroTransmissao,
                ChaveAcesso: null, ProtocoloAutorizacao: null,
                XmlAssinado: servico.ConteudoXMLAssinado?.OuterXml, XmlRetornoSefaz: xmlRetorno,
                Motivo: $"Retorno inesperado da SEFAZ (cStat {ret.CStat}): {ret.XMotivo}",
                XmlGerado: xmlGerado);
        }
        catch (ErroNaoRecuperavelException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Mesma recuperação da NF-e: protocolo pela chave determinística e,
            // se contingência habilitada, sinaliza SVC para o retry.
            if (doc.ModoContingencia is null)
            {
                try
                {
                    var consultado = await _consultaProtocolo.ConsultarAsync(tenant, 65, chaveOriginal, ambiente, certificado, ct);
                    if (consultado is not null)
                    {
                        // Autorizada sem protNFe no retorno: reconstrói o nfeProc
                        // (XML assinado salvo antes + protNFe da consulta) — sem
                        // isso o DANFCe sai sem QR e o consumidor fica sem XML.
                        var nfeProc = MontadorNfeProc.Montar(doc.XmlAssinado, consultado.XmlProtNFe);
                        return new ResultadoEmissao(
                            ResultadoEmissaoStatus.Autorizada,
                            consultado.ChNFe, consultado.NProt,
                            XmlAssinado: doc.XmlAssinado,
                            XmlRetornoSefaz: nfeProc ?? consultado.XmlProtNFe,
                            Motivo: null,
                            XmlGerado: xmlGerado);
                    }
                }
                catch (Exception consultEx)
                {
                    ex = new Exception($"{ex.Message} (consulta de protocolo: {consultEx.Message})", ex);
                }

                if (_contingenciaHabilitada)
                    doc.ModoContingencia = _modoContingencia;
            }

            return new ResultadoEmissao(
                ResultadoEmissaoStatus.ErroTransmissao,
                ChaveAcesso: null, ProtocoloAutorizacao: null,
                XmlAssinado: null, XmlRetornoSefaz: null,
                Motivo: $"Erro de transmissão: {ex.GetType().Name}: {ex.Message}",
                XmlGerado: xmlGerado);
        }
    }
}
