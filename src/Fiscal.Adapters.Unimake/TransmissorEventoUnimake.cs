using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;
using NFeInutilizacao = Unimake.Business.DFe.Servicos.NFe.Inutilizacao;
using NFeRecepcaoEvento = Unimake.Business.DFe.Servicos.NFe.RecepcaoEvento;
using NFCeInutilizacao = Unimake.Business.DFe.Servicos.NFCe.Inutilizacao;
using NFCeRecepcaoEvento = Unimake.Business.DFe.Servicos.NFCe.RecepcaoEvento;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Transmite eventos fiscais à SEFAZ via Unimake.DFe: cancelamento (110111),
/// carta de correção (110110, só NF-e) e inutilização de faixa. Assinatura e
/// URL do WS por UF/ambiente são da Unimake (mesma Configuracao da emissão —
/// eventos de NFC-e não exigem CSC).
/// </summary>
public class TransmissorEventoUnimake : ITransmissorEventoFiscal
{
    // Texto padrão exigido pela SEFAZ na tag xCondUso da CC-e.
    private const string XCondUsoPadrao =
        "A Carta de Correcao e disciplinada pelo paragrafo 1o-A do art. 7o do " +
        "Convenio S/N, de 15 de dezembro de 1970 e pode ser utilizada para " +
        "regularizacao de erro ocorrido na emissao de documento fiscal, desde que " +
        "o erro nao esteja relacionado com: I - as variaveis que determinam o " +
        "valor do imposto tais como: base de calculo, aliquota, diferenca de " +
        "preco, quantidade, valor da operacao ou da prestacao; II - a correcao de " +
        "dados cadastrais que implique mudanca do remetente ou do destinatario; " +
        "III - a data de emissao ou de saida.";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Task<ResultadoEvento> TransmitirAsync(
        EventoFiscal evento,
        DocumentoFiscal? documento,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken)
    {
        return evento.TipoEvento switch
        {
            "CANCELAMENTO" => TransmitirEventoAsync(evento, documento, tenant, certificado, ambiente,
                TipoEventoNFe.Cancelamento),
            "CCE" => TransmitirEventoAsync(evento, documento, tenant, certificado, ambiente,
                TipoEventoNFe.CartaCorrecao),
            "INUTILIZACAO" => TransmitirInutilizacaoAsync(evento, tenant, certificado, ambiente),
            _ => throw new ErroNaoRecuperavelException($"Tipo de evento não suportado: '{evento.TipoEvento}'."),
        };
    }

    private static Task<ResultadoEvento> TransmitirEventoAsync(
        EventoFiscal evento, DocumentoFiscal? documento, Tenant tenant,
        X509Certificate2 certificado, Ambiente ambiente, TipoEventoNFe tpEvento)
    {
        if (documento is null)
            throw new ErroNaoRecuperavelException(
                $"Evento '{evento.TipoEvento}' exige um documento fiscal associado.");
        if (string.IsNullOrEmpty(documento.ChaveAcesso))
            throw new ErroNaoRecuperavelException(
                "Documento sem chave de acesso — só é possível transmitir evento de documento autorizado.");
        if (documento.Modelo is not (55 or 65))
            throw new ErroNaoRecuperavelException($"Modelo de documento inválido para evento: {documento.Modelo}.");
        if (string.IsNullOrWhiteSpace(evento.Justificativa))
            throw new ErroNaoRecuperavelException(
                $"Evento '{evento.TipoEvento}' exige justificativa/correção (15–1000 caracteres).");

        var cancelamento = tpEvento == TipoEventoNFe.Cancelamento;
        if (cancelamento && string.IsNullOrEmpty(documento.ProtocoloAutorizacao))
            throw new ErroNaoRecuperavelException(
                "Cancelamento exige o protocolo de autorização do documento (protocoloAutorizacao vazio).");

        if (!Enum.TryParse<UFBrasil>(tenant.Uf, ignoreCase: true, out var cOrgao) || cOrgao == UFBrasil.NaoDefinido)
            throw new ErroNaoRecuperavelException($"UF do tenant inválida para evento: '{tenant.Uf}'.");

        EventoDetalhe detEvento = cancelamento
            ? new DetEventoCanc
            {
                DescEvento = "Cancelamento",
                Versao = "1.00",
                NProt = documento.ProtocoloAutorizacao!,
                XJust = evento.Justificativa!,
            }
            : new DetEventoCCE
            {
                DescEvento = "Carta de Correção",
                Versao = "1.00",
                XCorrecao = evento.Justificativa!,
                XCondUso = XCondUsoPadrao,
            };

        var inf = new InfEvento
        {
            Id = $"ID{(int)tpEvento}{documento.ChaveAcesso}01",
            COrgao = cOrgao,
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            CNPJ = SomenteDigitos(tenant.Cnpj),
            ChNFe = documento.ChaveAcesso,
            DhEvento = DateTimeOffset.Now,
            TpEvento = tpEvento,
            NSeqEvento = 1,
            VerEvento = "1.00",
            DetEvento = detEvento,
        };

        var env = new EnvEvento
        {
            Versao = "1.00",
            IdLote = Math.Abs(evento.Id.GetHashCode()).ToString(),
            Evento = new List<Evento>
            {
                new() { Versao = "1.00", InfEvento = inf },
            },
        };

        var config = ConfiguracaoUnimakeFactory.Criar(
            (short)documento.Modelo!.Value, tenant, certificado, ambiente, exigirCsc: false);

        (RetEnvEvento? ret, string xmlRetorno) = documento.Modelo == 65
            ? ExecutarNFCe(env, config)
            : ExecutarNFe(env, config);

        var evRet = ret?.RetEvento?.FirstOrDefault(e => e.InfEvento is not null);
        if (evRet is null)
        {
            // Falha no nível do lote (ex.: schema/rejeição antes de processar o evento).
            if ((ret?.CStat ?? 0) >= 200)
                return Task.FromResult(new ResultadoEvento(
                    ResultadoEventoStatus.Rejeitado, null, xmlRetorno,
                    $"cStat {ret!.CStat}: {ret.XMotivo}"));

            return Task.FromResult(new ResultadoEvento(
                ResultadoEventoStatus.ErroTransmissao, null, xmlRetorno,
                $"Retorno inesperado da SEFAZ (cStat {ret?.CStat}): {ret?.XMotivo}"));
        }

        return Task.FromResult(InterpreteEventoUnimake.InterpretarEvento(
            evRet.InfEvento.CStat, evRet.InfEvento.NProt, evRet.InfEvento.XMotivo, xmlRetorno));
    }

