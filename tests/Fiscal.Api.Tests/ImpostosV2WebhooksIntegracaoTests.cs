using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Fiscal.Worker.Jobs;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fiscal.Api.Tests;

/// <summary>
/// Contrato v2 (impostosV2 — CSOSN/Simples Nacional) e self-service de
/// webhooks com secret cifrado em repouso (docs/plano-evolucao-contrato-v2.md,
/// docs/revisao-seguranca.md §Pendências 2).
/// </summary>
[Collection("fiscal-db")]
public class ImpostosV2WebhooksIntegracaoTests(EmissaoIntegracaoTests.Factory factory)
    : IClassFixture<EmissaoIntegracaoTests.Factory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", factory.BootstrapKey);
        return client;
    }

    private static object ItemComIcmsV2(object icms, object? legado = null) => new
    {
        codigo = "SKU1",
        descricao = "Produto Teste",
        ncm = "12345678",
        cfop = "5102",
        quantidade = 2,
        valorUnitario = 50,
        valorTotal = 100,
        impostos = legado,
        impostosV2 = new { icms },
    };

    private HttpRequestMessage EmissaoRequest(object item, string idem)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                itens = new[] { item },
                totais = new { valorProdutos = 100, valorNota = 100 }
            })
        };
        req.Headers.Add("Idempotency-Key", idem);
        return req;
    }

    // ---------- impostosV2 (contrato v2 — F1: ICMS completo + CSOSN) ----------

    [Fact]
    public async Task Emissao_com_csosn_102_simples_nacional_aceita_202()
    {
        var client = Client();
        var item = ItemComIcmsV2(new { origem = 0, csosn = "102" });

        var resp = await client.SendAsync(EmissaoRequest(item, $"v2-{Guid.NewGuid()}"));

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("PENDENTE");
    }

    [Fact]
    public async Task Emissao_com_impostos_e_impostosV2_no_mesmo_item_retorna_400()
    {
        var client = Client();
        var legado = new[] { new { cst = "00", baseCalculo = 100m, aliquota = 18m, valor = 18m } };
        var item = ItemComIcmsV2(new { origem = 0, csosn = "102" }, legado);

        var resp = await client.SendAsync(EmissaoRequest(item, $"amb-{Guid.NewGuid()}"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("nunca os dois");
    }

    [Fact]
    public async Task Emissao_com_csosn_300_e_valor_proprio_retorna_422()
    {
        var client = Client();
        var item = ItemComIcmsV2(new { origem = 0, csosn = "300", valor = 10 });

        var resp = await client.SendAsync(EmissaoRequest(item, $"iso-{Guid.NewGuid()}"));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("grupos de imposto v2");
        body.Should().Contain("indica isenção");
    }

    [Fact]
    public async Task Emissao_com_cst_20_reducao_e_st_e_difal_aceita_202()
    {
        var client = Client();
        var item = ItemComIcmsV2(new
        {
            origem = 0,
            cst = "00",
            baseCalculo = 100,
            aliquota = 12,
            valor = 12,
            difal = new
            {
                aliquotaInterestadual = 12,
                baseDestino = 100,
                aliquotaDestino = 18,
                valorIcmsDestino = 18,
                valorIcmsOrigem = 0,
            },
        });

        var resp = await client.SendAsync(EmissaoRequest(item, $"dif-{Guid.NewGuid()}"));

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ---------- webhooks self-service + secret cifrado ----------

    [Fact]
    public async Task Webhooks_put_e_get_self_service_cifrando_o_segredo()
    {
        var client = Client();
        var segredo = "segredo-self-service-123";

        var put = await client.PutAsJsonAsync("/v1/tenants/webhooks", new
        {
            webhookUrl = "https://integrador.example.com/hook",
            webhookSecret = segredo,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var putBody = await put.Content.ReadFromJsonAsync<JsonElement>();
        putBody.GetProperty("webhookSecretCadastrado").GetBoolean().Should().BeTrue();

        var get = await client.GetAsync("/v1/tenants/webhooks");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        getBody.GetProperty("webhookUrl").GetString().Should().Be("https://integrador.example.com/hook");
        getBody.GetProperty("webhookSecretCadastrado").GetBoolean().Should().BeTrue();
        getBody.ToString().Should().NotContain(segredo);

        // Em repouso: coluna legada vazia, envelope presente (stub = UTF-8).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var prefixo = factory.BootstrapKey[..12];
        var tenantId = (await db.ApiKeys.SingleAsync(k => k.Prefixo == prefixo)).TenantId;
        var tenant = await db.Tenants.FindAsync(tenantId);
        tenant!.WebhookSecret.Should().BeNull();
        tenant.WebhookSecretCriptografado.Should().NotBeNull();
        Encoding.UTF8.GetString(tenant.WebhookSecretCriptografado!).Should().Be(segredo);
    }

    [Fact]
    public async Task Webhooks_put_url_invalida_retorna_422()
    {
        var client = Client();

        var put = await client.PutAsJsonAsync("/v1/tenants/webhooks", new
        {
            webhookUrl = "nao-e-uma-url",
        });

        put.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Webhooks_put_secret_curto_retorna_422()
    {
        var client = Client();

        var put = await client.PutAsJsonAsync("/v1/tenants/webhooks", new
        {
            webhookUrl = "https://integrador.example.com/hook",
            webhookSecret = "curto",
        });

        put.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Webhook_job_migra_segredo_legado_em_voo_e_entrega()
    {
        Guid tenantId;
        Guid entregaId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var prefixo = factory.BootstrapKey[..12];
            tenantId = (await db.ApiKeys.SingleAsync(k => k.Prefixo == prefixo)).TenantId;
            var tenant = await db.Tenants.FindAsync(tenantId);
            tenant!.WebhookUrl = "http://webhook-fake/hook";
            tenant.WebhookSecret = "segredo-legado-123456";
            tenant.WebhookSecretCriptografado = null;
            db.WebhooksEntrega.Add(new WebhookEntrega
            {
                Id = entregaId = Guid.NewGuid(),
                TenantId = tenantId,
                TipoEvento = "documento.autorizado",
                Payload = """{"evento":"teste"}""",
                Status = "PENDENTE",
            });
            await db.SaveChangesAsync();
        }

        var factory2 = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            var d = s.Single(x => x.ServiceType == typeof(IDespachanteWebhook));
            s.Remove(d);
            s.AddSingleton<IDespachanteWebhook>(new FakeDespachanteOk());
        }));
        using (var scope2 = factory2.Services.CreateScope())
        {
            var job = scope2.ServiceProvider.GetRequiredService<ProcessarWebhookJob>();
            await job.ExecutarAsync(entregaId, CancellationToken.None);

            var db2 = scope2.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var tenant = await db2.Tenants.FindAsync(tenantId);
            tenant!.WebhookSecret.Should().BeNull();
            Encoding.UTF8.GetString(tenant.WebhookSecretCriptografado!).Should().Be("segredo-legado-123456");

            var entrega = await db2.WebhooksEntrega.FindAsync(entregaId);
            await db2.Entry(entrega!).ReloadAsync();
            entrega!.Status.Should().Be("ENTREGUE");
        }
    }

    private class FakeDespachanteOk : IDespachanteWebhook
    {
        public Task<ResultadoEntrega> EntregarAsync(
            string url, string secret, string payload, CancellationToken cancellationToken) =>
            Task.FromResult(new ResultadoEntrega(true, 200, null));
    }
    // ---------- v2 F2/F3: item rico, totais, IPI/PIS/COFINS ----------

    [Fact]
    public async Task Emissao_com_item_rico_e_totais_v2_aceita_202()
    {
        var client = Client();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                destinatario = new { cnpjCpf = "11122233000144", nome = "Cliente Teste Ltda" },
                itens = new[]
                {
                    new
                    {
                        codigo = "SKU1",
                        descricao = "Produto a granel",
                        ncm = "12345678",
                        cfop = "5102",
                        gtin = "7891234567890",
                        cest = "0100400",
                        unidade = "KG",
                        quantidade = 2,
                        valorUnitario = 50,
                        valorTotal = 100,
                        valorDesconto = 10,
                        impostosV2 = new
                        {
                            icms = new { origem = 0, cst = "00", baseCalculo = 90, aliquota = 18, valor = 16.2 },
                        },
                    },
                },
                totais = new { valorProdutos = 100, valorNota = 110, valorFrete = 20 },
            }),
        };
        req.Headers.Add("Idempotency-Key", $"rico-{Guid.NewGuid()}");

        var resp = await client.SendAsync(req);
        var rawRico = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted, "body: {0}", rawRico);
    }

    [Fact]
    public async Task Emissao_com_formula_v2_errada_retorna_422()
    {
        var client = Client();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                destinatario = new { cnpjCpf = "11122233000144", nome = "Cliente Teste Ltda" },
                itens = new[]
                {
                    new
                    {
                        codigo = "SKU1", descricao = "x", ncm = "12345678", cfop = "5102",
                        quantidade = 1, valorUnitario = 100, valorTotal = 100, valorDesconto = 10,
                        impostosV2 = new { icms = new { origem = 0, cst = "00", baseCalculo = 90, aliquota = 18, valor = 16.2 } },
                    },
                },
                totais = new { valorProdutos = 100, valorNota = 100 }, // fórmula v2: 100 − 10 = 90
            }),
        };
        req.Headers.Add("Idempotency-Key", $"f2-bad-{Guid.NewGuid()}");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var conteudo = await resp.Content.ReadAsStringAsync();
        conteudo.Should().Contain("Fórmula v2");
    }

    [Fact]
    public async Task Emissao_nfe_com_ipi_pis_cofins_aceita_202()
    {
        var client = Client();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                destinatario = new { cnpjCpf = "11122233000144", nome = "Cliente Teste Ltda" },
                itens = new[]
                {
                    new
                    {
                        codigo = "SKU1", descricao = "x", ncm = "12345678", cfop = "5102",
                        quantidade = 1, valorUnitario = 100, valorTotal = 100,
                        impostosV2 = new
                        {
                            icms = new { origem = 0, cst = "00", baseCalculo = 100, aliquota = 18, valor = 18 },
                            ipi = new { cst = "00", baseCalculo = 100, aliquota = 10, valor = 10 },
                            pis = new { cst = "01", baseCalculo = 100, aliquota = 1.65, valor = 1.65 },
                            cofins = new { cst = "01", baseCalculo = 100, aliquota = 7.6, valor = 7.6 },
                        },
                    },
                },
                totais = new { valorProdutos = 100, valorNota = 110 }, // vNF inclui IPI 10
            }),
        };
        req.Headers.Add("Idempotency-Key", $"f3-{Guid.NewGuid()}");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
    // ---------- observabilidade (Fase 4) ----------

    [Fact]
    public async Task Metrics_endpoint_responde_com_metricas_de_negocio()
    {
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/metrics");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var conteudo = await resp.Content.ReadAsStringAsync();
        conteudo.Should().NotBeEmpty();
    }
    // ---------- v2 F4: NF-ref / devolução ----------

    [Fact]
    public async Task Devolucao_sem_nfes_referenciadas_retorna_422()
    {
        var client = Client();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                destinatario = new { cnpjCpf = "11122233000144", nome = "Cliente Teste Ltda" },
                finalidade = "devolucao",
                tipoOperacao = "entrada",
                itens = new[] { new { codigo = "X", descricao = "x", ncm = "12345678", cfop = "5102", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
                totais = new { valorProdutos = 10, valorNota = 10 },
            }),
        };
        req.Headers.Add("Idempotency-Key", $"dev-{Guid.NewGuid()}");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var conteudo = await resp.Content.ReadAsStringAsync();
        conteudo.Should().Contain("nfesReferenciadas");
    }

    [Fact]
    public async Task Devolucao_com_nf_ref_aceita_202()
    {
        var client = Client();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                destinatario = new { cnpjCpf = "11122233000144", nome = "Cliente Teste Ltda" },
                finalidade = "devolucao",
                tipoOperacao = "entrada",
                nfesReferenciadas = new[] { new { chaveAcesso = "41260912345678000199550010000001001000123456" } },
                itens = new[] { new { codigo = "X", descricao = "x", ncm = "12345678", cfop = "5102", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
                totais = new { valorProdutos = 10, valorNota = 10 },
            }),
        };
        req.Headers.Add("Idempotency-Key", $"dev-ok-{Guid.NewGuid()}");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Chave_referenciada_com_tamanho_errado_retorna_422()
    {
        var client = Client();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                destinatario = new { cnpjCpf = "11122233000144", nome = "Cliente Teste Ltda" },
                nfesReferenciadas = new[] { new { chaveAcesso = "123" } },
                itens = new[] { new { codigo = "X", descricao = "x", ncm = "12345678", cfop = "5102", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
                totais = new { valorProdutos = 10, valorNota = 10 },
            }),
        };
        req.Headers.Add("Idempotency-Key", $"dev-chave-{Guid.NewGuid()}");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var conteudo = await resp.Content.ReadAsStringAsync();
        conteudo.Should().Contain("44 dígitos");
    }
}
