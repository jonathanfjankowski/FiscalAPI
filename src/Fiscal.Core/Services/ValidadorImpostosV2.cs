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

    public IReadOnlyList<InconsistenciaFiscal> Validar(IReadOnlyList<ItemDto> itens, bool nfce = false)
    {
        var erros = new List<InconsistenciaFiscal>();
        for (var i = 0; i < itens.Count; i++)
        {
            var v2 = itens[i].ImpostosV2;
            if (v2 is null)
                continue;

            if (v2.Icms is { } icms)
                ValidarIcms(icms, i, itens[i].Codigo, erros);
            ValidarFederais(v2, i, nfce, erros);
            ValidarReforma(v2, i, erros);
        }
        return erros;
    }

    /// <summary>
    /// Fórmula do total da nota (v2 §5.2): qualquer campo novo de total
    /// presente troca a regra — valorNota = Σ brutos − descontos + frete +
    /// seguro + outras + ST + FCP-ST + IPI (tolerância R$ 0,01).
    /// </summary>
    /// <summary>Indica se o payload usa a fórmula v2 do total (algum campo novo presente).</summary>
    public static bool FormulaV2Ativa(TotaisDto totais, IReadOnlyList<ItemDto> itens) =>
        totais.ValorDesconto is not null || totais.ValorFrete is not null ||
        totais.ValorSeguro is not null || totais.OutrasDespesas is not null ||
        itens.Any(i => i.ValorDesconto is not null || i.ImpostosV2?.Ipi is not null);

    public IReadOnlyList<InconsistenciaFiscal> ValidarTotais(TotaisDto totais, IReadOnlyList<ItemDto> itens)
    {
        var erros = new List<InconsistenciaFiscal>();

        if (!FormulaV2Ativa(totais, itens))
            return erros;

        var brutos = itens.Sum(i => i.ValorTotal);
        var descontos = itens.Sum(i => i.ValorDesconto ?? 0) + (totais.ValorDesconto ?? 0);
        var st = itens.Sum(i => i.ImpostosV2?.Icms?.St?.ValorSt ?? 0);
        var fcpSt = itens.Sum(i => i.ImpostosV2?.Icms?.St?.ValorFcpSt ?? 0);
        var ipi = itens.Sum(i => i.ImpostosV2?.Ipi?.Valor ?? 0);

        var esperado = brutos - descontos + (totais.ValorFrete ?? 0) + (totais.ValorSeguro ?? 0) +
                       (totais.OutrasDespesas ?? 0) + st + fcpSt + ipi;

        if (Math.Abs(esperado - totais.ValorNota) > Tolerancia)
        {
            erros.Add(new InconsistenciaFiscal("valorNota",
                $"Fórmula v2: {brutos:N2} − {descontos:N2} (descontos) + frete/seguro/outras + ST {st:N2} + FCP-ST {fcpSt:N2} + IPI {ipi:N2} = {esperado:N2}, recebido {totais.ValorNota:N2}."));
        }

        return erros;
    }

    private static void ValidarFederais(ItemImpostosDtoV2 v2, int indice, bool nfce, List<InconsistenciaFiscal> erros)
    {
        if (nfce && (v2.Ipi is not null || v2.Pis is not null || v2.Cofins is not null))
        {
            erros.Add(new InconsistenciaFiscal($"itens[{indice}].impostosV2",
                "NFC-e não admite os grupos IPI/PIS/COFINS."));
            return;
        }

        if (v2.Ipi is { } ipi)
        {
            var cst = ipi.Cst?.Trim();
            if (cst is null)
                erros.Add(new InconsistenciaFiscal($"itens[{indice}].impostosV2.ipi.cst", "CST do IPI é obrigatório."));
            else if (cst is "00" or "49" or "50" or "99")
            {
                if (ipi.BaseCalculo is null || ipi.Aliquota is null || ipi.Valor is null)
                    erros.Add(new InconsistenciaFiscal($"itens[{indice}].impostosV2.ipi.valor",
                        $"IPI CST {cst} exige baseCalculo, aliquota e valor."));
                else
                    Conferir($"itens[{indice}].impostosV2.ipi.valor", ipi.BaseCalculo, ipi.Aliquota, ipi.Valor, erros);
            }
            else if (cst is "01" or "02" or "03" or "04" or "05" or "51")
            {
                if (ipi.Valor is > 0)
                    erros.Add(new InconsistenciaFiscal($"itens[{indice}].impostosV2.ipi.valor",
                        $"IPI CST {cst} não tributado — valor não é permitido."));
            }
            else
            {
                erros.Add(new InconsistenciaFiscal($"itens[{indice}].impostosV2.ipi.cst",
                    $"IPI CST '{cst}' fora do contrato (00, 01–05, 49, 50, 51, 99)."));
            }
        }

        if (v2.Pis is { } pis)
            ValidarPisCofins(pis.Cst, "pis", indice, pis.BaseCalculo, pis.Aliquota, pis.Valor, erros);
        if (v2.Cofins is { } cofins)
            ValidarPisCofins(cofins.Cst, "cofins", indice, cofins.BaseCalculo, cofins.Aliquota, cofins.Valor, erros);
    }

    private static void ValidarPisCofins(
        string? cst, string grupo, int indice,
        decimal? baseCalculo, decimal? aliquota, decimal? valor,
        List<InconsistenciaFiscal> erros)
    {
        var campo = $"itens[{indice}].impostosV2.{grupo}";
        cst = cst?.Trim();
        if (cst is null)
        {
            erros.Add(new InconsistenciaFiscal($"{campo}.cst", $"CST do {grupo.ToUpperInvariant()} é obrigatório."));
            return;
        }

        if (cst is "03")
        {
            erros.Add(new InconsistenciaFiscal($"{campo}.cst",
                $"{grupo.ToUpperInvariant()} CST 03 (por quantidade) não suportado no contrato atual."));
            return;
        }

        if (cst is "01" or "02")
        {
            if (baseCalculo is null || aliquota is null || valor is null)
                erros.Add(new InconsistenciaFiscal($"{campo}.valor",
                    $"{grupo.ToUpperInvariant()} CST {cst} exige baseCalculo, aliquota e valor."));
            else
                Conferir($"{campo}.valor", baseCalculo, aliquota, valor, erros);
            return;
        }

        if (cst is "04" or "05" or "06" or "07" or "08" or "09")
        {
            if (valor is > 0)
                erros.Add(new InconsistenciaFiscal($"{campo}.valor",
                    $"{grupo.ToUpperInvariant()} CST {cst} isento — valor não é permitido."));
            return;
        }

        if (cst is "99")
            Conferir($"{campo}.valor", baseCalculo, aliquota, valor, erros);
        else
            erros.Add(new InconsistenciaFiscal($"{campo}.cst",
                $"{grupo.ToUpperInvariant()} CST '{cst}' fora do contrato (01, 02, 04–09, 99)."));
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
    private static void ValidarReforma(ItemImpostosDtoV2 v2, int indice, List<InconsistenciaFiscal> erros)
    {
        if (v2.IbsCbs is { } ibs)
        {
            var campo = $"itens[{indice}].impostosV2.ibsCbs";
            if (string.IsNullOrWhiteSpace(ibs.CstIbsCbs) || ibs.CstIbsCbs.Length != 3)
                erros.Add(new InconsistenciaFiscal($"{campo}.cstIbsCbs", "CST do IBS/CBS deve ter 3 dígitos (tabela SEPEC)."));
            if (string.IsNullOrWhiteSpace(ibs.CClassTrib) || ibs.CClassTrib.Length != 6)
                erros.Add(new InconsistenciaFiscal($"{campo}.cClassTrib", "cClassTrib é obrigatório e deve ter 6 dígitos (tabela SEPEC)."));

            Conferir($"{campo}.valorIbsEstadual", ibs.BaseCalculo, ibs.AliquotaIbsEstadual, ibs.ValorIbsEstadual, erros);
            Conferir($"{campo}.valorIbsMunicipal", ibs.BaseCalculo, ibs.AliquotaIbsMunicipal, ibs.ValorIbsMunicipal, erros);
            Conferir($"{campo}.valorCbs", ibs.BaseCalculo, ibs.AliquotaCbs, ibs.ValorCbs, erros);
        }

        if (v2.Is is { } isDto)
        {
            var campo = $"itens[{indice}].impostosV2.is";
            if (string.IsNullOrWhiteSpace(isDto.CstIs) || isDto.CstIs.Length != 2)
                erros.Add(new InconsistenciaFiscal($"{campo}.cstIs", "CST do IS deve ter 2 dígitos (tabela SEPEC)."));
            if (string.IsNullOrWhiteSpace(isDto.CClassTribIs) || isDto.CClassTribIs.Length != 6)
                erros.Add(new InconsistenciaFiscal($"{campo}.cClassTribIs", "cClassTribIs é obrigatório e deve ter 6 dígitos."));

            if ((isDto.UnidadeTributavel is not null) != (isDto.QuantidadeTributavel is not null))
                erros.Add(new InconsistenciaFiscal($"{campo}.quantidadeTributavel",
                    "IS por quantidade: informe unidadeTributavel e quantidadeTributavel juntos."));

            Conferir($"{campo}.valor", isDto.BaseCalculo, isDto.Aliquota, isDto.Valor, erros);
        }
    }

}
