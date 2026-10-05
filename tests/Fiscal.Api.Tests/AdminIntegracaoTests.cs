using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using EmissaoIntegracaoFactory = Fiscal.Api.Tests.EmissaoIntegracaoTests;
using Fiscal.Persistence.Criptografia;
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
    public async Task Criar_tenant_com_criarApiKey_homologacao_cria_chave_na_mesma_transacao()
    {
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));

        var cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10)));
        var create = await client.PostAsJsonAsync("/v1/admin/tenants", new
        {
            cnpj,
            razaoSocial = "Tenant Com Key Automatica LTDA",
            uf = "PR",
            criarApiKey = "homologacao"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = body.GetProperty("tenant").GetProperty("id").GetGuid();
        body.GetProperty("tenant").GetProperty("apiKeysAtivas").GetInt32().Should().Be(1);

        var apiKey = body.GetProperty("apiKey");
        var chave = apiKey.GetProperty("chave").GetString()!;
        chave.Should().StartWith("fk_test_");
        apiKey.GetProperty("prefixo").GetString().Should().Be(chave[..12]);
        apiKey.GetProperty("ambiente").GetString().Should().Be("homologacao");
        apiKey.GetProperty("aviso").GetString().Should().NotBeNullOrEmpty();
        var keyId = apiKey.GetProperty("id").GetGuid();

        // Persistiu hash PBKDF2 (nunca a chave em claro) + prefixo de lookup.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Fiscal.Persistence.FiscalDbContext>();
            var persisted = await db.ApiKeys.FindAsync(new object[] { keyId });
            persisted.Should().NotBeNull();
            persisted!.KeyHash.Should().StartWith("$pbkdf2-sha256$").And.NotContain(chave);
            persisted.Prefixo.Should().Be(chave[..12]);
            persisted.Ativa.Should().BeTrue();
            persisted.RevogadoEm.Should().BeNull();
            persisted.Ambiente.Should().Be((short)Fiscal.Core.Enums.Ambiente.Homologacao);

            // Mesma transação: o tenant nasce com a chave — GET admin confirma 1 ativa.
            var obter = await client.GetAsync($"/v1/admin/tenants/{tenantId}");
            obter.StatusCode.Should().Be(HttpStatusCode.OK);
            var tenantBody = await obter.Content.ReadFromJsonAsync<JsonElement>();
            tenantBody.GetProperty("apiKeysAtivas").GetInt32().Should().Be(1);
        }

        // A chave gerada autentica na API de tenant.
        var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", chave);
        var check = await apiClient.GetAsync("/v1/api-keys");
        check.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Criar_tenant_com_criarApiKey_producao_gera_chave_fk_live()
    {
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));

        var cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10)));
        var create = await client.PostAsJsonAsync("/v1/admin/tenants", new
        {
            cnpj,
            razaoSocial = "Tenant Chave Producao LTDA",
            uf = "PR",
            criarApiKey = "producao"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        var apiKey = body.GetProperty("apiKey");
        apiKey.GetProperty("chave").GetString().Should().StartWith("fk_live_");
        apiKey.GetProperty("ambiente").GetString().Should().Be("producao");
    }

    [Fact]
    public async Task Criar_tenant_com_criarApiKey_invalido_retorna_422_e_nao_cria_tenant()
    {
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));

        var cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10)));
        var resp = await client.PostAsJsonAsync("/v1/admin/tenants", new
        {
            cnpj,
            razaoSocial = "Tenant CriarKey Invalida LTDA",
            uf = "PR",
            criarApiKey = "testando"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Nada foi persistido: o mesmo CNPJ pode ser usado num create válido logo depois.
        var retry = await client.PostAsJsonAsync("/v1/admin/tenants", new
        {
            cnpj,
            razaoSocial = "Tenant CriarKey Invalida LTDA",
            uf = "PR"
        });
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Criar_tenant_sem_criarApiKey_mantem_comportamento_antigo()
    {
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));

        var cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10)));
        var create = await client.PostAsJsonAsync("/v1/admin/tenants", new
        {
            cnpj,
            razaoSocial = "Tenant Sem Auto Key LTDA",
            uf = "PR"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // Corpo legado intacto: GUID puro (string), não um objeto.
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.String);
        var tenantId = body.GetGuid();

        // Nenhuma chave foi criada junto.
        var keys = await client.GetAsync($"/v1/admin/tenants/{tenantId}/api-keys");
        keys.StatusCode.Should().Be(HttpStatusCode.OK);
        (await keys.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().Should().BeEmpty();
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

    [Fact]
    public async Task Certificado_admin_upload_rotaciona_desativa_e_reativa()
    {
        var client = AdminClient();
        UsarToken(client, await LoginAsync(client));

        var cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10)));
        var create = await client.PostAsJsonAsync("/v1/admin/tenants", new
        {
            cnpj,
            razaoSocial = "Tenant Cert Ciclo LTDA",
            uf = "PR",
            codigoMunicipioIbge = "4106902",
            regimeTributario = 3,
            ambientePadrao = "homologacao"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tenantId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetGuid();

        // Upload exige o EnvelopeEncryptionService REAL (o fixture usa stub de decrypt).
        var factory2 = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Certificados:ChaveMestraKEK"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            }));
            b.ConfigureServices(services =>
            {
                var stub = services.Single(d => d.ServiceType == typeof(Fiscal.Core.Interfaces.ICertificadoStore));
                services.Remove(stub);
                services.AddSingleton<Fiscal.Core.Interfaces.ICertificadoStore, EnvelopeEncryptionService>();
            });
        });
        var admin2 = factory2.CreateClient();
        UsarToken(admin2, await LoginAsync(admin2));

        static async Task<(Guid Id, string Thumbprint)> UploadAsync(HttpClient c, Guid tId, string senha)
        {
            using var cert = CriarCertAdmin();
            var pfx = cert.Export(X509ContentType.Pfx, senha);
            using var form = new MultipartFormDataContent();
            var conteudo = new ByteArrayContent(pfx);
            conteudo.Headers.ContentType = new("application/x-pkcs12");
            form.Add(conteudo, "pfx", "admin.pfx");
            form.Add(new StringContent(senha), "senha");
            var resp = await c.PostAsync($"/v1/admin/tenants/{tId}/certificados", form);
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return (body.GetProperty("id").GetGuid(), body.GetProperty("thumbprint").GetString()!);
        }

        static X509Certificate2 CriarCertAdmin()
        {
            var req = new CertificateRequest("CN=Admin Cert Ciclo", RSA.Create(2048),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        }

        var (id1, _) = await UploadAsync(admin2, tenantId, "senha-a");
        var (id2, _) = await UploadAsync(admin2, tenantId, "senha-b");

        var listar = await admin2.GetAsync($"/v1/admin/tenants/{tenantId}/certificados");
        listar.StatusCode.Should().Be(HttpStatusCode.OK);
        var certs = await listar.Content.ReadFromJsonAsync<JsonElement>();
        certs.EnumerateArray().Where(c => c.GetProperty("ativo").GetBoolean())
            .Select(c => c.GetProperty("id").GetGuid())
            .Should().ContainSingle().Which.Should().Be(id2);

        (await admin2.DeleteAsync($"/v1/admin/tenants/{tenantId}/certificados/{id2}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reativar = await admin2.PostAsync($"/v1/admin/tenants/{tenantId}/certificados/{id1}/ativar", null);
        reativar.StatusCode.Should().Be(HttpStatusCode.OK);

        certs = await (await admin2.GetAsync($"/v1/admin/tenants/{tenantId}/certificados"))
            .Content.ReadFromJsonAsync<JsonElement>();
        certs.EnumerateArray().Where(c => c.GetProperty("ativo").GetBoolean())
            .Select(c => c.GetProperty("id").GetGuid())
            .Should().ContainSingle().Which.Should().Be(id1);

        // Tenant inexistente → 404 nos endpoints de certificado.
        var outro = Guid.NewGuid();
        (await admin2.DeleteAsync($"/v1/admin/tenants/{outro}/certificados/{id1}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin2.PostAsync($"/v1/admin/tenants/{outro}/certificados/{id1}/ativar", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
