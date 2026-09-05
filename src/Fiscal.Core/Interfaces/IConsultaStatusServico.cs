using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;

namespace Fiscal.Core.Interfaces;

public record StatusServico(int CStat, string XMotivo, int? TMed, DateTimeOffset ConsultadoEm);

/// <summary>Consulta o status do serviço SEFAZ (modelo 55/65) para a UF do tenant.</summary>
public interface IConsultaStatusServico
{
    Task<StatusServico> ConsultarAsync(
        Tenant tenant,
        short modelo,
        X509Certificate2? certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken);
}
