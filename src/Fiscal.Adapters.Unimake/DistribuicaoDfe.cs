using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;
using NFeDistribuicao = Unimake.Business.DFe.Servicos.NFe.DistribuicaoDFe;
using NFeRecepcaoEvento = Unimake.Business.DFe.Servicos.NFe.RecepcaoEvento;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Utilitários de parsing de XML de distribuição (resNFe/procNFe).
/// Público para testes.
/// </summary>
public static class ExtratorDfe
{
    /// <summary>Chave de 44 dígitos a partir de resNFe (attr ChNFe) ou procNFe (Id="NFe{chave}").</summary>
    public static string? ChaveDoXml(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return null; }

        var infNFe = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "infNFe");
        if (infNFe is null) return null;

        var ch = (string?)infNFe.Attribute("ChNFe") ?? (string?)infNFe.Attribute("chNFe");
        if (string.IsNullOrEmpty(ch))
        {
            var id = (string?)infNFe.Attribute("Id");
            if (id is not null && id.StartsWith("NFe")) ch = id[3..];
        }

        return ch is { Length: 44 } ? ch : null;
    }

    public static string? ValorDoElemento(string xml, string nomeLocal)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return null; }

        // resNFe traz vNF/dhEmi como atributos de infNFe; procNFe traz como elementos.
        var elemento = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == nomeLocal)?
            .Value.Trim();
        if (!string.IsNullOrEmpty(elemento)) return elemento;

        return doc.Descendants()
            .Select(e => (string?)e.Attribute(nomeLocal))
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
    }
}

/// <summary>
/// Consulta o Distribuição DFe (SEFAZ RFB) via Unimake — páginas de NSU com
/// NFe destinadas ao CNPJ do tenant. DocZip já vem descomprimido pelo pacote
/// (ConteudoXML). Eventos (resEvento/cancelamento de terceiros) são ignorados.
/// </summary>
public class ConsultaDistribuicaoUnimake : IConsultaDistribuicaoDfe
{
    public Task<ResultadoDistribuicao> ConsultarAsync(
        Tenant tenant,
        X509Certificate2? certificado,
        Ambiente ambiente,
        string ultimoNsu,
        CancellationToken cancellationToken)
    {
        if (certificado is null)
            throw new ErroNaoRecuperavelException("Distribuição DFe exige certificado A1 ativo.");

        if (!Enum.TryParse<UFBrasil>(tenant.Uf, ignoreCase: true, out var cUf) || cUf == UFBrasil.NaoDefinido)
            throw new ErroNaoRecuperavelException($"UF do tenant inválida para distribuição DFe: '{tenant.Uf}'.");

        var config = ConfiguracaoUnimakeFactory.Criar(modelo: 55, tenant, certificado, ambiente, exigirCsc: false);

        var dist = new DistDFeInt
        {
            Versao = "1.01",
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            CUFAutor = cUf,
            CNPJ = new string(tenant.Cnpj.Where(char.IsDigit).ToArray()),
            DistNSU = new DistNSU { UltNSU = ultimoNsu.PadLeft(15, '0') },
        };

        using var servico = new NFeDistribuicao(dist, config);
        RetornoUnimake.Executar(servico);

        var ret = servico.Result; // RetDistDFeInt
        var documentos = ret.LoteDistDFeInt?.DocZip?
            .Where(d => d.Schema is not null && d.Schema.Contains("NFe", StringComparison.OrdinalIgnoreCase))
            .Select(d => new NotaDistDfe(d.NSU ?? "", d.Schema, d.ConteudoXML ?? ""))
            .Where(d => !string.IsNullOrEmpty(d.Xml))
            .ToList() ?? [];

        return Task.FromResult(new ResultadoDistribuicao(
            ret.CStat, ret.XMotivo,
            ret.UltNSU ?? ultimoNsu.PadLeft(15, '0'),
            ret.MaxNSU ?? ultimoNsu.PadLeft(15, '0'),
            documentos));
    }
}

/// <summary>Sandbox/testes: resposta "sem documentos" (cStat 137).</summary>
public class ConsultaDistribuicaoMock : IConsultaDistribuicaoDfe
{
    public Task<ResultadoDistribuicao> ConsultarAsync(
        Tenant tenant, X509Certificate2? certificado, Ambiente ambiente, string ultimoNsu, CancellationToken cancellationToken) =>
        Task.FromResult(new ResultadoDistribuicao(
            CStat: 137, XMotivo: "Nenhum DF-e localizado (MOCK)",
            UltNsu: ultimoNsu.PadLeft(15, '0'), MaxNsu: ultimoNsu.PadLeft(15, '0'),
            Documentos: Array.Empty<NotaDistDfe>()));
}

