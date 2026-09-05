using System.Text;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Pdf;
using FluentAssertions;
using Xunit;

namespace Fiscal.Core.Tests;

public class GeradorPdfQuestPdfTests
{
    private static DocumentoFiscal Documento(short modelo, string status) => new()
    {
        Id = Guid.NewGuid(),
        Tipo = modelo == 65 ? TipoDocumento.NFCE : TipoDocumento.NFE,
        Ambiente = (short)Ambiente.Homologacao,
        Modelo = modelo,
        Serie = 1,
        Numero = 42,
        Status = System.Enum.Parse<StatusDocumento>(status),
        ChaveAcesso = new string('1', 44),
        ProtocoloAutorizacao = "135000000000000",
        PayloadEntrada = """
            {
              "ambiente": "homologacao",
              "serie": 1,
              "destinatario": { "cnpjCpf": "12345678000199", "nome": "Dest Teste",
                "endereco": { "logradouro": "Rua X", "numero": "10", "bairro": "Centro",
                  "codigoMunicipioIbge": "4106902", "nomeMunicipio": "Curitiba", "uf": "PR" } },
              "itens": [ { "codigo": "SKU1", "descricao": "Produto Teste", "ncm": "12345678",
                "cfop": "5102", "quantidade": 2, "valorUnitario": 50, "valorTotal": 100,
                "impostos": [ { "cst": "00", "baseCalculo": 100, "aliquota": 18, "valor": 18 } ] } ],
              "totais": { "valorProdutos": 100, "valorNota": 100 },
              "pagamento": [ { "forma": "01", "valor": 100 } ],
              "naturezaOperacao": "VENDA DE MERCADORIA"
            }
            """,
    };

    private static Tenant Tenant() => new()
    {
        Cnpj = "12345678000199",
        RazaoSocial = "Empresa Teste LTDA",
        Uf = "PR",
        InscricaoEstadual = "12345678-01",
        Logradouro = "Rua A",
        Numero = "100",
        Bairro = "Centro",
        Cep = "80000000",
        NomeMunicipio = "Curitiba",
    };

    [Fact]
    public async Task Danfe_gera_pdf_valido()
    {
        var bytes = await new GeradorPdfQuestPdf().GerarDanfeAsync(
            Documento(55, "AUTORIZADA"), Tenant(), CancellationToken.None);

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Danfce_gera_pdf_valido()
    {
        var bytes = await new GeradorPdfQuestPdf().GerarDanfceAsync(
            Documento(65, "AUTORIZADA"), Tenant(), CancellationToken.None);

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Danfse_gera_pdf_simplificado()
    {
        var bytes = await new GeradorPdfQuestPdf().GerarDanfseAsync(
            Documento(Fiscal.Core.ModelosDocumento.NFSeNacional, "AUTORIZADA"), Tenant(), CancellationToken.None);

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }
}
