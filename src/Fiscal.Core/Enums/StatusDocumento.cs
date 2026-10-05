namespace Fiscal.Core.Enums;

public enum StatusDocumento
{
    PENDENTE = 0,
    PROCESSANDO = 1,
    AUTORIZADA = 2,
    REJEITADA = 3,
    CONTINGENCIA = 4,
    CANCELAMENTO_PENDENTE = 5,
    CANCELADA = 6,
    ERRO_CANCELAMENTO = 7,
    DENEGADA = 8,
    ERRO_INTERNO = 9,

    /// <summary>Terminal: esgotou MaxTentativas em CONTINGENCIA (SEFAZ inacessível).
    /// Não reprocessa sozinho — exige reemissão ou replay manual.</summary>
    FALHA_EMISSAO = 10
}
