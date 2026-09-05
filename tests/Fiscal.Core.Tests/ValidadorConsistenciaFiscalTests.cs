using Fiscal.Core.Services;
using FluentAssertions;
using Xunit;

namespace Fiscal.Core.Tests;

public class ValidadorConsistenciaFiscalTests
{
    private readonly ValidadorConsistenciaFiscal _validador = new();

    [Fact]
    public void Documento_valido_nao_retorna_inconsistencias()
    {
        var doc = new DocumentoParaValidar(
            ValorTotal: 100.00m,
            Itens: new[]
            {
                new ItemFiscal("SKU1", 2m, 50.00m, 100.00m)
            },
            Impostos: Array.Empty<ImpostoFiscal>());

        var erros = _validador.Validar(doc);

        erros.Should().BeEmpty();
    }

    [Fact]
    public void Soma_dos_itens_diferente_do_total_e_inconsistente()
    {
        var doc = new DocumentoParaValidar(
            ValorTotal: 100.00m,
            Itens: new[]
            {
                new ItemFiscal("SKU1", 2m, 30.00m, 60.00m),
                new ItemFiscal("SKU2", 1m, 30.00m, 30.00m)
            },
            Impostos: Array.Empty<ImpostoFiscal>());

        var erros = _validador.Validar(doc);

        erros.Should().ContainSingle()
            .Which.Campo.Should().Be("valorTotal");
    }

    [Fact]
    public void Quantidade_x_valorUnitario_diferente_do_valorTotal_do_item_e_inconsistente()
    {
        var doc = new DocumentoParaValidar(
            ValorTotal: 100.00m,
            Itens: new[]
            {
                new ItemFiscal("SKU1", 2m, 50.00m, 90.00m) // 2*50 = 100, veio 90
            },
            Impostos: Array.Empty<ImpostoFiscal>());

        var erros = _validador.Validar(doc);

        erros.Should().Contain(e => e.Campo == "itens[0].valorTotal");
    }

    [Fact]
    public void Base_x_aliquota_diferente_do_valor_do_imposto_e_inconsistente()
    {
        var doc = new DocumentoParaValidar(
            ValorTotal: 100.00m,
            Itens: new[] { new ItemFiscal("SKU1", 1m, 100.00m, 100.00m) },
            Impostos: new[]
            {
                new ImpostoFiscal("01", BaseCalculo: 100m, Aliquota: 1.65m, Valor: 1.60m) // esperado 1.65
            });

        var erros = _validador.Validar(doc);

        erros.Should().Contain(e => e.Campo == "impostos[0].valor");
    }

    [Fact]
    public void Base_x_aliquota_correto_nao_gera_erro()
    {
        var doc = new DocumentoParaValidar(
            ValorTotal: 100.00m,
            Itens: new[] { new ItemFiscal("SKU1", 1m, 100.00m, 100.00m) },
            Impostos: new[]
            {
                new ImpostoFiscal("01", BaseCalculo: 100m, Aliquota: 1.65m, Valor: 1.65m)
            });

        var erros = _validador.Validar(doc);

        erros.Should().BeEmpty();
    }

    [Fact]
    public void Cst_isento_com_valor_zero_nao_gera_erro()
    {
        var doc = new DocumentoParaValidar(
            ValorTotal: 100.00m,
            Itens: new[] { new ItemFiscal("SKU1", 1m, 100.00m, 100.00m) },
            Impostos: new[]
            {
                new ImpostoFiscal("06", BaseCalculo: 0m, Aliquota: 0m, Valor: 0m)
            });

        var erros = _validador.Validar(doc);

        erros.Should().BeEmpty();
    }

    [Fact]
    public void Cst_isento_com_valor_positivo_gera_erro()
    {
        var doc = new DocumentoParaValidar(
            ValorTotal: 100.00m,
            Itens: new[] { new ItemFiscal("SKU1", 1m, 100.00m, 100.00m) },
            Impostos: new[]
            {
                new ImpostoFiscal("06", BaseCalculo: 0m, Aliquota: 0m, Valor: 5m)
            });

        var erros = _validador.Validar(doc);

        erros.Should().Contain(e => e.Campo == "impostos[0].valor");
    }

    [Fact]
    public void Tolerancia_de_um_centavo_e_aceita()
    {
        // 100 * 1.65% = 1.65, mas se vier 1.64 ou 1.66 (arredondamento), passa.
        var doc1 = new DocumentoParaValidar(100m,
            new[] { new ItemFiscal("X", 1m, 100m, 100m) },
            new[] { new ImpostoFiscal("01", 100m, 1.65m, 1.64m) });
        var doc2 = new DocumentoParaValidar(100m,
            new[] { new ItemFiscal("X", 1m, 100m, 100m) },
            new[] { new ImpostoFiscal("01", 100m, 1.65m, 1.66m) });

        _validador.Validar(doc1).Should().BeEmpty();
        _validador.Validar(doc2).Should().BeEmpty();
    }
}
