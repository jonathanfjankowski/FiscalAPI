using Fiscal.Adapters.Unimake;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using FluentAssertions;
using Xunit;

namespace Fiscal.Core.Tests;

/// <summary>
/// infCpl — informações complementares de interesse do fisco/contribuinte.
/// O ERP manda o texto no payload de emissão (observações + valor aproximado
/// dos tributos, Lei 12.741/2012); a API leva para o grupo infAdic/infCpl do
/// XML da NF-e/NFC-e.
/// </summary>
public class InformacoesComplementaresTests
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

    private static EmissaoRequest Request(string? infCpl) => new(
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
        NaturezaOperacao: "VENDA DE MERCADORIA",
        InformacoesComplementares: infCpl);

    [Fact]
    public void NFe_com_infCpl_mapeia_para_o_grupo_infAdic()
    {
        var envi = MapperEnviNFe.Criar(
            Documento(TipoDocumento.NFE), TenantCompleto(),
            Request("Pedido 123 — entrega em 15 dias."), Ambiente.Homologacao);

        var infCpl = envi.NFe[0].InfNFeField.InfAdic?.InfCpl;
        infCpl.Should().NotBeNull("o infCpl informado tem de ir para o XML");
        infCpl.Should().Contain("Pedido 123");
    }

    [Fact]
    public void NFce_com_infCpl_mapeia_para_o_grupo_infAdic()
    {
        var envi = MapperEnviNFe.Criar(
            Documento(TipoDocumento.NFCE), TenantCompleto(),
            Request("Pedido 123"), Ambiente.Homologacao);

        envi.NFe[0].InfNFeField.InfAdic?.InfCpl.Should().Contain("Pedido 123");
    }

    [Fact]
    public void Sem_infCpl_o_grupo_nao_e_criado()
    {
        var envi = MapperEnviNFe.Criar(
            Documento(TipoDocumento.NFE), TenantCompleto(), Request(null), Ambiente.Homologacao);

        envi.NFe[0].InfNFeField.InfAdic.Should().BeNull();
    }

    [Fact]
    public void InfCpl_em_branco_nao_cria_o_grupo()
    {
        var envi = MapperEnviNFe.Criar(
            Documento(TipoDocumento.NFE), TenantCompleto(), Request("   "), Ambiente.Homologacao);

        envi.NFe[0].InfNFeField.InfAdic.Should().BeNull();
    }
}
