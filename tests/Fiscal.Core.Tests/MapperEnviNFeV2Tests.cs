using Fiscal.Adapters.Unimake;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using FluentAssertions;
using Unimake.Business.DFe.Servicos;
using Xunit;

namespace Fiscal.Core.Tests;

/// <summary>
/// Grupos de ICMS do contrato v2 no mapper (CST 00–90, CSOSN 101–900, ST,
/// FCP e DIFAL). Confere o object model Unimake e os totais do ICMSTot.
/// </summary>
public class MapperEnviNFeV2Tests
{
    private static Tenant TenantCompleto() => new()
    {
        Id = Guid.NewGuid(),
        Cnpj = "12345678000199",
        RazaoSocial = "Empresa Teste LTDA",
        Uf = "PR",
        CodigoMunicipioIbge = "4106902",
        RegimeTributario = 3,
        InscricaoEstadual = "12345678-01",
        Logradouro = "Rua do Emitente",
        Numero = "100",
        Complemento = "Sala 5",
        Bairro = "Centro",
        Cep = "80000000",
        NomeMunicipio = "Curitiba",
    };

    private static DocumentoFiscal Documento() => new()
    {
        Id = Guid.NewGuid(),
        Tipo = TipoDocumento.NFE,
        Ambiente = (short)Ambiente.Homologacao,
        Modelo = 55,
        Serie = 1,
        Numero = 42,
        PayloadEntrada = "{}",
    };

    private static ItemDto ItemV2(IcmsDto icms) => new(
        Codigo: "SKU1", Descricao: "Produto Teste", Ncm: "12345678", Cfop: "5102",
        Quantidade: 2, ValorUnitario: 50, ValorTotal: 100,
        Impostos: null, ImpostosV2: new ItemImpostosDtoV2(icms));

    private static EmissaoRequest Request(ItemDto item) => new(
        Ambiente: "homologacao",
        Serie: 1,
        Destinatario: new DestinatarioDto(
            CnpjCpf: "12345678000199",
            Nome: "Destinatario Teste",
            InscricaoEstadual: null,
            Endereco: new EnderecoDto(
                Cep: "80000000", Logradouro: "Rua do Destino", Numero: "10",
                Complemento: null, Bairro: "Centro",
                CodigoMunicipioIbge: "4106902", Uf: "SP", NomeMunicipio: "São Paulo")),
        Itens: [item],
        Totais: new TotaisDto(ValorProdutos: 100, ValorNota: 100),
        Pagamento: [new PagamentoDto("90", 0)],
        NaturezaOperacao: "VENDA");

    private static Unimake.Business.DFe.Xml.NFe.InfNFe Mapear(ItemDto item) =>
        MapperEnviNFe.Criar(Documento(), TenantCompleto(), Request(item), Ambiente.Homologacao)
            .NFe[0].InfNFeField;

