using Fiscal.Adapters.Unimake;
using FluentAssertions;
using Xunit;

namespace Fiscal.Core.Tests;

public class ExtratorDfeTests
{
    [Fact]
    public void Extrai_chave_de_resNFe_atributo_ChNFe()
    {
        const string xml = @"<resNFe xmlns=""http://www.portalfiscal.inf.br/nfe"" versao=""1.01"">
            <infNFe ChNFe=""41260912345678000199550010000001001000123456"" tpNF=""1"" vNF=""100.00"">
              <CNPJ>98765432000155</CNPJ><xNome>Fornecedor SA</xNome>
            </infNFe></resNFe>";

        ExtratorDfe.ChaveDoXml(xml).Should().Be("41260912345678000199550010000001001000123456");
        ExtratorDfe.ValorDoElemento(xml, "xNome").Should().Be("Fornecedor SA");
        ExtratorDfe.ValorDoElemento(xml, "vNF").Should().Be("100.00");
    }

    [Fact]
    public void Extrai_chave_de_procNFe_atributo_Id()
    {
        const string xml = @"<nfeProc xmlns=""http://www.portalfiscal.inf.br/nfe"" versao=""4.00"">
            <NFe><infNFe Id=""NFe41260912345678000199550010000001001000123456"" versao=""4.00""/></NFe></nfeProc>";

        ExtratorDfe.ChaveDoXml(xml).Should().Be("41260912345678000199550010000001001000123456");
    }

    [Fact]
    public void Xml_invalido_retorna_null_sem_estourar()
    {
        ExtratorDfe.ChaveDoXml("isto não é xml").Should().BeNull();
        ExtratorDfe.ValorDoElemento("isto não é xml", "vNF").Should().BeNull();
    }
}
