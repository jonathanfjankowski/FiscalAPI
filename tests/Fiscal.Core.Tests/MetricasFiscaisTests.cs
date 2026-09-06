using System.Diagnostics.Metrics;
using Fiscal.Core.Enums;
using Fiscal.Core.Services;
using FluentAssertions;
using Xunit;

namespace Fiscal.Core.Tests;

/// <summary>Instrumentos de negócio (meter FiscalAPI) criam e gravam sem erro.</summary>
public class MetricasFiscaisTests
{
    [Fact]
    public void Instrumentos_gravam_sem_erro_e_expoem_nomes()
    {
        using var factory = new MeterFactoryIsolado();
        var metricas = new MetricasFiscais(factory);

        var gravar = () =>
        {
            metricas.DocumentoProcessado(TipoDocumento.NFE, StatusDocumento.AUTORIZADA, "PR", false);
            metricas.LatenciaAutorizacao(12.5, TipoDocumento.NFE, "PR");
            metricas.WebhookEntregue("documento.autorizado", true);
            metricas.ContingenciaAcionada("SVCAN");
            metricas.EventoProcessado("110111", "PROCESSADO");
        };
        gravar.Should().NotThrow();
        factory.MeterName.Should().Be(MetricasFiscais.NomeMedidor);
    }

    private sealed class MeterFactoryIsolado : IMeterFactory
    {
        public string MeterName { get; private set; } = string.Empty;

        public Meter Create(MeterOptions options)
        {
            MeterName = options.Name;
            return new Meter(options);
        }

        public void Dispose() { }
    }
}
