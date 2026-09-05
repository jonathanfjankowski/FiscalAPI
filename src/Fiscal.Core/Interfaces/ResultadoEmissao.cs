using Fiscal.Core.Enums;

namespace Fiscal.Core.Interfaces;

public enum ResultadoEmissaoStatus
{
    Autorizada,
    Rejeitada,
    Denegada,
    ErroTransmissao
}

public record ResultadoEmissao(
    ResultadoEmissaoStatus Status,
    string? ChaveAcesso,
    string? ProtocoloAutorizacao,
    string? XmlAssinado,
    string? XmlRetornoSefaz,
    string? Motivo,
    string? XmlGerado = null,
    string? ReciboLote = null);
