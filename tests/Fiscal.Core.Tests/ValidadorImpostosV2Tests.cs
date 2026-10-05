using System.Text.RegularExpressions;
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

        // Mensagem usa :N2 com a cultura corrente: pt-BR → "18,00"; invariante → "18.00".
        erros.Should().Contain(e => Regex.IsMatch(e, @"= 18[.,]00, recebido 19[.,]00"));
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
    public void Difal_valor_icms_destino_com_interna_cheia_falha()
    {
        // Fórmula do MOC (rejeições SEFAZ 815/816): vICMSUFDest =
        // BC × (interna − interestadual). Informar BC × interna cheia rejeita.
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 12, Valor: 12,
            Difal: new DifalDto(AliquotaInterestadual: 12,
                BaseDestino: 100, AliquotaDestino: 18,
                ValorIcmsDestino: 18, ValorIcmsOrigem: 0))));

        erros.Should().Contain(e => e.Contains("vICMSUFDest = BC × (interna − interestadual)"));
    }

    [Fact]
    public void Difal_com_diferencial_correto_passa()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 12, Valor: 12,
            Difal: new DifalDto(AliquotaInterestadual: 12,
                BaseDestino: 100, AliquotaDestino: 18,
                ValorIcmsDestino: 6, ValorIcmsOrigem: 0))));

        erros.Should().BeEmpty();
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
    [Fact]
    public void Pis_isento_com_valor_falha()
    {
        var item = new ItemDto(
            Codigo: "SKU1", Descricao: "Produto", Ncm: "12345678", Cfop: "5102",
            Quantidade: 1, ValorUnitario: 100, ValorTotal: 100, Impostos: null,
            ImpostosV2: new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                Pis: new PisDto(Cst: "04", Valor: 1.65m)));

        Validar(item).Should().Contain(e => e.Contains("isento — valor não é permitido"));
    }

    [Fact]
    public void Pis_tributado_sem_trio_falha()
    {
        var item = new ItemDto(
            Codigo: "SKU1", Descricao: "Produto", Ncm: "12345678", Cfop: "5102",
            Quantidade: 1, ValorUnitario: 100, ValorTotal: 100, Impostos: null,
            ImpostosV2: new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                Pis: new PisDto(Cst: "01", BaseCalculo: 100)));

        Validar(item).Should().Contain(e => e.Contains("PIS CST 01 exige baseCalculo, aliquota e valor"));
    }

    [Fact]
    public void Aritmetica_do_pis_errada_falha()
    {
        var item = new ItemDto(
            Codigo: "SKU1", Descricao: "Produto", Ncm: "12345678", Cfop: "5102",
            Quantidade: 1, ValorUnitario: 100, ValorTotal: 100, Impostos: null,
            ImpostosV2: new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                Pis: new PisDto(Cst: "01", BaseCalculo: 100, Aliquota: 1.65m, Valor: 5)));

        Validar(item).Should().Contain(e => e.Contains("impostosV2.pis.valor"));
    }

    [Fact]
    public void Nfce_com_grupo_federal_falha()
    {
        var item = new ItemDto(
            Codigo: "SKU1", Descricao: "Produto", Ncm: "12345678", Cfop: "5102",
            Quantidade: 1, ValorUnitario: 100, ValorTotal: 100, Impostos: null,
            ImpostosV2: new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                Cofins: new CofinsDto(Cst: "01", BaseCalculo: 100, Aliquota: 7.6m, Valor: 7.6m)));

        var erros = new ValidadorImpostosV2().Validar([item], nfce: true);
        erros.Should().Contain(e => e.Mensagem.Contains("NFC-e não admite"));
    }

    [Fact]
    public void Formula_v2_do_total_conferida_campo_a_campo()
    {
        var itens = new List<ItemDto>
        {
            new("SKU1", "Produto", "12345678", "5102", 1, 100, 100, Impostos: null,
                ImpostosV2: new ItemImpostosDtoV2(
                    Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                    Ipi: new IpiDto(Cst: "00", BaseCalculo: 100, Aliquota: 10, Valor: 10),
                    Pis: new PisDto(Cst: "01", BaseCalculo: 100, Aliquota: 1.65m, Valor: 1.65m),
                    Cofins: new CofinsDto(Cst: "01", BaseCalculo: 100, Aliquota: 7.6m, Valor: 7.6m)),
                ValorDesconto: 10),
        };
        var totais = new TotaisDto(ValorProdutos: 100, ValorNota: 210.65m,
            ValorFrete: 100, ValorSeguro: 10, OutrasDespesas: 5);

        // 100 − 10 + 100 + 10 + 5 + IPI 10 = 215? Não: ST/FCP-ST 0, IPI 10 → 215. ValorNota errado de propósito abaixo.
        var errosErrado = new ValidadorImpostosV2().ValidarTotais(totais, itens);
        errosErrado.Should().NotBeEmpty();

        var totaisOk = totais with { ValorNota = 215 };
        var errosOk = new ValidadorImpostosV2().ValidarTotais(totaisOk, itens);
        errosOk.Should().BeEmpty();
    }

    [Fact]
    public void Formula_v2_somente_ativa_com_campo_novo()
    {
        // Payload legado: soma simples dos itens, sem campos novos.
        var itens = new List<ItemDto>
        {
            new("SKU1", "Produto", "12345678", "5102", 2, 50, 100, Impostos: null),
        };
        var totais = new TotaisDto(ValorProdutos: 100, ValorNota: 100);

        var erros = new ValidadorImpostosV2().ValidarTotais(totais, itens);
        erros.Should().BeEmpty();
    }
    [Fact]
    public void Reforma_com_sepec_incompleto_falha()
    {
        var item = new ItemDto(
            Codigo: "SKU1", Descricao: "Produto", Ncm: "12345678", Cfop: "5102",
            Quantidade: 1, ValorUnitario: 100, ValorTotal: 100, Impostos: null,
            ImpostosV2: new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                IbsCbs: new IbsCbsDto(
                    CstIbsCbs: "1", CClassTrib: "000001", BaseCalculo: 100,
                    AliquotaCbs: 1, ValorCbs: 2)));

        Validar(item).Should().Contain(e => e.Contains("CST do IBS/CBS deve ter 3 dígitos"))
                             .And.Contain(e => e.Contains("valorCbs"));
    }

    [Fact]
    public void Is_por_quantidade_incompleto_falha()
    {
        var item = new ItemDto(
            Codigo: "SKU1", Descricao: "Produto", Ncm: "12345678", Cfop: "5102",
            Quantidade: 1, ValorUnitario: 100, ValorTotal: 100, Impostos: null,
            ImpostosV2: new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                Is: new IsDto(CstIs: "01", CClassTribIs: "000005",
                    BaseCalculo: 100, Aliquota: 10, Valor: 10,
                    UnidadeTributavel: "KG")));

        Validar(item).Should().Contain(e => e.Contains("unidadeTributavel e quantidadeTributavel juntos"));
    }

    // --------------------------------------------------- espelho FiscalLIB V004
    // A FiscalLIB emite base × alíquota / 100 com arredondamento bancário
    // (half-to-even) em 2 casas. Estes casos travam o contrato entre as duas
    // pontas: os valores V004 da lib passam na tolerância de R$ 0,01; acima
    // dela, rejeita. Espelho dos casos de tests/Unit/MatematicaTest.php.

    [Fact]
    public void Valores_arredondamento_bancario_V004_da_lib_passam()
    {
        // 100 × 12,3456% = 12,3456 → V004 = 12,35 (o truncamento daria 12,34)
        // 333,33 × 18% = 59,9994 → V004 = 60,00 (o truncamento daria 59,99)
        // 10 × 0,05% = 0,005 → V004 = 0,00 (meio → dígito par) · 10 × 0,15% = 0,015 → V004 = 0,02
        var casos = new (decimal BaseCalculo, decimal Aliquota, decimal Valor)[]
        {
            (100m, 12.3456m, 12.35m),
            (333.33m, 18m, 60.00m),
            (10m, 0.05m, 0.00m),
            (10m, 0.15m, 0.02m),
        };

        foreach (var (bc, aliq, valor) in casos)
        {
            var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
                BaseCalculo: bc, Aliquota: aliq, Valor: valor)));

            erros.Should().BeEmpty($"base {bc} × {aliq}% = V004 {valor} deve passar");
        }
    }

    [Fact]
    public void Valor_alem_da_tolerancia_de_um_centavo_falha()
    {
        // 333,33 × 18% = 59,9994 — V004 60,00 passa; 59,98 difere 0,0194 > 0,01
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 333.33m, Aliquota: 18, Valor: 59.98m)));

        erros.Should().Contain(e => e.Contains("impostosV2.icms.valor"));
    }

    [Fact]
    public void Cst_51_com_diferimento_minimo_omitindo_valor_passa()
    {
        // pDif 0,004 (< 0,005): a lib agora compara em 4 casas — diferimento
        // existe, então `valor` (próprio) fica nulo e vICMSDif vai 0,00.
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "51",
            BaseCalculo: 1000, Aliquota: 12,
            ValorIcmsOperacao: 120, PercentualDiferimento: 0.004m, ValorIcmsDiferido: 0.00m)));

        erros.Should().BeEmpty();
    }

    [Fact]
    public void Difal_com_interna_menor_que_interestadual_falha()
    {
        // Espelho da guarda da FiscalLIB: vICMSUFDest negativo é entrada
        // impossível (pICMSUFDest < pICMSInter).
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 12, Valor: 12,
            Difal: new DifalDto(AliquotaInterestadual: 12,
                BaseDestino: 100, AliquotaDestino: 10,
                ValorIcmsDestino: -2, ValorIcmsOrigem: 0))));

        erros.Should().Contain(e => e.Contains("vICMSUFDest negativo"));
    }

    [Fact]
    public void Difal_com_interna_igual_interestadual_passa()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 12, Valor: 12,
            Difal: new DifalDto(AliquotaInterestadual: 12,
                BaseDestino: 100, AliquotaDestino: 12,
                ValorIcmsDestino: 0, ValorIcmsOrigem: 0))));

        erros.Should().BeEmpty();
    }

    [Fact]
    public void Desoneracao_cst_40_correta_passa()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "40",
            BaseCalculo: 100, Aliquota: 18, ValorDesonerado: 18, MotivoDesoneracao: "9")));

        erros.Should().BeEmpty();
    }

    [Fact]
    public void Desoneracao_cst_40_aritmetica_errada_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "40",
            BaseCalculo: 100, Aliquota: 18, ValorDesonerado: 20, MotivoDesoneracao: "9")));

        erros.Should().Contain(e => e.Contains("valorDesonerado") && e.Contains("Base cheia"));
    }

    [Fact]
    public void Desoneracao_motivo_invalido_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "40",
            BaseCalculo: 100, Aliquota: 18, ValorDesonerado: 18, MotivoDesoneracao: "5")));

        erros.Should().Contain(e => e.Contains("motivoDesoneracao = 3, 9 ou 12"));
    }

    [Fact]
    public void Desoneracao_motivo_sem_valor_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "40",
            BaseCalculo: 100, Aliquota: 18, MotivoDesoneracao: "9")));

        erros.Should().Contain(e => e.Contains("motivoDesoneracao informado sem valorDesonerado"));
    }

    [Fact]
    public void Desoneracao_csosn_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Csosn: "102", ValorDesonerado: 18, MotivoDesoneracao: "9")));

        erros.Should().Contain(e => e.Contains("não se aplica ao Simples Nacional"));
    }

    [Fact]
    public void Desoneracao_cst_nao_admissivel_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18, ValorDesonerado: 18, MotivoDesoneracao: "9")));

        erros.Should().Contain(e => e.Contains("não se aplica ao CST '00'"));
    }

    [Fact]
    public void Codigo_beneficio_fiscal_muito_longo_falha()
    {
        var erros = Validar(Item(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18, CodigoBeneficioFiscal: "RBC123456789")));

        erros.Should().Contain(e => e.Contains("codigoBeneficioFiscal"));
    }

    [Fact]
    public void Total_subtrai_desonerado()
    {
        var itens = new List<ItemDto>
        {
            new("SKU1", "Produto", "12345678", "5102", 1, 100, 100, Impostos: null,
                ImpostosV2: new ItemImpostosDtoV2(
                    Icms: new IcmsDto(Origem: 0, Cst: "40",
                        BaseCalculo: 100, Aliquota: 18, ValorDesonerado: 18, MotivoDesoneracao: "9"))),
        };

        // 100 bruto − 18 desonerado = 82
        var totaisOk = new TotaisDto(ValorProdutos: 100, ValorNota: 82, ValorDesonerado: 18);
        new ValidadorImpostosV2().ValidarTotais(totaisOk, itens).Should().BeEmpty();

        var totaisErrado = totaisOk with { ValorNota = 100 };
        var erros = new ValidadorImpostosV2().ValidarTotais(totaisErrado, itens);
        erros.Should().Contain(e => e.Mensagem.Contains("desonerado"));
    }
}
