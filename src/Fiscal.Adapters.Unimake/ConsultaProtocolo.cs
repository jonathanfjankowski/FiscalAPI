using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;
using NFeConsultaProtocolo = Unimake.Business.DFe.Servicos.NFe.ConsultaProtocolo;
using NFCeConsultaProtocolo = Unimake.Business.DFe.Servicos.NFCe.ConsultaProtocolo;

namespace Fiscal.Adapters.Unimake;

/// <summary>Consulta de protocolo real (ConsSitNFe) por modelo.</summary>
public class ConsultaProtocoloUnimake : IConsultaProtocolo
{
    public Task<ProtocoloConsultado?> ConsultarAsync(
        Tenant tenant, short modelo, string chaveAcesso, Ambiente ambiente,
        X509Certificate2? certificado, CancellationToken cancellationToken)
    {
        if (modelo is not (55 or 65))
            throw new ErroNaoRecuperavelException($"Modelo inválido para consulta de protocolo: {modelo}.");
        if (!Enum.TryParse<UFBrasil>(tenant.Uf, ignoreCase: true, out var cUf) || cUf == UFBrasil.NaoDefinido)
            throw new ErroNaoRecuperavelException($"UF do tenant inválida: '{tenant.Uf}'.");

        var config = ConfiguracaoUnimakeFactory.Criar(modelo, tenant, certificado!, ambiente, exigirCsc: false);

        var cons = new ConsSitNFe
        {
            Versao = "4.00",
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            ChNFe = chaveAcesso,
            XServ = "CONSIT",
        };

        (int cStat, string motivo, string? nProt) = modelo == 65
            ? ConsultarNFCe(cons, config)
            : ConsultarNFe(cons, config);

        // 217 = "NF-e não consta na base" — não autorizada, não recuperada.
        if (cStat is 100 or 150)
            return Task.FromResult<ProtocoloConsultado?>(new ProtocoloConsultado(cStat, motivo, nProt, chaveAcesso));
        return Task.FromResult<ProtocoloConsultado?>(null);
    }

    private static (int, string, string?) ConsultarNFe(ConsSitNFe cons, Configuracao config)
    {
        using var servico = new NFeConsultaProtocolo(cons, config);
        RetornoUnimake.Executar(servico);
        var ret = servico.Result;
        return (ret.CStat, ret.XMotivo, ret.ProtNFe?.InfProt?.NProt);
    }

    private static (int, string, string?) ConsultarNFCe(ConsSitNFe cons, Configuracao config)
    {
        using var servico = new NFCeConsultaProtocolo(cons, config);
        RetornoUnimake.Executar(servico);
        var ret = servico.Result;
        return (ret.CStat, ret.XMotivo, ret.ProtNFe?.InfProt?.NProt);
    }
}

/// <summary>Sandbox/testes: chave nunca consta na base (não recuperada).</summary>
public class ConsultaProtocoloMock : IConsultaProtocolo
{
    public Task<ProtocoloConsultado?> ConsultarAsync(
        Tenant tenant, short modelo, string chaveAcesso, Ambiente ambiente,
        X509Certificate2? certificado, CancellationToken cancellationToken) =>
        Task.FromResult<ProtocoloConsultado?>(null);
}
