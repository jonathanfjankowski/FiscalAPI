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
    public async Task Danfse_com_payload_dps_gera_pdf_valido()
    {
        var doc = Documento(Fiscal.Core.ModelosDocumento.NFSeNacional, "AUTORIZADA");
        // Identificador NFS-e Nacional (inf.Id, 50 posições) — não é chave de 44.
        doc.ChaveAcesso = "DPS" + new string('9', 47);
        doc.PayloadEntrada = """
            {
              "ambiente": "homologacao",
              "serie": 1,
              "dataCompetencia": "2026-09-01",
              "tomador": { "cnpjCpf": "12345678000199", "nome": "Tomador Teste",
                "endereco": { "codigoMunicipioIbge": "4106902", "cep": "80000000",
                  "logradouro": "Rua Y", "numero": "20", "bairro": "Centro" } },
              "servico": { "codigoTributarioNacional": "010701",
                "codigoTributarioMunicipal": "1273", "descricaoServico": "Desenvolvimento de software",
                "codigoNbs": "112011000" },
              "valores": { "valorServicos": 1500.00, "tributacaoIssqn": 1, "retencaoIssqn": 1,
                "aliquotaIssqn": 5,
                "tributacaoFederal": { "cstPisCofins": "01", "valorPis": 7.5, "valorCofins": 34.5,
                  "valorRetidoIrrf": 0, "valorRetidoCsll": 0, "valorRetidoCpp": 0 },
                "totalTributos": { "federal": 42.0, "estadual": 0, "municipal": 75.0 } },
              "informacoesComplementares": "Serviço prestado conforme contrato 123."
            }
            """;

        var bytes = await new GeradorPdfQuestPdf().GerarDanfseAsync(doc, Tenant(), CancellationToken.None);

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Danfse_com_payload_de_substituicao_gera_pdf_valido()
    {
        var doc = Documento(Fiscal.Core.ModelosDocumento.NFSeNacional, "AUTORIZADA");
        doc.PayloadEntrada = """
            {
              "cMotivo": 1,
              "xMotivo": "Erro na emissão",
              "chaveSubstituida": "DPS99999999999999999999999999999999999999999999999999",
              "dps": {
                "ambiente": "homologacao",
                "serie": 1,
                "servico": { "codigoTributarioNacional": "010701", "descricaoServico": "Correção de software" },
                "valores": { "valorServicos": 100.00, "tributacaoIssqn": 1, "retencaoIssqn": 1 }
              }
            }
            """;

        var bytes = await new GeradorPdfQuestPdf().GerarDanfseAsync(doc, Tenant(), CancellationToken.None);

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Danfse_com_payload_legado_ainda_gera_pdf()
    {
        // Documentos antigos de sandbox gravavam EmissaoRequest no PayloadEntrada;
        // o DANFSe deve continuar gerando (só sem as seções de serviço/tomador).
        var doc = Documento(Fiscal.Core.ModelosDocumento.NFSeNacional, "AUTORIZADA");

        var bytes = await new GeradorPdfQuestPdf().GerarDanfseAsync(doc, Tenant(), CancellationToken.None);

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Danfce_com_chave_e_qrcode_no_xml_gera_pdf_valido()
    {
        var doc = Documento(65, "AUTORIZADA");
        doc.XmlAssinado = """
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <NFe><infNFe Id="NFe123"><infNFeSupl>
                <qrCode><![CDATA[https://www.sefaz.pr.gov.br/NFCeConsulta?p=4126]]></qrCode>
              </infNFeSupl></infNFe></NFe>
            </nfeProc>
            """;

        var bytes = await new GeradorPdfQuestPdf().GerarDanfceAsync(doc, Tenant(), CancellationToken.None);

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().StartWith("%PDF-");
    }
}
