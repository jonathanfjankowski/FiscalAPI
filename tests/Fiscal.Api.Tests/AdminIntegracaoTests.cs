using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EmissaoIntegracaoFactory = Fiscal.Api.Tests.EmissaoIntegracaoTests;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fiscal.Api.Tests;

/// <summary>
/// Testes de integração dos endpoints /v1/admin/* (auth JWT, tenants,
/// api keys, documentos, dashboard). Reaproveita o Factory da suíte de
/// emissão e adiciona envs de seed do admin.
/// </summary>
[Collection("fiscal-db")]
public class AdminIntegracaoTests : IClassFixture<AdminIntegracaoTests.Factory>
{
    private readonly Factory _factory;

    public AdminIntegracaoTests(Factory factory) => _factory = factory;

    public class Factory : EmissaoIntegracaoFactory.Factory
    {
        public const string AdminEmail = "admin@test.local";
        public const string AdminSenha = "senha-segura-123";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // DB separado da suíte de emissão: as fábricas rodam em
                    // paralelo e dois EnsureCreated no mesmo SQLite compartilhado
                    // correm (CREATE TABLE concorrente falha).
                    ["ConnectionStrings:Postgres"] = "Data Source=file:adminpanel_mem?mode=memory&cache=shared",
                    ["ADMIN_EMAIL"] = AdminEmail,
                    ["ADMIN_PASSWORD"] = AdminSenha,
                    ["ADMIN_JWT_SECRET"] = "jwt-secret-de-teste-com-mais-de-32-caracteres"
                });
            });
        }
    }

    private async Task<string> LoginAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/v1/admin/auth/login",
            new { email = Factory.AdminEmail, senha = Factory.AdminSenha });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        return client;
    }

    private void UsarToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Login_com_senha_errada_retorna_401()
    {
        var client = AdminClient();
        var resp = await client.PostAsJsonAsync("/v1/admin/auth/login",
            new { email = Factory.AdminEmail, senha = "errada" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_valido_retorna_token_e_expiracao()
    {
        var client = AdminClient();
        var resp = await client.PostAsJsonAsync("/v1/admin/auth/login",
            new { email = Factory.AdminEmail, senha = Factory.AdminSenha });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("expiraEm").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Endpoints_admin_sem_token_retornam_401()
    {
        var client = AdminClient();
        var resp = await client.GetAsync("/v1/admin/tenants");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lista_tenants_inclui_tenant_bootstrap()
    {
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));

        var resp = await client.GetAsync("/v1/admin/tenants");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.EnumerateArray().Should().Contain(t =>
            t.GetProperty("razaoSocial").GetString() == "Tenant Teste");
    }

    [Fact]
    public async Task Fluxo_completo_criar_tenant_gerar_e_revogar_api_key()
    {
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));

        var cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10)));
        var create = await client.PostAsJsonAsync("/v1/admin/tenants", new
        {
            cnpj,
            razaoSocial = "Tenant Admin Painel LTDA",
            uf = "PR",
            codigoMunicipioIbge = "4106902",
            regimeTributario = 3,
            ambientePadrao = "homologacao"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tenantId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();

        // CNPJ duplicado → 409
        var duplicado = await client.PostAsJsonAsync("/v1/admin/tenants", new
        {
            cnpj,
            razaoSocial = "Duplicado LTDA",
            uf = "PR"
        });
        duplicado.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // API key: criada, listada (só prefixo) e revogada
        var keyResp = await client.PostAsJsonAsync($"/v1/admin/tenants/{tenantId}/api-keys",
            new { descricao = "key do painel", ambiente = "homologacao" });
        keyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyBody = await keyResp.Content.ReadFromJsonAsync<JsonElement>();
        var chave = keyBody.GetProperty("chave").GetString()!;
        chave.Should().StartWith("fk_test_");
        var keyId = keyBody.GetProperty("id").GetGuid();

        var listKeys = await client.GetAsync($"/v1/admin/tenants/{tenantId}/api-keys");
        var keys = await listKeys.Content.ReadFromJsonAsync<JsonElement>();
        keys.EnumerateArray().Should().Contain(k => k.GetProperty("id").GetGuid() == keyId);
        keys.EnumerateArray().First().TryGetProperty("chave", out _).Should().BeFalse();

        // A chave gerada funciona na API de tenant
        var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", chave);
        var check = await apiClient.GetAsync("/v1/api-keys");
        check.StatusCode.Should().Be(HttpStatusCode.OK);

        var revoke = await client.DeleteAsync($"/v1/admin/tenants/{tenantId}/api-keys/{keyId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Após revogação, a chave não autentica mais
        var checkRevogada = await apiClient.GetAsync("/v1/api-keys");
        checkRevogada.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lista_documentos_cruzada_mostra_documento_do_tenant()
    {
        // Cria um documento via API de tenant (bootstrap key)…
        var tenantClient = _factory.CreateClient();
        tenantClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                itens = new[] { new { codigo = "X", descricao = "x", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
                totais = new { valorProdutos = 10, valorNota = 10 }
            })
        };
        req.Headers.Add("Idempotency-Key", $"admin-list-{Guid.NewGuid()}");
        var resp = await tenantClient.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // …e verifica que aparece na listagem admin.
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));
        var list = await client.GetAsync("/v1/admin/documentos-fiscais?pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        body.GetProperty("itens").EnumerateArray()
            .Should().Contain(d => d.GetProperty("id").GetGuid() == id);

        var detail = await client.GetAsync($"/v1/admin/documentos-fiscais/{id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await detail.Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("status").GetString().Should().Be("PENDENTE");
        doc.GetProperty("tenantRazaoSocial").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Lista_documentos_rejeita_status_invalido_com_422()
    {
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));
        var resp = await client.GetAsync("/v1/admin/documentos-fiscais?status=NAO_EXISTE");
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Cancelamento_via_painel_em_documento_autorizado_funciona()
    {
        // Cria documento e força AUTORIZADA direto no banco.
        var tenantClient = _factory.CreateClient();
        tenantClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                itens = new[] { new { codigo = "X", descricao = "x", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
                totais = new { valorProdutos = 10, valorNota = 10 }
            })
        };
        req.Headers.Add("Idempotency-Key", $"admin-canc-{Guid.NewGuid()}");
        var resp = await tenantClient.SendAsync(req);
        var id = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Fiscal.Persistence.FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FindAsync(id);
            doc!.Status = Fiscal.Core.Enums.StatusDocumento.AUTORIZADA;
            await db.SaveChangesAsync();
        }

        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));
        var canc = await client.PostAsJsonAsync($"/v1/admin/documentos-fiscais/{id}/cancelamento",
            new { justificativa = "Cancelamento feito pelo painel admin" });
        canc.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await canc.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("eventoId").GetGuid().Should().NotBeEmpty();

        var detail = await client.GetAsync($"/v1/admin/documentos-fiscais/{id}");
        var doc2 = await detail.Content.ReadFromJsonAsync<JsonElement>();
        doc2.GetProperty("status").GetString().Should().Be("CANCELAMENTO_PENDENTE");

        // CC-e em NF-e autorizada→ agora CANCELAMENTO_PENDENTE → 409
        var cce = await client.PostAsJsonAsync($"/v1/admin/documentos-fiscais/{id}/carta-correcao",
            new { correcao = "Tentativa de CC-e após cancelamento" });
        cce.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dashboard_retorna_resumo_com_contagens()
    {
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));
        var resp = await client.GetAsync("/v1/admin/dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("documentos7Dias").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        body.GetProperty("tenantsAtivos").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("porStatus").EnumerateArray().Should().NotBeNull();
    }
}
