namespace Fiscal.Core.Contracts;

/// <summary>
/// Parâmetros do pedido de inutilização de faixa de numeração. Gravados em
/// EventoFiscal.DadosEvento (jsonb) pelo EventosController e lidos pelo
/// transmissor — a inutilização não amarra a um DocumentoFiscal.
/// </summary>
public record InutilizacaoDados(
    short Modelo,
    short Serie,
    long NumeroInicial,
    long NumeroFinal,
    string Ambiente);
