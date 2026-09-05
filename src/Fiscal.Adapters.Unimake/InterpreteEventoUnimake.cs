using Fiscal.Core.Interfaces;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Interpretação dos códigos de status (cStat) da SEFAZ para eventos e
/// inutilização. Público para testes unitários — a tabela é lógica pura.
/// </summary>
public static class InterpreteEventoUnimake
{
    /// <summary>cStat do infEvento de um RetEvento: 135 (evento homologado),
    /// 136 (vinculado a lote anterior) e 155 (cancelamento homologado fora do
    /// prazo, com provimento judicial) são sucesso; demais = rejeição.</summary>
    public static ResultadoEvento InterpretarEvento(int cStat, string? nProt, string? xMotivo, string? xmlRetorno)
    {
        var processado = cStat is 135 or 136 or 155;
        return new ResultadoEvento(
            processado ? ResultadoEventoStatus.Processado : ResultadoEventoStatus.Rejeitado,
            nProt,
            xmlRetorno,
            processado ? null : $"cStat {cStat}: {xMotivo}");
    }

    /// <summary>cStat do infInut do RetInutNFe: 102 = inutilização homologada;
    /// demais = rejeição.</summary>
    public static ResultadoEvento InterpretarInutilizacao(int cStat, string? nProt, string? xMotivo, string? xmlRetorno)
    {
        var processado = cStat == 102;
        return new ResultadoEvento(
            processado ? ResultadoEventoStatus.Processado : ResultadoEventoStatus.Rejeitado,
            nProt,
            xmlRetorno,
            processado ? null : $"cStat {cStat}: {xMotivo}");
    }
}
