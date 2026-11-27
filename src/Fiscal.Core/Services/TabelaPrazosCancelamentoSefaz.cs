namespace Fiscal.Core.Services;

/// <summary>
/// Tabela base dos prazos de cancelamento da SEFAZ por UF e modelo fiscal
/// (55 = NF-e, 65 = NFC-e), em minutos.
///
/// Regra geral nacional (Ajuste SINIEF):
/// - NF-e (55): 24 horas (1440 min), com exceções estaduais (ex.: MT 2h, RS 168h).
/// - NFC-e (65): 30 minutos, com exceções estaduais (ex.: MT 2h, MS/PR 24h).
///
/// Qualquer valor pode ser sobrescrito na tabela <c>prazos_cancelamento</c>
/// pelo painel admin sem precisar de novo deploy.
/// </summary>
public static class TabelaPrazosCancelamentoSefaz
{
    public const int PrazoPadraoNfeMinutos = 24 * 60; // 24 horas
    public const int PrazoPadraoNfceMinutos = 30;     // 30 minutos

    public static readonly string[] Ufs =
    [
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO",
        "MA", "MG", "MS", "MT", "PA", "PB", "PE", "PI", "PR",
        "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
    ];

    private static readonly Dictionary<(string Uf, short Modelo), (int Minutos, string Observacao)> Excecoes = new()
    {
        [("MT", 55)] = (120, "SEFAZ-MT: prazo padrão de 2 horas para NF-e."),
        [("RS", 55)] = (168 * 60, "SEFAZ-RS: aceita cancelamento de NF-e em até 168 horas (7 dias)."),
        [("MT", 65)] = (120, "SEFAZ-MT: prazo de 2 horas para NFC-e."),
        [("MS", 65)] = (24 * 60, "SEFAZ-MS: prazo de 24 horas para NFC-e."),
        [("PR", 65)] = (24 * 60, "SEFAZ-PR: prazo de 24 horas para NFC-e."),
    };

    public static bool UfValida(string uf) =>
        !string.IsNullOrWhiteSpace(uf) && Array.IndexOf(Ufs, uf.Trim().ToUpperInvariant()) >= 0;

    public static (int PrazoMinutos, string Observacao) PadraoPara(string uf, short modelo)
    {
        var ufNorm = (uf ?? string.Empty).Trim().ToUpperInvariant();
        if (Excecoes.TryGetValue((ufNorm, modelo), out var exc))
            return exc;

        return modelo == 65
            ? (PrazoPadraoNfceMinutos, "Regra geral SEFAZ (30 minutos).")
            : (PrazoPadraoNfeMinutos, "Regra geral SEFAZ (24 horas).");
    }

    public static string FormatarPrazo(int minutos)
    {
        if (minutos < 60)
            return minutos == 1 ? "1 minuto" : $"{minutos} minutos";

        if (minutos % 1440 == 0 && minutos > 1440)
        {
            var dias = minutos / 1440;
            return $"{minutos / 60} horas ({dias} dias)";
        }

        if (minutos % 60 == 0)
        {
            var horas = minutos / 60;
            return horas == 1 ? "1 hora" : $"{horas} horas";
        }

        var h = minutos / 60;
        var m = minutos % 60;
        return $"{h}h{m:D2}min";
    }
}
