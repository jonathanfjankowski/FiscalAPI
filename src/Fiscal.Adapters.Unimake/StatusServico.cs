using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;
using NFeStatusServico = Unimake.Business.DFe.Servicos.NFe.StatusServico;
using NFCeStatusServico = Unimake.Business.DFe.Servicos.NFCe.StatusServico;

namespace Fiscal.Adapters.Unimake;

/// <summary>Consulta real do StatusServico via Unimake (NFe/NFCe por modelo).</summary>
public class ConsultaStatusServicoUnimake : IConsultaStatusServico
{
    public Task<StatusServico> ConsultarAsync(
        Tenant tenant, short modelo, X509Certificate2? certificado, Ambiente ambiente,
        CancellationToken cancellationToken)
    {
        if (certificado is null)
            throw new ErroNaoRecuperavelException("Consulta de status de serviço exige certificado A1 ativo.");
        if (modelo is not (55 or 65))
            throw new ErroNaoRecuperavelException($"Modelo inválido para status de serviço: {modelo}.");
        if (!Enum.TryParse<UFBrasil>(tenant.Uf, ignoreCase: true, out var cUf) || cUf == UFBrasil.NaoDefinido)
            throw new ErroNaoRecuperavelException($"UF do tenant inválida: '{tenant.Uf}'.");

        var config = ConfiguracaoUnimakeFactory.Criar(modelo, tenant, certificado, ambiente, exigirCsc: false);

        var cons = new ConsStatServ
        {
            Versao = "4.00",
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            CUF = cUf,
            XServ = "STATUS",
        };

        using ServicoBase servico = modelo == 65
            ? new NFCeStatusServico(cons, config)
            : new NFeStatusServico(cons, config);
        RetornoUnimake.Executar(servico);

        var ret = modelo == 65
            ? ((NFCeStatusServico)servico).Result
            : ((NFeStatusServico)servico).Result;

        return Task.FromResult(new StatusServico(
            ret.CStat, ret.XMotivo, ret.TMed > 0 ? ret.TMed : null, DateTimeOffset.UtcNow));
    }
}

/// <summary>Sandbox/testes: serviço sempre "livre" (cStat 107), sem SEFAZ.</summary>
public class ConsultaStatusServicoMock : IConsultaStatusServico
{
    public Task<StatusServico> ConsultarAsync(
        Tenant tenant, short modelo, X509Certificate2? certificado, Ambiente ambiente,
        CancellationToken cancellationToken) =>
        Task.FromResult(new StatusServico(107, "Serviço em Operação (MOCK)", 1, DateTimeOffset.UtcNow));
}
