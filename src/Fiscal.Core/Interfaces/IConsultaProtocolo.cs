using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;

namespace Fiscal.Core.Interfaces;

/// <summary>
/// ProtNFe recuperado na consulta (elemento assinado da resposta CONSIT) —
/// permite montar o nfeProc de distribuição quando a autorização é recuperada
/// sem o retorno completo do lote. Null quando indisponível.
/// </summary>
/// <param name="XmlProtNFe">XML do elemento protNFe (com assinatura da SEFAZ).</param>
public record ProtocoloConsultado(
    int CStat, string? XMotivo, string? NProt, string? ChNFe, string? XmlProtNFe = null);

/// <summary>
/// Consulta o protocolo de autorização de uma chave de acesso. Usada quando a
/// transmissão falha de forma ambígua (timeout após possível processamento):
/// se a SEFAZ autorizou, recuperamos o protocolo sem reenviar; se não,
/// habilita a troca para contingência SVC sem risco de duplicidade.
/// </summary>
public interface IConsultaProtocolo
{
    /// <summary>Null quando a chave não consta na base (cStat 217).</summary>
    Task<ProtocoloConsultado?> ConsultarAsync(
        Tenant tenant,
        short modelo,
        string chaveAcesso,
        Ambiente ambiente,
        X509Certificate2? certificado,
        CancellationToken cancellationToken);
}
