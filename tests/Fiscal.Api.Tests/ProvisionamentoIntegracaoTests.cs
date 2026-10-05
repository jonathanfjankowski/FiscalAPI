using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Fiscal.Api.Tests;

/// <summary>
/// Provisionamento automático pelo ERP (POST /v1/empresas): auth por
/// X-Bootstrap-Key, criação idempotente por CNPJ, rotação de API key por
/// ambiente e token de webhook novo a cada chamada.
/// </summary>
[Collection("fiscal-db")]
public class ProvisionamentoIntegracaoTests : IClassFixture<ProvisionamentoIntegracaoTests.Factory>
{
    private const string BootstrapToken = "bootstrap-token-de-teste-com-32-chars";

    private readonly Factory _factory;

    public ProvisionamentoIntegracaoTests(Factory factory) => _factory = factory;

    public class Factory : EmissaoIntegracaoTests.Factory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // DB separado das outras fábricas (mesma regra da suíte admin).
                    ["ConnectionStrings:Postgres"] = "Data Source=file:provisionamento_mem?mode=memory&cache=shared",
                    ["Fiscal:BootstrapToken"] = BootstrapToken
                });
            });
        }
    }

    private HttpClient Cliente(string? token = BootstrapToken)
    {
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Add("X-Bootstrap-Key", token);
        }

        return client;
    }

    private object Pedido(string cnpj = "12345678000199", string ambiente = "homologacao") => new
    {
        cnpj,
        razaoSocial = "Empresa Provisionada LTDA",
        nomeFantasia = "Provisionada",
        inscricaoEstadual = "123456789012",
        uf = "PR",
        ambiente,
        webhookUrl = $"https://erp.test/api/webhooks/fiscal/1"
    };

    [Fact]
    public async Task Sem_bootstrap_key_rejeita()
    {
        var resposta = await Cliente(null).PostAsJsonAsync("/v1/empresas", Pedido());

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bootstrap_key_errada_rejeita()
    {
        var resposta = await Cliente("token-errado").PostAsJsonAsync("/v1/empresas", Pedido());

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Provisiona_tenant_e_emite_chave_do_ambiente()
    {
        var resposta = await Cliente().PostAsJsonAsync("/v1/empresas", Pedido());

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("id").GetString().Should().NotBeEmpty();
        var chave = corpo.GetProperty("apiKey").GetString()!;
        chave.Should().StartWith("fk_test_");
        chave.Length.Should().BeGreaterThan(16);
        corpo.GetProperty("webhookToken").GetString()!.Length.Should().BeGreaterThan(30);
    }

    [Fact]
    public async Task Reprovisionamento_e_idempotente_por_cnpj_e_rotaciona_chave()
    {
        var primeira = await (await Cliente().PostAsJsonAsync("/v1/empresas", Pedido())).Content
            .ReadFromJsonAsync<JsonElement>();
        var segunda = await (await Cliente().PostAsJsonAsync("/v1/empresas", Pedido())).Content
            .ReadFromJsonAsync<JsonElement>();

        segunda.GetProperty("id").GetString().Should().Be(primeira.GetProperty("id").GetString(),
            "mesmo CNPJ → mesmo tenant");
        segunda.GetProperty("apiKey").GetString().Should().NotBe(primeira.GetProperty("apiKey").GetString(),
            "cada provisionamento rotaciona a chave");
        segunda.GetProperty("webhookToken").GetString().Should().NotBe(primeira.GetProperty("webhookToken").GetString());
    }

    [Fact]
    public async Task Ambiente_producao_emite_chave_fk_live()
    {
        var resposta = await Cliente().PostAsJsonAsync("/v1/empresas", Pedido(ambiente: "producao"));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("apiKey").GetString()!.Should().StartWith("fk_live_");
    }

    [Fact]
    public async Task Ambiente_invalido_rejeita()
    {
        var resposta = await Cliente().PostAsJsonAsync("/v1/empresas", Pedido(ambiente: "staging"));

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
