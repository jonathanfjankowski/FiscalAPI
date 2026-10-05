using System.Xml;
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
/// Testes do mapper PayloadEntrada → EnviNFe (Unimake). Não fala com SEFAZ —
/// valida o object model e o XML gerado (a Unimake calcula chave de acesso e cDV).
/// </summary>
public class MapperEnviNFeTests
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

    private static DocumentoFiscal Documento(TipoDocumento tipo) => new()
    {
        Id = Guid.NewGuid(),
        Tipo = tipo,
        Ambiente = (short)Ambiente.Homologacao,
        Modelo = (short)(tipo == TipoDocumento.NFCE ? 65 : 55),
        Serie = 1,
        Numero = 42,
        PayloadEntrada = "{}",
    };

    private static EmissaoRequest RequestValida() => new(
        Ambiente: "homologacao",
        Serie: 1,
        Destinatario: new DestinatarioDto(
            CnpjCpf: "12345678000199",
            Nome: "Destinatario Teste",
            InscricaoEstadual: null,
            Endereco: new EnderecoDto(
                Cep: "80000000", Logradouro: "Rua do Destino", Numero: "10",
                Complemento: null, Bairro: "Centro",
                CodigoMunicipioIbge: "4106902", Uf: "PR", NomeMunicipio: "Curitiba")),
        Itens:
        [
            new ItemDto(
                Codigo: "SKU1", Descricao: "Produto Teste", Ncm: "12345678", Cfop: "5102",
                Quantidade: 2, ValorUnitario: 50, ValorTotal: 100,
                Impostos: [new ImpostoDto("00", BaseCalculo: 100, Aliquota: 18, Valor: 18)]),
        ],
        Totais: new TotaisDto(ValorProdutos: 100, ValorNota: 100),
        Pagamento: [new PagamentoDto("01", 100)],
        NaturezaOperacao: "VENDA DE MERCADORIA");

    [Fact]
    public void NFe_mapeia_ide_emit_dest_itens_e_total()
    {
        var envi = MapperEnviNFe.Criar(
            Documento(TipoDocumento.NFE), TenantCompleto(), RequestValida(), Ambiente.Homologacao);

        envi.Versao.Should().Be("4.00");
        envi.IndSinc.Should().Be(SimNao.Sim); // lote unitário → síncrono (SEFAZ-PR rejeita 452 no assíncrono)
        envi.NFe.Should().HaveCount(1);

        var nfe = envi.NFe[0].InfNFeField;
        nfe.Ide.CUF.Should().Be(UFBrasil.PR);
        nfe.Ide.Mod.Should().Be(ModeloDFe.NFe);
        nfe.Ide.Serie.Should().Be(1);
        nfe.Ide.NNF.Should().Be(42);
        nfe.Ide.NatOp.Should().Be("VENDA DE MERCADORIA");
        nfe.Ide.TpAmb.Should().Be(TipoAmbiente.Homologacao);
        nfe.Ide.CNF.Should().HaveLength(8);

        nfe.Emit.CNPJ.Should().Be("12345678000199");
        nfe.Emit.IE.Should().Be("12345678-01");
        nfe.Emit.EnderEmit.XMun.Should().Be("Curitiba");

        nfe.Dest!.CNPJ.Should().Be("12345678000199");
        nfe.Dest.IndIEDest.Should().Be(IndicadorIEDestinatario.NaoContribuinte);

        var det = nfe.Det.Single();
        det.Prod.CProd.Should().Be("SKU1");
        det.Prod.NCM.Should().Be("12345678");
        det.Prod.QCom.Should().Be(2);
        det.Imposto.ICMS!.ICMS00.Should().NotBeNull();
        det.Imposto.ICMS.ICMS00!.VBC.Should().Be(100);
        det.Imposto.ICMS.ICMS00.PICMS.Should().Be(18);
        det.Imposto.ICMS.ICMS00.VICMS.Should().Be(18);

        nfe.Total.ICMSTot.VNF.Should().Be(100);
        nfe.Total.ICMSTot.VBC.Should().Be(100);
        nfe.Total.ICMSTot.VICMS.Should().Be(18);
        nfe.Pag!.DetPag.Single().TPag.Should().Be(MeioPagamento.Dinheiro);
    }

    [Fact]
    public void NFe_gera_xml_com_chave_de_44_digitos_calculada_pela_unimake()
    {
        var envi = MapperEnviNFe.Criar(
            Documento(TipoDocumento.NFE), TenantCompleto(), RequestValida(), Ambiente.Homologacao);

        var xml = envi.GerarXML().OuterXml;

        xml.Should().Contain("<enviNFe");
        xml.Should().Contain("<natOp>VENDA DE MERCADORIA</natOp>");
        xml.Should().Contain("<emit>");
        xml.Should().Contain("<CNPJ>12345678000199</CNPJ>");
        xml.Should().Contain("<det nItem=\"1\"");
        xml.Should().Contain("<CST>00</CST>");

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var id = doc.SelectSingleNode("//*[local-name()='infNFe']")!.Attributes!["Id"]!.Value;
        id.Should().StartWith("NFe").And.HaveLength(47); // "NFe" + 44 dígitos
    }

    [Fact]
    public void NFce_é_sincrona_e_exige_pagamento()
    {
        var doc = Documento(TipoDocumento.NFCE);
        var req = RequestValida() with { Destinatario = null, Pagamento = null };

        var act = () => MapperEnviNFe.Criar(doc, TenantCompleto(), req, Ambiente.Homologacao);

        act.Should().Throw<ErroNaoRecuperavelException>()
            .WithMessage("*NFC-e exige ao menos uma forma de pagamento*");
    }

    [Fact]
    public void NFce_aceita_destinatario_ausente_consumidor_nao_identificado()
    {
        var doc = Documento(TipoDocumento.NFCE);
        var req = RequestValida() with { Destinatario = null };

        var envi = MapperEnviNFe.Criar(doc, TenantCompleto(), req, Ambiente.Homologacao);

        envi.IndSinc.Should().Be(SimNao.Sim); // NFC-e é síncrona
        envi.NFe[0].InfNFeField.Dest.Should().BeNull();
        envi.NFe[0].InfNFeField.Ide.Mod.Should().Be(ModeloDFe.NFCe);
        envi.NFe[0].InfNFeField.Ide.TpImp.Should().Be(FormatoImpressaoDANFE.NFCe);
    }

    [Fact]
    public void NFe_sem_destinatario_falha()
    {
        var req = RequestValida() with { Destinatario = null };

        var act = () => MapperEnviNFe.Criar(
            Documento(TipoDocumento.NFE), TenantCompleto(), req, Ambiente.Homologacao);

        act.Should().Throw<ErroNaoRecuperavelException>()
            .WithMessage("*exige 'destinatario'*");
    }

    [Fact]
    public void Perfil_do_emitente_incompleto_falha()
    {
        var tenant = TenantCompleto();
        tenant.InscricaoEstadual = null;

        var act = () => MapperEnviNFe.Criar(
            Documento(TipoDocumento.NFE), tenant, RequestValida(), Ambiente.Homologacao);

        act.Should().Throw<ErroNaoRecuperavelException>()
            .WithMessage("*Perfil fiscal do emitente incompleto*");
    }

    [Fact]
    public void Cst_nao_suportado_falha_com_mensagem_clara()
    {
        var req = RequestValida();
        req.Itens[0].Impostos![0] = new ImpostoDto("10", 100, 18, 18);

        var act = () => MapperEnviNFe.Criar(
            Documento(TipoDocumento.NFE), TenantCompleto(), req, Ambiente.Homologacao);

        act.Should().Throw<ErroNaoRecuperavelException>()
            .WithMessage("*CST '10' não suportado*");
    }
}
