using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;

namespace Fiscal.Core.Interfaces;

public enum ResultadoEventoStatus
{
    Processado,
    Rejeitado,
    ErroTransmissao
}

public record ResultadoEvento(
    ResultadoEventoStatus Status,
    string? Protocolo,
    string? XmlRetorno,
    string? Motivo);

/// <summary>
/// Transmite um EventoFiscal à SEFAZ (cancelamento 110111, CC-e 110110,
/// inutilização). Para eventos ligados a documento, o documento informa chave
/// de acesso e protocolo; inutilização carrega os próprios dados em
/// EventoFiscal.DadosEvento (JSON de InutilizacaoDados).
/// </summary>
public interface ITransmissorEventoFiscal
{
    Task<ResultadoEvento> TransmitirAsync(
        EventoFiscal evento,
        DocumentoFiscal? documento,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken);
}

/// <summary>
/// Transmite o evento prévio EPEC (110140) à SVRS — contingência NF-e: a
/// venda é registrada na SEFAZ antes da transmissão da NF-e completa
/// (tpEmis 4, janela de 168h). Implementação real: Unimake; sandbox não usa
/// (o mock não entra em contingência).
/// </summary>
public interface ITransmissorEpec
{
    /// <summary>Evento EPEC montado a partir do payload do documento (dados de
    /// emissão/destinatário/totais — o XML completo ainda não existe).</summary>
    Task<ResultadoEvento> TransmitirAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        X509Certificate2 certificado,
        Ambiente ambiente,
        CancellationToken cancellationToken);
}
