using Fiscal.Core.Contracts;
using Fiscal.Core.Services;
using FluentAssertions;
using Xunit;

namespace Fiscal.Core.Tests;

/// <summary>
/// Regras declarativas do contrato v2 (docs/plano-evolucao-contrato-v2.md §4):
/// CST ou CSOSN, obrigatoriedade por código, isento × valor e aritmética
/// por grupo (tolerância R$ 0,01).
/// </summary>
public class ValidadorImpostosV2Tests
{
    private static ItemDto Item(IcmsDto icms) => new(
        Codigo: "SKU1", Descricao: "Produto", Ncm: "12345678", Cfop: "5102",
        Quantidade: 1, ValorUnitario: 100, ValorTotal: 100,
        Impostos: null, ImpostosV2: new ItemImpostosDtoV2(icms));

    private static string[] Validar(ItemDto item)
    {
        var erros = new ValidadorImpostosV2().Validar([item]);
        return [.. erros.Select(e => $"{e.Campo}: {e.Mensagem}")];
    }

    [Fact]
    public void Cst_e_csosn_juntos_falham()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00", Csosn: "102",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)));

        erros.Should().Contain(e => e.Contains("nunca os dois"));
    }

    [Fact]
    public void Sem_cst_e_sem_csosn_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0)));

        erros.Should().Contain(e => e.Contains("'cst' (regime normal) ou 'csosn'"));
    }

    [Fact]
    public void Cst_fora_do_contrato_falha_com_mensagem_explicita()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "30")));

        erros.Should().Contain(e => e.Contains("CST '30' não suportado"));
    }

    [Fact]
    public void Csosn_fora_do_contrato_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Csosn: "4000")));

        erros.Should().Contain(e => e.Contains("CSOSN '4000' não suportado"));
    }

    [Fact]
    public void Origem_invalida_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 9, Csosn: "102")));

        erros.Should().Contain(e => e.Contains("Origem 9 inválida"));
    }

    [Fact]
    public void Cst_00_sem_trio_completo_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18)));

        erros.Should().Contain(e => e.Contains("CST 00 exige baseCalculo, aliquota e valor"));
    }

    [Fact]
    public void Cst_10_sem_st_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "10",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)));

        erros.Should().Contain(e => e.Contains("exige grupo 'st'"));
    }

    [Fact]
    public void Cst_40_com_valor_proprio_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "40",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)));

        erros.Should().Contain(e => e.Contains("indica isenção"));
    }

    [Fact]
    public void Cst_20_sem_reducao_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "20",
            BaseCalculo: 80, Aliquota: 18, Valor: 14.40m)));

        erros.Should().Contain(e => e.Contains("CST 20 exige percentualReducaoBc"));
    }

    [Fact]
    public void Cst_51_sem_vicmsop_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "51")));

        erros.Should().Contain(e => e.Contains("CST 51 exige valorIcmsOperacao"));
    }

    [Fact]
    public void Csosn_300_com_valor_proprio_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Csosn: "300", Valor: 10)));

        erros.Should().Contain(e => e.Contains("indica isenção"));
    }

    [Fact]
    public void Csosn_202_sem_st_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Csosn: "202")));

        erros.Should().Contain(e => e.Contains("202 exige grupo 'st'"));
    }

    [Fact]
    public void Aritmetica_do_icms_errada_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 19)));

        erros.Should().Contain(e => e.Contains("= 18,00, recebido 19,00"));
    }

    [Fact]
    public void Aritmetica_da_st_errada_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "10",
            BaseCalculo: 100, Aliquota: 18, Valor: 18,
            St: new IcmsStDto(ModBcSt: "4", BaseCalculoSt: 100, AliquotaSt: 18, ValorSt: 20))));

        erros.Should().Contain(e => e.Contains("st.valorSt"));
    }

    [Fact]
    public void Aritmetica_do_fcp_errada_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "20",
            PercentualReducaoBc: 20, BaseCalculo: 80, Aliquota: 18, Valor: 14.40m,
            FcpPercentual: 2, ValorFcp: 3)));

        erros.Should().Contain(e => e.Contains("valorFcp"));
    }

    [Fact]
    public void Credito_do_simples_incompleto_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Csosn: "101", PercentualCreditoSimples: 2.72m)));

        erros.Should().Contain(e => e.Contains("percentualCreditoSimples e valorCreditoSimples juntos"));
    }

    [Fact]
    public void Difal_sem_aliquota_interestadual_valida_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 12, Valor: 12,
            Difal: new DifalDto(AliquotaInterestadual: 18,
                BaseDestino: 100, AliquotaDestino: 18,
                ValorIcmsDestino: 18, ValorIcmsOrigem: 0))));

        erros.Should().Contain(e => e.Contains("aliquotaInterestadual = 4, 7 ou 12"));
    }

    [Fact]
    public void Difal_sem_valores_da_partilha_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 12, Valor: 12,
            Difal: new DifalDto(AliquotaInterestadual: 12))));

        erros.Should().Contain(e => e.Contains("exige baseDestino, valorIcmsDestino e valorIcmsOrigem"));
    }

    [Fact]
    public void Csosn_102_simples_passa_sem_erros()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Csosn: "102")));

        erros.Should().BeEmpty();
    }

    [Fact]
    public void Cst_00_completo_passa_sem_erros()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)));

        erros.Should().BeEmpty();
    }

    [Fact]
    public void Csosn_201_com_st_completa_passa_sem_erros()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Csosn: "201",
            St: new IcmsStDto(ModBcSt: "4", BaseCalculoSt: 100, AliquotaSt: 18, ValorSt: 18))));

        erros.Should().BeEmpty();
    }

    [Fact]
    public void Csosn_500_com_valores_retidos_passa_sem_erros()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Csosn: "500",
            St: new IcmsStDto(BaseCalculoStRetido: 100, AliquotaStRetida: 18, ValorStRetido: 18))));

        erros.Should().BeEmpty();
    }
}
