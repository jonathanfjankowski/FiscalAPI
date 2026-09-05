using System.Security.Cryptography;
using System.Text;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Services;
using FluentAssertions;
using Xunit;

namespace Fiscal.Core.Tests;

public class WebhookAssinaturaTests
{
    [Fact]
    public void Assinatura_e_hex_minusculo_com_64_caracteres()
    {
        var sig = AssinadorWebhook.CalcularAssinatura("segredo", 1700000000, "{\"a\":1}");

        sig.Should().HaveLength(64);
        sig.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Assinatura_cobre_timestamp_e_ponto_e_payload()
    {
        // Formato do contrato: HMAC-SHA256(secret, "{timestamp}.{payload}").
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("segredo"));
        var esperado = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes("1700000000.{\"a\":1}"))).ToLowerInvariant();

        var sig = AssinadorWebhook.CalcularAssinatura("segredo", 1700000000, "{\"a\":1}");

        sig.Should().Be(esperado);
    }

    [Fact]
    public void Payload_contem_tipo_timestamp_e_documento()
    {
        var doc = new DocumentoFiscal
        {
            Id = Guid.NewGuid(),
            Tipo = TipoDocumento.NFE,
            Status = StatusDocumento.AUTORIZADA,
            Ambiente = (short)Ambiente.Homologacao,
            Serie = 1,
            Numero = 5,
            ChaveAcesso = new string('1', 44),
            ProtocoloAutorizacao = "135000000000000",
            CriadoEm = DateTimeOffset.UnixEpoch,
            AtualizadoEm = DateTimeOffset.UnixEpoch,
        };

        var json = Webhooks.PayloadPara(doc, Webhooks.EventoAutorizado, DateTimeOffset.FromUnixTimeSeconds(1700000000));

        json.Should().Contain("\"tipo\":\"documento.autorizado\"");
        json.Should().Contain("\"timestamp\":1700000000");
        json.Should().Contain("\"documento\":{");
        json.Should().Contain("\"chaveAcesso\":\"11111111111111111111111111111111111111111111\"");
        json.Should().Contain("\"status\":\"AUTORIZADA\"");
        json.Should().NotContain("segredo"); // segredo nunca vai no payload
    }
}