    private static Task<ResultadoEvento> TransmitirInutilizacaoAsync(
        EventoFiscal evento, Tenant tenant, X509Certificate2 certificado, Ambiente ambiente)
    {
        var dados = string.IsNullOrWhiteSpace(evento.DadosEvento)
            ? null
            : JsonSerializer.Deserialize<InutilizacaoDados>(evento.DadosEvento, JsonOpts);
        if (dados is null)
            throw new ErroNaoRecuperavelException(
                "Evento de inutilização sem DadosEvento (modelo/serie/faixa) — registre via POST /v1/inutilizacoes.");
        if (string.IsNullOrWhiteSpace(evento.Justificativa))
            throw new ErroNaoRecuperavelException("Inutilização exige justificativa (15–1000 caracteres).");

        if (!Enum.TryParse<UFBrasil>(tenant.Uf, ignoreCase: true, out var cUf) || cUf == UFBrasil.NaoDefinido)
            throw new ErroNaoRecuperavelException($"UF do tenant inválida para inutilização: '{tenant.Uf}'.");

        var inut = new InutNFe
        {
            Versao = "1.00",
            InfInut = new InutNFeInfInut
            {
                TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
                CUF = cUf,
                Ano = DateTimeOffset.Now.ToString("yy"),
                CNPJ = SomenteDigitos(tenant.Cnpj),
                Mod = dados.Modelo == 65 ? ModeloDFe.NFCe : ModeloDFe.NFe,
                Serie = dados.Serie,
                NNFIni = checked((int)dados.NumeroInicial),
                NNFFin = checked((int)dados.NumeroFinal),
                XJust = evento.Justificativa!,
            },
        };

        var config = ConfiguracaoUnimakeFactory.Criar(dados.Modelo, tenant, certificado, ambiente, exigirCsc: false);

        (RetInutNFe? ret, string xmlRetorno) = dados.Modelo == 65
            ? ExecutarInutilizacaoNFCe(inut, config)
            : ExecutarInutilizacaoNFe(inut, config);

        var infRet = ret?.InfInut;
        if (infRet is null)
        {
            return Task.FromResult(new ResultadoEvento(
                ResultadoEventoStatus.ErroTransmissao, null, xmlRetorno,
                "Retorno da SEFAZ sem infInut."));
        }

        return Task.FromResult(InterpreteEventoUnimake.InterpretarInutilizacao(
            infRet.CStat, infRet.NProt, infRet.XMotivo, xmlRetorno));
    }

    private static (RetEnvEvento?, string) ExecutarNFe(EnvEvento env, Configuracao config)
    {
        using var servico = new NFeRecepcaoEvento(env, config);
        RetornoUnimake.Executar(servico);
        return (servico.Result, servico.RetornoWSString);
    }

    private static (RetEnvEvento?, string) ExecutarNFCe(EnvEvento env, Configuracao config)
    {
        using var servico = new NFCeRecepcaoEvento(env, config);
        RetornoUnimake.Executar(servico);
        return (servico.Result, servico.RetornoWSString);
    }

    private static (RetInutNFe?, string) ExecutarInutilizacaoNFe(InutNFe inut, Configuracao config)
    {
        using var servico = new NFeInutilizacao(inut, config);
        RetornoUnimake.Executar(servico);
        return (servico.Result, servico.RetornoWSString);
    }

    private static (RetInutNFe?, string) ExecutarInutilizacaoNFCe(InutNFe inut, Configuracao config)
    {
        using var servico = new NFCeInutilizacao(inut, config);
        RetornoUnimake.Executar(servico);
        return (servico.Result, servico.RetornoWSString);
    }

    private static string SomenteDigitos(string cnpj) => new(cnpj.Where(char.IsDigit).ToArray());
}
