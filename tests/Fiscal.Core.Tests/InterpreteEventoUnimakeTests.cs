using Fiscal.Adapters.Unimake;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using FluentAssertions;
using Xunit;

namespace Fiscal.Core.Tests;

public class InterpreteEventoUnimakeTests
{
    [Theory]
    [InlineData(135, true)]  // evento homologado
    [InlineData(136, true)]  // evento vinculado a lote anterior
    [InlineData(155, true)]  // cancelamento homologado fora do prazo (judicial)
    [InlineData(573, false)] // duplicidade de evento
    [InlineData(243, false)] // CPF/CNPJ receptor divergente
    [InlineData(492, false)] // justificativa inválida
    public void InterpretarEvento_mapeia_cStat(int cStat, bool processado)
    {
        var r = InterpreteEventoUnimake.InterpretarEvento(cStat, "NPROT1", "motivo", "<xml/>");

        r.Status.Should().Be(processado ? ResultadoEventoStatus.Processado : ResultadoEventoStatus.Rejeitado);
        r.Protocolo.Should().Be("NPROT1");
        r.XmlRetorno.Should().Be("<xml/>");
        if (processado) r.Motivo.Should().BeNull();
        else r.Motivo.Should().Contain($"cStat {cStat}");
    }

    [Theory]
    [InlineData(102, true)]  // inutilização homologada
    [InlineData(203, false)] // rejeição: elemento não encontrado
    [InlineData(411, false)] // numeroFinal < numeroInicial
    public void InterpretarInutilizacao_mapeia_cStat(int cStat, bool processado)
    {
        var r = InterpreteEventoUnimake.InterpretarInutilizacao(cStat, "NPROT2", "motivo", "<xml/>");

        r.Status.Should().Be(processado ? ResultadoEventoStatus.Processado : ResultadoEventoStatus.Rejeitado);
        r.Protocolo.Should().Be("NPROT2");
    }

    [Fact]
    public async Task TransmissorMock_devolve_processado_com_protocolo_e_xml()
    {
        var transmissor = new TransmissorEventoMock();
        var evento = new EventoFiscal
        {
            Id = Guid.NewGuid(),
            TipoEvento = "CANCELAMENTO",
            Justificativa = "Teste de cancelamento via mock",
        };

        var r = await transmissor.TransmitirAsync(
            evento, new DocumentoFiscal { Modelo = 55 }, new Tenant(), null!, Ambiente.Homologacao, default);

        r.Status.Should().Be(ResultadoEventoStatus.Processado);
        r.Protocolo.Should().NotBeNullOrEmpty();
        r.XmlRetorno.Should().Contain("cStat").And.Contain("135");
        r.Motivo.Should().BeNull();
    }

    [Fact]
    public async Task TransmissorMock_inutilizacao_retorna_cStat_102()
    {
        var transmissor = new TransmissorEventoMock();
        var evento = new EventoFiscal { Id = Guid.NewGuid(), TipoEvento = "INUTILIZACAO", Justificativa = "faixa" };

        var r = await transmissor.TransmitirAsync(
            evento, null, new Tenant(), null!, Ambiente.Homologacao, default);

        r.Status.Should().Be(ResultadoEventoStatus.Processado);
        r.XmlRetorno.Should().Contain("102");
    }
}
