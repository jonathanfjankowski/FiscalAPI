using Fiscal.Core.Contracts;

namespace Fiscal.Core.Services;

/// <summary>
/// Validação declarativa dos grupos de imposto v2 (docs/plano-evolucao-contrato-v2.md §4).
/// A API não calcula tributos: confere obrigatoriedade por CST/CSOSN, coerência
/// isento × valor e aritmética por grupo (tolerância R$ 0,01). Falhas viram 422.
/// </summary>
public class ValidadorImpostosV2
{
    private const decimal Tolerancia = 0.01m;

    private static readonly string[] CstsSuportados = ["00", "10", "20", "40", "41", "51", "60", "70", "90"];
    private static readonly string[] CsosnsSuportados = ["101", "102", "103", "201", "202", "203", "300", "400", "500", "900"];

    public IReadOnlyList<InconsistenciaFiscal> Validar(IReadOnlyList<ItemDto> itens)
    {
        var erros = new List<InconsistenciaFiscal>();
        for (var i = 0; i < itens.Count; i++)
        {
            var icms = itens[i].ImpostosV2?.Icms;
            if (icms is null)
                continue;
            ValidarIcms(icms, i, itens[i].Codigo, erros);
        }
        return erros;
    }

    private static void ValidarIcms(IcmsDto icms, int indice, string codigo, List<InconsistenciaFiscal> erros)
    {
        var prefixo = $"itens[{indice}].impostosV2.icms";
        string P(string campo) => $"{prefixo}.{campo}";

        if (icms.Origem is < 0 or > 8)
            erros.Add(new(P("origem"), $"Origem {icms.Origem} inválida — use 0 a 8."));

        if (icms.Cst is not null && icms.Csosn is not null)
        {
            erros.Add(new(P("cst"), "Informe 'cst' OU 'csosn' — nunca os dois."));
            return;
        }
        if (icms.Cst is null && icms.Csosn is null)
        {
            erros.Add(new(P("cst"), "Informe 'cst' (regime normal) ou 'csosn' (Simples Nacional)."));
            return;
        }

        if (icms.Cst is { } cst)
        {
            if (!CstsSuportados.Contains(cst))
            {
                erros.Add(new(P("cst"),
                    $"CST '{cst}' não suportado (suporta {string.Join("/", CstsSuportados)}). " +
                    "CST 02/15/30/53/61, ICMSPart e ICMSST não fazem parte do contrato atual."));
                return;
            }
            ValidarPorCst(cst, icms, P, erros);
        }
        else if (icms.Csosn is { } csosn)
        {
            if (!CsosnsSuportados.Contains(csosn))
            {
                erros.Add(new(P("csosn"),
                    $"CSOSN '{csosn}' não suportado (suporta {string.Join("/", CsosnsSuportados)})."));
                return;
            }
            ValidarPorCsosn(csosn, icms, P, erros);
        }

        ValidarAritmetica(icms, P, erros);
        ValidarDifal(icms.Difal, P, erros);
    }

    private static void ValidarPorCst(string cst, IcmsDto icms, Func<string, string> P, List<InconsistenciaFiscal> erros)
    {
        switch (cst)
        {
            case "00":
                ExigirTributacao("00", icms, P, erros);
                break;
            case "10":
                ExigirTributacao("10", icms, P, erros);
                ExigirStPropria("10", icms.St, P, erros);
                break;
            case "20":
                ExigirTributacao("20", icms, P, erros);
                if (icms.PercentualReducaoBc is null)
                    erros.Add(new(P("percentualReducaoBc"), "CST 20 exige percentualReducaoBc."));
                break;
            case "40" or "41":
                ProibirValorProprio(cst, icms, P, erros);
                break;
            case "51":
                if (icms.ValorIcmsOperacao is null)
                    erros.Add(new(P("valorIcmsOperacao"), "CST 51 exige valorIcmsOperacao (vICMSOp)."));
                break;
            case "60":
                ProibirValorProprio("60", icms, P, erros);
                break;
            case "70":
                ExigirTributacao("70", icms, P, erros);
                if (icms.PercentualReducaoBc is null)
                    erros.Add(new(P("percentualReducaoBc"), "CST 70 exige percentualReducaoBc."));
                ExigirStPropria("70", icms.St, P, erros);
                break;
            case "90":
                // CST 90 é "outras" — combinações livres; valida-se o que vier (aritmética abaixo).
                break;
        }
    }

    private static void ValidarPorCsosn(string csosn, IcmsDto icms, Func<string, string> P, List<InconsistenciaFiscal> erros)
    {
        switch (csosn)
        {
            case "101":
                ExigirCreditoSimples("101", icms, P, erros);
                break;
            case "201":
                ExigirStPropria("201", icms.St, P, erros);
                break;
            case "202" or "203":
                ExigirStPropria(csosn, icms.St, P, erros);
                break;
            case "300" or "400":
                ProibirValorProprio(csosn, icms, P, erros);
                break;
            case "500":
                ProibirValorProprio("500", icms, P, erros);
                break;
                // 102/103/900: livres (900 valida o que vier).
        }
    }