/// <summary>
/// Transmite Manifestação do Destinatário via RecepcaoEvento (Unimake).
/// </summary>
public class TransmissorManifestacaoUnimake : ITransmissorManifestacao
{
    private static readonly (string Codigo, string DescEvento)[] Tipos =
    [
        ("210200", "Confirmacao da Operacao"),
        ("210210", "Ciencia da Operacao"),
        ("210220", "Desconhecimento da Operacao"),
        ("210240", "Operacao nao Realizada"),
    ];

    public Task<ResultadoEvento> TransmitirAsync(
        ManifestacaoDestinatario manifestacao,
        NotaRecebida nota,
        Tenant tenant,
        X509Certificate2? certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken)
    {
        var tipo = Tipos.FirstOrDefault(t => t.Codigo == manifestacao.Tipo);
        if (tipo.Codigo is null)
            throw new ErroNaoRecuperavelException($"Tipo de manifestação inválido: '{manifestacao.Tipo}'.");
        if (string.IsNullOrEmpty(nota.Chave) || nota.Chave.Length != 44)
            throw new ErroNaoRecuperavelException("Nota recebida sem chave de 44 dígitos.");

        // cOrgao = cUF do emitente da nota (2 primeiros dígitos da chave).
        var cufCodigo = int.Parse(nota.Chave[..2]);
        var cOrgao = System.Enum.IsDefined(typeof(UFBrasil), cufCodigo)
            ? (UFBrasil)cufCodigo
            : throw new ErroNaoRecuperavelException($"cUF da chave desconhecido: {cufCodigo}.");

        var inf = new InfEvento
        {
            Id = $"ID{tipo.Codigo}{nota.Chave}01",
            COrgao = cOrgao,
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            CNPJ = new string(tenant.Cnpj.Where(char.IsDigit).ToArray()),
            ChNFe = nota.Chave,
            DhEvento = DateTimeOffset.Now,
            TpEvento = (TipoEventoNFe)int.Parse(tipo.Codigo),
            NSeqEvento = 1,
            VerEvento = "1.00",
            DetEvento = new DetEventoManif
            {
                DescEvento = tipo.DescEvento,
                Versao = "1.00",
                XJust = manifestacao.Justificativa,
            },
        };

        var env = new EnvEvento
        {
            Versao = "1.00",
            IdLote = Math.Abs(manifestacao.Id.GetHashCode()).ToString(),
            Evento = new List<Evento> { new() { Versao = "1.00", InfEvento = inf } },
        };

        var config = ConfiguracaoUnimakeFactory.Criar(modelo: 55, tenant, certificado!, ambiente, exigirCsc: false);

        using var servico = new NFeRecepcaoEvento(env, config);
        RetornoUnimake.Executar(servico);

        var ret = servico.Result; // RetEnvEvento
        var evRet = ret.RetEvento?.FirstOrDefault(e => e.InfEvento is not null);
        if (evRet is null)
        {
            if (ret.CStat >= 200)
                return Task.FromResult(new ResultadoEvento(
                    ResultadoEventoStatus.Rejeitado, null, servico.RetornoWSString,
                    $"cStat {ret.CStat}: {ret.XMotivo}"));

            return Task.FromResult(new ResultadoEvento(
                ResultadoEventoStatus.ErroTransmissao, null, servico.RetornoWSString,
                $"Retorno inesperado da SEFAZ (cStat {ret.CStat}): {ret.XMotivo}"));
        }

        return Task.FromResult(InterpreteEventoUnimake.InterpretarEvento(
            evRet.InfEvento.CStat, evRet.InfEvento.NProt, evRet.InfEvento.XMotivo, servico.RetornoWSString));
    }
}

/// <summary>Sandbox/testes: manifestação sempre PROCESSADO (cStat 135).</summary>
public class TransmissorManifestacaoMock : ITransmissorManifestacao
{
    public Task<ResultadoEvento> TransmitirAsync(
        ManifestacaoDestinatario manifestacao, NotaRecebida nota, Tenant tenant,
        X509Certificate2? certificado, Ambiente ambiente, CancellationToken cancellationToken)
    {
        var protocolo = "MOCK" + Math.Abs(manifestacao.Id.GetHashCode()).ToString("D13")[..13];
        return Task.FromResult(new ResultadoEvento(
            ResultadoEventoStatus.Processado, protocolo,
            $"<retEnvEvento><cStat>135</cStat><nProt>{protocolo}</nProt></retEnvEvento>",
            Motivo: null));
    }
}
