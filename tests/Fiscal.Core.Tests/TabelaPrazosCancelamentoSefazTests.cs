using Fiscal.Core.Services;
using Xunit;

namespace Fiscal.Core.Tests;

public class TabelaPrazosCancelamentoSefazTests
{
    [Fact]
    public void ContemTodasAs27UfsBrasileiras()
    {
        Assert.Equal(27, TabelaPrazosCancelamentoSefaz.Ufs.Length);
        Assert.True(TabelaPrazosCancelamentoSefaz.UfValida("SP"));
        Assert.True(TabelaPrazosCancelamentoSefaz.UfValida("mt"));
        Assert.False(TabelaPrazosCancelamentoSefaz.UfValida("XX"));
    }

    [Fact]
    public void RetornaPadroesEExcecoesOficiaisPorUfEModelo()
    {
        Assert.Equal(1440, TabelaPrazosCancelamentoSefaz.PadraoPara("SP", 55).PrazoMinutos);
        Assert.Equal(30, TabelaPrazosCancelamentoSefaz.PadraoPara("SP", 65).PrazoMinutos);
        Assert.Equal(120, TabelaPrazosCancelamentoSefaz.PadraoPara("MT", 55).PrazoMinutos);
        Assert.Equal(10080, TabelaPrazosCancelamentoSefaz.PadraoPara("RS", 55).PrazoMinutos);
        Assert.Equal(1440, TabelaPrazosCancelamentoSefaz.PadraoPara("PR", 65).PrazoMinutos);
    }

    [Theory]
    [InlineData(30, "30 minutos")]
    [InlineData(60, "1 hora")]
    [InlineData(120, "2 horas")]
    [InlineData(1440, "24 horas")]
    [InlineData(10080, "168 horas (7 dias)")]
    [InlineData(90, "1h30min")]
    public void FormataPrazoDeFormaLegivel(int minutos, string esperado)
    {
        Assert.Equal(esperado, TabelaPrazosCancelamentoSefaz.FormatarPrazo(minutos));
    }
}