    [Fact]
    public void Csosn_102_simples_nacional_mapeia_icmssn102()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Csosn: "102")));

        var det = nfe.Det.Single();
        det.Imposto.ICMS!.ICMSSN102.Should().NotBeNull();
        det.Imposto.ICMS.ICMSSN102!.CSOSN.Should().Be("102");
        det.Imposto.ICMS.ICMSSN102.Orig.Should().Be(OrigemMercadoria.Nacional);
    }

    [Fact]
    public void Origem_estrangeira_6_mapeia_no_grupo()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 6, Csosn: "102")));

        nfe.Det.Single().Imposto.ICMS!.ICMSSN102!.Orig.Should().Be(OrigemMercadoria.Estrangeira6);
    }

    [Fact]
    public void Csosn_101_mapeia_credito_do_simples()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Csosn: "101",
            PercentualCreditoSimples: 2.72m, ValorCreditoSimples: 2.72m)));

        var sn101 = nfe.Det.Single().Imposto.ICMS!.ICMSSN101!;
        sn101.PCredSN.Should().Be(2.72);
        sn101.VCredICMSSN.Should().Be(2.72);
    }

    [Fact]
    public void Csosn_201_com_st_mapeia_icmssn201_e_totais_st()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Csosn: "201",
            St: new IcmsStDto(ModBcSt: "4", BaseCalculoSt: 130, AliquotaSt: 18, ValorSt: 23.40m))));

        var sn201 = nfe.Det.Single().Imposto.ICMS!.ICMSSN201!;
        sn201.CSOSN.Should().Be("201");
        sn201.ModBCST.Should().Be(ModalidadeBaseCalculoICMSST.MargemValorAgregado);
        sn201.VBCST.Should().Be(130);
        sn201.VICMSST.Should().Be(23.40);

        nfe.Total.ICMSTot.VBCST.Should().Be(130);
        nfe.Total.ICMSTot.VST.Should().Be(23.40);
    }

    [Fact]
    public void Csosn_201_sem_st_falha_alto()
    {
        var act = () => Mapear(ItemV2(new IcmsDto(Origem: 0, Csosn: "201")));

        act.Should().Throw<ErroNaoRecuperavelException>().WithMessage("*exige grupo 'st'*");
    }

    [Fact]
    public void Cst_20_com_reducao_e_fcp_mapeia_icms20()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Cst: "20",
            ModBc: "3", PercentualReducaoBc: 20,
            BaseCalculo: 80, Aliquota: 18, Valor: 14.40m,
            FcpPercentual: 2, ValorFcp: 1.60m)));

        var icms20 = nfe.Det.Single().Imposto.ICMS!.ICMS20!;
        icms20.PRedBC.Should().Be(20);
        icms20.VBC.Should().Be(80);
        icms20.VICMS.Should().Be(14.40);
        icms20.PFCP.Should().Be(2);
        icms20.VFCP.Should().Be(1.60);

        nfe.Total.ICMSTot.VFCP.Should().Be(1.60);
    }

    [Fact]
    public void Cst_10_mapeia_icms_e_st_juntos()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Cst: "10",
            BaseCalculo: 100, Aliquota: 18, Valor: 18,
            St: new IcmsStDto(ModBcSt: "6", BaseCalculoSt: 130, AliquotaSt: 18, ValorSt: 23.40m))));

        var icms10 = nfe.Det.Single().Imposto.ICMS!.ICMS10!;
        icms10.VBC.Should().Be(100);
        icms10.VICMS.Should().Be(18);
        icms10.VBCST.Should().Be(130);
        icms10.VICMSST.Should().Be(23.40);
    }

    [Fact]
    public void Cst_51_mapeia_diferimento()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Cst: "51",
            ModBc: "3", BaseCalculo: 100, Aliquota: 18,
            ValorIcmsOperacao: 18, PercentualDiferimento: 100, ValorIcmsDiferido: 18)));

        var icms51 = nfe.Det.Single().Imposto.ICMS!.ICMS51!;
        icms51.VICMSOp.Should().Be(18);
        icms51.PDif.Should().Be(100);
        icms51.VICMSDif.Should().Be(18);
    }

    [Fact]
    public void Cst_60_mapeia_st_retida_sem_somar_no_vicms()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Cst: "60",
            St: new IcmsStDto(BaseCalculoStRetido: 100, AliquotaStRetida: 18,
                ValorStRetido: 18, ValorIcmsSubstituto: 17))));

        var icms60 = nfe.Det.Single().Imposto.ICMS!.ICMS60!;
        icms60.VBCSTRet.Should().Be(100);
        icms60.PST.Should().Be(18);
        icms60.VICMSSTRet.Should().Be(18);
        icms60.VICMSSubstituto.Should().Be(17);

        // ST retida não compõe vBC/vICMS próprios.
        nfe.Total.ICMSTot.VBC.Should().Be(0);
        nfe.Total.ICMSTot.VICMS.Should().Be(0);
    }

    [Fact]
    public void Csosn_500_mapeia_st_retida()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Csosn: "500",
            St: new IcmsStDto(BaseCalculoStRetido: 100, ValorStRetido: 18))));

        var sn500 = nfe.Det.Single().Imposto.ICMS!.ICMSSN500!;
        sn500.CSOSN.Should().Be("500");
        sn500.VBCSTRet.Should().Be(100);
        sn500.VICMSSTRet.Should().Be(18);
    }

    [Fact]
    public void Csosn_900_aceita_grupos_parciais_e_st()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Csosn: "900",
            ModBc: "3", BaseCalculo: 100, Aliquota: 18, Valor: 18,
            St: new IcmsStDto(ModBcSt: "4", BaseCalculoSt: 130, AliquotaSt: 18, ValorSt: 23.40m))));

        var sn900 = nfe.Det.Single().Imposto.ICMS!.ICMSSN900!;
        sn900.VBC.Should().Be(100);
        sn900.VICMS.Should().Be(18);
        sn900.VBCST.Should().Be(130);
        sn900.VICMSST.Should().Be(23.40);
    }

    [Fact]
    public void Difal_mapeia_icmsufdest_e_totais_da_partilha()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 12, Valor: 12,
            Difal: new DifalDto(AliquotaInterestadual: 12,
                BaseDestino: 100, AliquotaDestino: 18,
                ValorIcmsDestino: 18, ValorIcmsOrigem: 0))));

        var ufDest = nfe.Det.Single().Imposto.ICMSUFDest!;
        ufDest.VBCUFDest.Should().Be(100);
        ufDest.PICMSInter.Should().Be(12);
        ufDest.PICMSInterPart.Should().Be(100); // partilha 100% destino (Convênio 190/2017)
        ufDest.VICMSUFDest.Should().Be(18);
        ufDest.VICMSUFRemet.Should().Be(0);

        nfe.Total.ICMSTot.VICMSUFDest.Should().Be(18);
        nfe.Total.ICMSTot.VICMSUFRemet.Should().Be(0);
    }

    [Fact]
    public void Difal_com_fcp_mapeia_fcp_do_destino()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 12, Valor: 12,
            Difal: new DifalDto(AliquotaInterestadual: 7,
                BaseDestino: 100, ValorIcmsDestino: 18, ValorIcmsOrigem: 0,
                FcpPercentualDestino: 2, ValorFcpDestino: 2))));

        var ufDest = nfe.Det.Single().Imposto.ICMSUFDest!;
        ufDest.PFCPUFDest.Should().Be(2);
        ufDest.VFCPUFDest.Should().Be(2);
        nfe.Total.ICMSTot.VFCPUFDest.Should().Be(2);
    }

    [Fact]
    public void Difal_sem_aliquota_interestadual_falha_alto()
    {
        var act = () => Mapear(ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 12, Valor: 12,
            Difal: new DifalDto(AliquotaInterestadual: 18,
                BaseDestino: 100, ValorIcmsDestino: 18, ValorIcmsOrigem: 0))));

        act.Should().Throw<ErroNaoRecuperavelException>()
            .WithMessage("*aliquotaInterestadual = 4, 7 ou 12*");
    }

    [Fact]
    public void Cst_90_aceita_parcial_sem_st()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Cst: "90",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)));

        var icms90 = nfe.Det.Single().Imposto.ICMS!.ICMS90!;
        icms90.VBC.Should().Be(100);
        icms90.VICMS.Should().Be(18);
    }

    [Fact]
    public void Xml_gerado_contem_csosn()
    {
        var envi = MapperEnviNFe.Criar(Documento(), TenantCompleto(),
            Request(ItemV2(new IcmsDto(Origem: 0, Csosn: "102"))), Ambiente.Homologacao);

        envi.GerarXML().OuterXml.Should().Contain("<CSOSN>102</CSOSN>");
    }

    [Fact]
    public void Xml_gerado_contem_grupo_icmsufdest()
    {
        var envi = MapperEnviNFe.Criar(Documento(), TenantCompleto(),
            Request(ItemV2(new IcmsDto(Origem: 0, Cst: "00",
                BaseCalculo: 100, Aliquota: 12, Valor: 12,
                Difal: new DifalDto(AliquotaInterestadual: 12,
                    BaseDestino: 100, ValorIcmsDestino: 18, ValorIcmsOrigem: 0)))),
            Ambiente.Homologacao);

        envi.GerarXML().OuterXml.Should().Contain("<ICMSUFDest>");
    }
    [Fact]
    public void Item_rico_mapeia_gtin_cest_unidade_e_desconto()
    {
        var item = ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)) with
        {
            Gtin = "7891234567890",
            Cest = "0100400",
            Unidade = "KG",
            ValorDesconto = 10,
        };
        var nfe = Mapear(item);

        var prod = nfe.Det.Single().Prod;
        prod.CEAN.Should().Be("7891234567890");
        prod.CEST.Should().Be("0100400");
        prod.UCom.Should().Be("KG");
        prod.UTrib.Should().Be("KG");
        prod.VDesc.Should().Be(10);
        nfe.Total.ICMSTot.VDesc.Should().Be(10);
    }

    [Fact]
    public void Frete_seguro_e_outras_vao_para_icmstot_e_modfrete()
    {
        var req = Request(ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18))) with
        {
            Totais = new TotaisDto(ValorProdutos: 100, ValorNota: 165,
                ValorFrete: 50, ValorSeguro: 10, OutrasDespesas: 5),
        };
        var nfe = MapperEnviNFe.Criar(Documento(), TenantCompleto(), req, Ambiente.Homologacao)
            .NFe[0].InfNFeField;

        var tot = nfe.Total.ICMSTot;
        tot.VFrete.Should().Be(50);
        tot.VSeg.Should().Be(10);
        tot.VOutro.Should().Be(5);
    }

    [Fact]
    public void Ipi_tributado_mapeia_ipitrib_e_soma_no_total()
    {
        var item = ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)) with
        {
            ImpostosV2 = new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                Ipi: new IpiDto(Cst: "00", BaseCalculo: 100, Aliquota: 10, Valor: 10)),
        };
        var nfe = Mapear(item);

        var ipiTrib = nfe.Det.Single().Imposto.IPI!.IPITrib!;
        ipiTrib.CST.Should().Be("00");
        ipiTrib.VBC.Should().Be(100);
        ipiTrib.PIPI.Should().Be(10);
        ipiTrib.VIPI.Should().Be(10);
        nfe.Total.ICMSTot.VIPI.Should().Be(10);
    }

    [Fact]
    public void Pis_e_cofins_tributados_mapeiam_e_somam_no_total()
    {
        var item = ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)) with
        {
            ImpostosV2 = new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                Pis: new PisDto(Cst: "01", BaseCalculo: 100, Aliquota: 1.65m, Valor: 1.65m),
                Cofins: new CofinsDto(Cst: "01", BaseCalculo: 100, Aliquota: 7.6m, Valor: 7.6m)),
        };
        var nfe = Mapear(item);

        var imposto = nfe.Det.Single().Imposto;
        imposto.PIS!.PISAliq!.CST.Should().Be("01");
        imposto.PIS.PISAliq.VPIS.Should().Be(1.65);
        imposto.COFINS!.COFINSAliq!.CST.Should().Be("01");
        imposto.COFINS.COFINSAliq.VCOFINS.Should().Be(7.6);
        nfe.Total.ICMSTot.VPIS.Should().Be(1.65);
        nfe.Total.ICMSTot.VCOFINS.Should().Be(7.6);
    }

    [Fact]
    public void Pis_isento_mapeia_pisnt()
    {
        var item = ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)) with
        {
            ImpostosV2 = new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                Pis: new PisDto(Cst: "04")),
        };

        Mapear(item).Det.Single().Imposto.PIS!.PISNT!.CST.Should().Be("04");
    }
    [Fact]
    public void Ide_configuravel_finalidade_tpoperacao_indpres_indfinal()
    {
        var req = Request(ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18))) with
        {
            Finalidade = "devolucao",
            TipoOperacao = "entrada",
            IndicadorPresenca = "internet",
            IndicadorConsumidorFinal = "nao",
            NfesReferenciadas = [new NfRefDto("41260912345678000199550010000001001000123456")],
        };
        var nfe = MapperEnviNFe.Criar(Documento(), TenantCompleto(), req, Ambiente.Homologacao)
            .NFe[0].InfNFeField;

        nfe.Ide.FinNFe.Should().Be(FinalidadeNFe.Devolucao);
        nfe.Ide.TpNF.Should().Be(TipoOperacao.Entrada);
        nfe.Ide.IndPres.Should().Be(IndicadorPresenca.OperacaoInternet);
        nfe.Ide.IndFinal.Should().Be(SimNao.Nao);
        nfe.Ide.NFref!.Single().RefNFe.Should().Be("41260912345678000199550010000001001000123456");
    }

    [Fact]
    public void Ide_defaults_mantidos_sem_campos_novos()
    {
        var nfe = Mapear(ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)));

        nfe.Ide.FinNFe.Should().Be(FinalidadeNFe.Normal);
        nfe.Ide.TpNF.Should().Be(TipoOperacao.Saida);
        nfe.Ide.IndFinal.Should().Be(SimNao.Sim);
        nfe.Ide.NFref.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Chave_referenciada_invalida_falha_alto()
    {
        var req = Request(ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18))) with
        {
            NfesReferenciadas = [new NfRefDto("123")],
        };

        var act = () => MapperEnviNFe.Criar(Documento(), TenantCompleto(), req, Ambiente.Homologacao);

        act.Should().Throw<ErroNaoRecuperavelException>().WithMessage("*44 dígitos*");
    }
    [Fact]
    public void Reforma_mapeia_ibscbs_e_is_com_totais()
    {
        var item = ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)) with
        {
            ImpostosV2 = new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                IbsCbs: new IbsCbsDto(
                    CstIbsCbs: "101", CClassTrib: "000001", BaseCalculo: 100,
                    AliquotaIbsEstadual: 0.9m, ValorIbsEstadual: 0.9m,
                    AliquotaIbsMunicipal: 0.1m, ValorIbsMunicipal: 0.1m,
                    AliquotaCbs: 1, ValorCbs: 1),
                Is: new IsDto(CstIs: "01", CClassTribIs: "000005",
                    BaseCalculo: 100, Aliquota: 10, Valor: 10)),
        };
        var nfe = Mapear(item);

        var ibscbs = nfe.Det.Single().Imposto.IBSCBS!;
        ibscbs.CST.Should().Be("101");
        ibscbs.CClassTrib.Should().Be("000001");
        ibscbs.GIBSCBS!.GIBSUF!.VIBSUF.Should().Be(0.9);
        ibscbs.GIBSCBS.GIBSMun!.VIBSMun.Should().Be(0.1);
        ibscbs.GIBSCBS.GCBS!.VCBS.Should().Be(1);

        var impostoIs = nfe.Det.Single().Imposto.IS!;
        impostoIs.CSTIS.Should().Be("01");
        impostoIs.VIS.Should().Be(10);

        var ibscbsTot = nfe.Total.IBSCBSTot!;
        ibscbsTot.VBCIBSCBS.Should().Be(100);
        ibscbsTot.GIBS!.VIBS.Should().Be(1);
        ibscbsTot.GCBS!.VCBS.Should().Be(1);
        nfe.Total.ISTot!.VIS.Should().Be(10);
    }

    [Fact]
    public void Reforma_sem_cclasstrib_falha_alto()
    {
        var item = ItemV2(new IcmsDto(Origem: 0, Cst: "00",
            BaseCalculo: 100, Aliquota: 18, Valor: 18)) with
        {
            ImpostosV2 = new ItemImpostosDtoV2(
                Icms: new IcmsDto(Origem: 0, Cst: "00", BaseCalculo: 100, Aliquota: 18, Valor: 18),
                IbsCbs: new IbsCbsDto(CstIbsCbs: "101", CClassTrib: "", BaseCalculo: 100)),
        };

        var act = () => Mapear(item);
        act.Should().Throw<ErroNaoRecuperavelException>().WithMessage("*cClassTrib*");
    }
}
