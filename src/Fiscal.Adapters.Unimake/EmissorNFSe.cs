using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Adapter de NFS-e (padrão Nacional — DPS). O envelope REST, numeração, fila
/// e sandbox (EmissorMock) já funcionam; a integração real de transmissão DPS
/// (layout nacional + provedores municipais) é a sprint seguinte do roadmap
/// NFS-e — fora de sandbox este adapter falha alto para não gravar documento
/// com chave/protocolo fake.
/// </summary>
public class EmissorNFSe : IEmissorFiscal
{
    public Task<ResultadoEmissao> EmitirAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException(
            "EmissorNFSe (DPS padrão Nacional) ainda não integrado à Unimake.DFe. " +
            "Use Fiscal:ModoSandbox=true (EmissorMock) para testes do envelope. " +
            "A transmissão DPS real é a próxima sprint do roadmap NFS-e.");
    }
}
