using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Unimake.Business.DFe.Servicos;
using NACIONAL = Unimake.Business.DFe.Xml.NFSe.NACIONAL;
using GerarNfseNacional = Unimake.Business.DFe.Servicos.NFSe.GerarNfse;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Adapter real de NFS-e (padrão Nacional — DPS, layout 1.01) via Unimake.DFe.
/// A autorização é SÍNCRONA: DPS assinado → webservice "GerarNfse" (REST,
/// dpsXmlGZipB64) → NFSe autorizada ou lista de erros (Temp.Erros).
/// Sem recibo/lote — retry reenvia o mesmo DPS (Id determinístico: série +
/// número reservados são únicos por tenant).
/// Emissões pela rota legada (EmissaoRequest) só funcionam em sandbox — aqui
/// elas falham alto para não gravar documento com chave/protocolo fake.
/// </summary>
public class EmissorNFSe : IEmissorFiscal
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Task<ResultadoEmissao> EmitirAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken)
    {
        NfseDpsRequest request;
        MapperDps.InfoSubstituicao? substituicao;
        try
        {
            request = MapperDps.LerRequest(documento.PayloadEntrada);
            substituicao = MapperDps.LerSubstituicao(documento.PayloadEntrada);
        }
        catch (JsonException ex)
        {
            throw new ErroNaoRecuperavelException("PayloadEntrada não pôde ser deserializado como NfseDpsRequest: " + ex.Message, ex);
        }

        if (certificado is null)
            throw new ErroNaoRecuperavelException(
                "Emissão real de NFS-e exige certificado digital A1 ativo para o tenant.");

        var dps = MapperDps.Criar(documento, tenant, request, ambiente, substituicao);
        var xmlGerado = dps.GerarXML().OuterXml;

        var config = new Configuracao
        {
            TipoDFe = TipoDFe.NFSe,
            PadraoNFSe = PadraoNFSe.NACIONAL,
            Servico = Servico.NFSeGerarNfse,
            TipoAmbiente = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            SchemaVersao = "1.01",
            CertificadoDigital = certificado,
        };

        try
        {
            using var servico = new GerarNfseNacional(dps.GerarXML(), config);
            RetornoUnimake.Executar(servico);

            var xmlAssinado = servico.ConteudoXMLAssinado?.OuterXml;
            var xmlRetorno = servico.RetornoWSString;

            // Autorizada síncrona: retorno com o objeto NFSe (chave de 50, "NFS...").
            if (servico.Result?.InfNFSe is { } inf)
            {
                return Task.FromResult(new ResultadoEmissao(
                    ResultadoEmissaoStatus.Autorizada,
                    ChaveAcesso: inf.Id,
                    ProtocoloAutorizacao: null,
                    XmlAssinado: xmlAssinado, XmlRetornoSefaz: xmlRetorno,
                    Motivo: null,
                    XmlGerado: xmlGerado));
            }

            // Rejeição: retorno Temp com a lista de erros (código + descrição).
            var temp = servico.ResultErro;
            if (temp is not null)
            {
                var motivos = new List<string>();
                if (temp.Erro is not null) motivos.Add($"{temp.Erro.Codigo}: {temp.Erro.Descricao}".Trim(' ', ':'));
                if (temp.Erros is not null) motivos.Add($"{temp.Erros.Codigo}: {temp.Erros.Descricao}".Trim(' ', ':'));
                motivos.RemoveAll(string.IsNullOrWhiteSpace);

                return Task.FromResult(new ResultadoEmissao(
                    ResultadoEmissaoStatus.Rejeitada,
                    ChaveAcesso: temp.ChaveAcesso,
                    ProtocoloAutorizacao: null,
                    XmlAssinado: xmlAssinado, XmlRetornoSefaz: xmlRetorno,
                    Motivo: motivos.Count > 0
                        ? "Rejeição SEFAZ Nacional: " + string.Join(" | ", motivos)
                        : "Rejeição SEFAZ Nacional sem detalhes.",
                    XmlGerado: xmlGerado));
            }

            return Task.FromResult(new ResultadoEmissao(
                ResultadoEmissaoStatus.ErroTransmissao,
                ChaveAcesso: null, ProtocoloAutorizacao: null,
                XmlAssinado: null, XmlRetornoSefaz: xmlRetorno,
                Motivo: "Retorno da SEFAZ Nacional sem NFSe nem lista de erros.",
                XmlGerado: xmlGerado));
        }
        catch (ErroNaoRecuperavelException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Erro de transmissão (rede/SEFAZ fora): o retry reenvia o mesmo DPS.
            return Task.FromResult(new ResultadoEmissao(
                ResultadoEmissaoStatus.ErroTransmissao,
                ChaveAcesso: null, ProtocoloAutorizacao: null,
                XmlAssinado: null, XmlRetornoSefaz: null,
                Motivo: $"Erro de transmissão: {ex.GetType().Name}: {ex.Message}",
                XmlGerado: xmlGerado));
        }
    }
}