    private static void ExigirTributacao(string cst, IcmsDto icms, Func<string, string> P, List<InconsistenciaFiscal> erros)
    {
        if (icms.BaseCalculo is null || icms.Aliquota is null || icms.Valor is null)
            erros.Add(new(P("valor"),
                $"CST {cst} exige baseCalculo, aliquota e valor."));
    }

    private static void ExigirStPropria(string codigo, IcmsStDto? st, Func<string, string> P, List<InconsistenciaFiscal> erros)
    {
        if (st is null || st.ModBcSt is null || st.BaseCalculoSt is null || st.AliquotaSt is null || st.ValorSt is null)
        {
            erros.Add(new(P("st"),
                $"{codigo} exige grupo 'st' com modBcSt, baseCalculoSt, aliquotaSt e valorSt."));
        }
    }

    private static void ExigirCreditoSimples(string csosn, IcmsDto icms, Func<string, string> P, List<InconsistenciaFiscal> erros)
    {
        if ((icms.PercentualCreditoSimples is null) != (icms.ValorCreditoSimples is null))
            erros.Add(new(P("valorCreditoSimples"),
                $"CSOSN {csosn}: informe percentualCreditoSimples e valorCreditoSimples juntos."));
    }

    private static void ProibirValorProprio(string codigo, IcmsDto icms, Func<string, string> P, List<InconsistenciaFiscal> erros)
    {
        if (icms.Valor is > 0)
            erros.Add(new(P("valor"),
                $"Código {codigo} indica isenção/não tributação — valor próprio {icms.Valor:N2} não é permitido."));
        if (icms.Cst is "60" or "40" or "41" or "50" && icms.St is not null &&
            (icms.St.BaseCalculoSt is not null || icms.St.AliquotaSt is not null || icms.St.ValorSt is not null))
            erros.Add(new(P("st"),
                $"Código {codigo} não admite ST própria — use os campos retidos (baseCalculoStRetida/valorStRetido)."));
    }

    private static void ValidarAritmetica(IcmsDto icms, Func<string, string> P, List<InconsistenciaFiscal> erros)
    {
        Conferir(P("valor"), icms.BaseCalculo, icms.Aliquota, icms.Valor, erros);

        if (icms.St is { } st)
        {
            Conferir(P("st.valorSt"), st.BaseCalculoSt, st.AliquotaSt, st.ValorSt, erros);
            Conferir(P("st.valorFcpSt"), st.BaseCalculoSt, st.FcpPercentualSt, st.ValorFcpSt, erros);
            Conferir(P("st.valorStRetido"), st.BaseCalculoStRetido, st.AliquotaStRetida, st.ValorStRetido, erros);
            Conferir(P("st.valorFcpStRetido"), st.BaseCalculoStRetido, st.FcpPercentualStRetido, st.ValorFcpStRetido, erros);
        }

        if (icms.FcpPercentual is not null && icms.ValorFcp is not null)
        {
            var baseFcp = icms.BaseCalculo ?? 0;
            if (Math.Abs(baseFcp * icms.FcpPercentual.Value / 100m - icms.ValorFcp.Value) > Tolerancia)
                erros.Add(new(P("valorFcp"),
                    $"Base {baseFcp:N2} × FCP {icms.FcpPercentual:N2}% difere do valorFcp informado ({icms.ValorFcp:N2})."));
        }

        if (icms.Cst == "51")
            Conferir(P("valorIcmsOperacao"), icms.BaseCalculo, icms.Aliquota, icms.ValorIcmsOperacao, erros);
    }

    private static void ValidarDifal(DifalDto? difal, Func<string, string> P, List<InconsistenciaFiscal> erros)
    {
        if (difal is null)
            return;

        if (difal.AliquotaInterestadual is not (4 or 7 or 12))
        {
            erros.Add(new(P("difal.aliquotaInterestadual"),
                "DIFAL exige aliquotaInterestadual = 4, 7 ou 12 (pICMSInter)."));
        }
        if (difal.BaseDestino is null || difal.ValorIcmsDestino is null || difal.ValorIcmsOrigem is null)
            erros.Add(new(P("difal"), "DIFAL exige baseDestino, valorIcmsDestino e valorIcmsOrigem."));

        Conferir(P("difal.valorIcmsDestino"), difal.BaseDestino, difal.AliquotaDestino, difal.ValorIcmsDestino, erros);
        Conferir(P("difal.valorFcpDestino"), difal.BaseDestino, difal.FcpPercentualDestino, difal.ValorFcpDestino, erros);
    }

    private static void Conferir(string campo, decimal? baseCalculo, decimal? aliquota, decimal? valor, List<InconsistenciaFiscal> erros)
    {
        if (baseCalculo is { } b && aliquota is { } a && valor is { } v &&
            Math.Abs(b * a / 100m - v) > Tolerancia)
        {
            erros.Add(new(campo, $"Base {b:N2} × alíquota {a:N4}% = {b * a / 100m:N2}, recebido {v:N2}."));
        }
    }
}
