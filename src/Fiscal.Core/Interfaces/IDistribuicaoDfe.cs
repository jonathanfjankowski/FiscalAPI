using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;

namespace Fiscal.Core.Interfaces;

public record NotaDistDfe(string Nsu, string Schema, string Xml);

public record ResultadoDistribuicao(
    int CStat,
    string? XMotivo,
    string UltNsu,
    string MaxNsu,
    IReadOnlyList<NotaDistDfe> Documentos);

/// <summary>
/// Consulta o Distribuição DFe (NSU) para um tenant — retorna resumos/procs
/// de NFe destinadas ao CNPJ do tenant. Implementação real: Unimake; sandbox
/// e testes usam mock/fake.
/// </summary>
public interface IConsultaDistribuicaoDfe
{
    Task<ResultadoDistribuicao> ConsultarAsync(
        Tenant tenant,
        X509Certificate2? certificado,
        Ambiente ambiente,
        string ultimoNsu,
        CancellationToken cancellationToken);
}

/// <summary>
/// Transmite uma Manifestação do Destinatário (210200/210210/210220/210240)
/// para uma nota recebida.
/// </summary>
public interface ITransmissorManifestacao
{
    Task<ResultadoEvento> TransmitirAsync(
        ManifestacaoDestinatario manifestacao,
        NotaRecebida nota,
        Tenant tenant,
        X509Certificate2? certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken);
}
