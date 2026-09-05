using Fiscal.Adapters.Unimake;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using FluentAssertions;
using Unimake.Business.DFe.Servicos;
using Xunit;

namespace Fiscal.Core.Tests;

public class ContingenciaTests
{
    private static Tenant Tenant() => new()
    {
        Cnpj = "12345678000199",
        RazaoSocial = "Empresa Teste LTDA",
        Uf = "PR",
        CodigoMunicipioIbge = "4106902",
        RegimeTributario = 3,
        InscricaoEstadual = "12345678-01",
        Logradouro = "Rua A", Numero = "100", Bairro = "Centro",
        Cep = "80000000", NomeMunicipio = "Curitiba",
    };

    private static DocumentoFiscal Documento(string? modoContingencia = null) => new()
    {
        Id = Guid.NewGuid(),
        Tipo = TipoDocumento.NFE,
        Ambiente = (short)Ambiente.Homologacao,
        Modelo = 55,
        Serie = 1,
        Numero = 42,
        ModoContingencia = modoContingencia,
        CriadoEm = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(-3)),
        PayloadEntrada = "{}",
    };

    private static EmissaoRequest Request() => new(
        Ambiente: "homologacao",
        Serie: 1,
        Destinatario: new DestinatarioDto("12345678000199", "Dest Teste", null, null),
        Itens: [new ItemDto("SKU1", "Produto", "12345678", "5102", 1, 10, 10,
            [new ImpostoDto("00", 10m, 18m, 1.8m)])],
        Totais: new TotaisDto(10, 10),
        Pagamento: [new PagamentoDto("01", 10)],
        NaturezaOperacao: "VENDA");

    [Theory]
    [InlineData("SVCAN", TipoEmissao.ContingenciaSVCAN)]
    [InlineData("SVCRS", TipoEmissao.ContingenciaSVCRS)]
    [InlineData(null, TipoEmissao.Normal)]
    public void Mapper_usa_tpEmis_do_modo_contingencia(string? modo, TipoEmissao esperado)
    {
        var envi = MapperEnviNFe.Criar(Documento(modo), Tenant(), Request(), Ambiente.Homologacao);

        envi.NFe[0].InfNFeField.Ide.TpEmis.Should().Be(esperado);
    }

    [Fact]
    public void Mapper_usa_dhEmi_do_CriadoEm_e_chave_e_deterministica_entre_tentativas()
    {
        var doc = Documento(null);

        var envi1 = MapperEnviNFe.Criar(doc, Tenant(), Request(), Ambiente.Homologacao);
        var envi2 = MapperEnviNFe.Criar(doc, Tenant(), Request(), Ambiente.Homologacao);

        envi1.NFe[0].InfNFeField.Ide.DhEmi.Should().Be(doc.CriadoEm);
        envi1.NFe[0].InfNFeField.Chave.Should().Be(envi2.NFe[0].InfNFeField.Chave);
        envi1.NFe[0].InfNFeField.Chave.Should().HaveLength(44);
    }

    [Fact]
    public void Chave_muda_entre_normal_e_svc_pelo_tpEmis()
    {
        var normal = MapperEnviNFe.Criar(Documento(null), Tenant(), Request(), Ambiente.Homologacao);
        var svc = MapperEnviNFe.Criar(Documento("SVCAN"), Tenant(), Request(), Ambiente.Homologacao);

        normal.NFe[0].InfNFeField.Chave.Should().NotBe(svc.NFe[0].InfNFeField.Chave);
    }
}
