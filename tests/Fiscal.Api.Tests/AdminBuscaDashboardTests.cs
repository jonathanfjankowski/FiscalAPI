using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fiscal.Api.Tests;

/// <summary>
/// Busca/ordenação de documentos e o dashboard de atenção.
///
/// O dashboard não responde "quantos" — responde "o que está travado": fila
/// de transmissão, certificados vencendo (com a empresa), webhooks em falha e
/// rejeições recentes com o motivo da SEFAZ.
/// </summary>
[Collection("fiscal-db")]
public class AdminBuscaDashboardTests : IClassFixture<AdminIntegracaoTests.Factory>
{
    private readonly AdminIntegracaoTests.Factory _factory;

    public AdminBuscaDashboardTests(AdminIntegracaoTests.Factory factory) => _factory = factory;

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/admin/auth/login", new
        {
            email = AdminIntegracaoTests.Factory.AdminEmail,
            senha = AdminIntegracaoTests.Factory.AdminSenha
        });
        resp.EnsureSuccessStatusCode();
        var token = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Tenant> CriarTenantAsync(string razao = "Busca Tenant LTDA")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var tenant = new Tenant
        {
            Cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10))),
            RazaoSocial = razao,
            Uf = "PR",
            RegimeTributario = 3,
            AmbientePadrao = (short)Ambiente.Homologacao,
            Sandbox = true,
            Ativo = true,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<DocumentoFiscal> CriarDocAsync(
        Tenant tenant, StatusDocumento status, long numero, string? chave = null, int diasAtras = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var doc = new DocumentoFiscal
        {
            TenantId = tenant.Id,
            IdempotencyKey = Guid.NewGuid().ToString(),
            Tipo = TipoDocumento.NFE,
            Modelo = 55,
            Serie = 1,
            Numero = numero,
            Ambiente = (short)Ambiente.Homologacao,
            Status = status,
            ChaveAcesso = chave,
            MotivoStatus = status == StatusDocumento.REJEITADA ? "Rejeicao 999: NCM inexistente" : null,
            PayloadEntrada = "{}",
            CriadoEm = DateTimeOffset.UtcNow.AddDays(-diasAtras),
            AtualizadoEm = DateTimeOffset.UtcNow.AddDays(-diasAtras),
        };
        db.DocumentosFiscais.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    // ---------------- Busca e ordenação ----------------

    [Fact]
    public async Task Busca_por_razao_social_encontra_documentos_do_tenant()
    {
        var tenant = await CriarTenantAsync("Padaria Buscavel LTDA");
        await CriarDocAsync(tenant, StatusDocumento.AUTORIZADA, 1001);

        var client = await AdminAsync();
        var body = await (await client.GetAsync("/v1/admin/documentos-fiscais?q=Padaria Buscavel"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("itens").EnumerateArray()
            .All(d => d.GetProperty("tenantRazaoSocial").GetString() == "Padaria Buscavel LTDA")
            .Should().BeTrue();
    }

    [Fact]
    public async Task Busca_por_numero_encontra_o_documento()
    {
        var tenant = await CriarTenantAsync("Tenant Busca Numero LTDA");
        var numero = Random.Shared.Next(500_000, 999_999);
        await CriarDocAsync(tenant, StatusDocumento.AUTORIZADA, numero);

        var client = await AdminAsync();
        var body = await (await client.GetAsync($"/v1/admin/documentos-fiscais?q={numero}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("itens").EnumerateArray()
            .Any(d => d.GetProperty("numero").GetInt64() == numero).Should().BeTrue();
    }

    [Fact]
    public async Task Busca_por_chave_de_acesso_encontra_o_documento()
    {
        var tenant = await CriarTenantAsync("Tenant Busca Chave LTDA");
        var chave = string.Concat(Enumerable.Range(0, 44).Select(_ => Random.Shared.Next(10)));
        await CriarDocAsync(tenant, StatusDocumento.AUTORIZADA, 4242, chave);

        var client = await AdminAsync();
        var body = await (await client.GetAsync($"/v1/admin/documentos-fiscais?q={chave}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("itens").EnumerateArray()
            .Any(d => d.GetProperty("chaveAcesso").GetString() == chave).Should().BeTrue();
    }

    [Fact]
    public async Task Busca_sem_resultado_devolve_total_zero_e_lista_vazia()
    {
        var client = await AdminAsync();
        var body = await (await client.GetAsync("/v1/admin/documentos-fiscais?q=zzz-nao-existe-zzz"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("total").GetInt32().Should().Be(0);
        body.GetProperty("itens").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Ordenacao_asc_poe_o_mais_antigo_primeiro()
    {
        var tenant = await CriarTenantAsync("Tenant Ordenacao LTDA");
        await CriarDocAsync(tenant, StatusDocumento.AUTORIZADA, 7001, diasAtras: 10);
        await CriarDocAsync(tenant, StatusDocumento.AUTORIZADA, 7002, diasAtras: 1);

        var client = await AdminAsync();
        var body = await (await client.GetAsync(
                $"/v1/admin/documentos-fiscais?tenantId={tenant.Id}&ordenarPor=criadoEm&ordem=asc"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var numeros = body.GetProperty("itens").EnumerateArray()
            .Select(d => d.GetProperty("numero").GetInt64()).ToList();

        numeros.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Ordenacao_desc_e_o_padrao()
    {
        var tenant = await CriarTenantAsync("Tenant Ordenacao Desc LTDA");
        await CriarDocAsync(tenant, StatusDocumento.AUTORIZADA, 8001, diasAtras: 10);
        await CriarDocAsync(tenant, StatusDocumento.AUTORIZADA, 8002, diasAtras: 1);

        var client = await AdminAsync();
        var body = await (await client.GetAsync($"/v1/admin/documentos-fiscais?tenantId={tenant.Id}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var numeros = body.GetProperty("itens").EnumerateArray()
            .Select(d => d.GetProperty("numero").GetInt64()).ToList();

        numeros.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Ordenar_por_campo_invalido_retorna_422()
    {
        var client = await AdminAsync();
        var resp = await client.GetAsync("/v1/admin/documentos-fiscais?ordenarPor=senhaDoBanco");

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------- Dashboard de atenção ----------------

    [Fact]
    public async Task Dashboard_traz_a_fila_de_transmissao()
    {
        var tenant = await CriarTenantAsync("Tenant Fila LTDA");
        await CriarDocAsync(tenant, StatusDocumento.PENDENTE, 9001);
        await CriarDocAsync(tenant, StatusDocumento.PROCESSANDO, 9002);
        await CriarDocAsync(tenant, StatusDocumento.CONTINGENCIA, 9003);
        await CriarDocAsync(tenant, StatusDocumento.AUTORIZADA, 9004);

        var client = await AdminAsync();
        var body = await (await client.GetAsync("/v1/admin/dashboard"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var fila = body.GetProperty("fila").EnumerateArray().ToList();
        fila.Should().NotBeEmpty();

        var statusNaFila = fila.Select(d => d.GetProperty("status").GetString()).ToList();
        statusNaFila.Should().Contain("PENDENTE");
        statusNaFila.Should().Contain("PROCESSANDO");
        statusNaFila.Should().Contain("CONTINGENCIA");
        // Autorizada não é fila.
        statusNaFila.Should().NotContain("AUTORIZADA");

        // Cada item da fila carrega o necessário para agir.
        var item = fila.First();
        item.TryGetProperty("tenantRazaoSocial", out _).Should().BeTrue();
        item.TryGetProperty("numero", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Dashboard_lista_certificados_vencendo_com_a_empresa()
    {
        var tenant = await CriarTenantAsync("Tenant Cert Vencendo LTDA");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            db.Certificados.Add(new Certificado
            {
                TenantId = tenant.Id,
                Thumbprint = "ABC123",
                ValidoAte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
                Ativo = true,
                PfxCriptografado = [1, 2, 3],
                SenhaCriptografada = [1, 2, 3],
                ChaveDekCriptografada = [1, 2, 3],
            });
            // Um certificado distante NÃO deve aparecer.
            db.Certificados.Add(new Certificado
            {
                TenantId = tenant.Id,
                Thumbprint = "DEF456",
                ValidoAte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(365)),
                Ativo = true,
                PfxCriptografado = [1, 2, 3],
                SenhaCriptografada = [1, 2, 3],
                ChaveDekCriptografada = [1, 2, 3],
            });
            await db.SaveChangesAsync();
        }

        var client = await AdminAsync();
        var body = await (await client.GetAsync("/v1/admin/dashboard"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var vencendo = body.GetProperty("certificadosVencendo").EnumerateArray().ToList();
        vencendo.Should().Contain(c =>
            c.GetProperty("tenantId").GetGuid() == tenant.Id &&
            c.GetProperty("thumbprint").GetString() == "ABC123");

        var item = vencendo.First(c => c.GetProperty("tenantId").GetGuid() == tenant.Id);
        item.GetProperty("tenantRazaoSocial").GetString().Should().Be("Tenant Cert Vencendo LTDA");
        item.GetProperty("diasRestantes").GetInt32().Should().BeLessThanOrEqualTo(30);

        vencendo.Should().NotContain(c => c.GetProperty("thumbprint").GetString() == "DEF456");
    }

    [Fact]
    public async Task Dashboard_lista_webhooks_em_falha()
    {
        var tenant = await CriarTenantAsync("Tenant Webhook Falha LTDA");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            db.WebhooksEntrega.Add(new WebhookEntrega
            {
                TenantId = tenant.Id,
                TipoEvento = "documento.autorizado",
                Payload = "{}",
                Status = "FALHA",
                Tentativas = 8,
                UltimoStatusCode = 500,
                UltimoErro = "Connection refused",
            });
            await db.SaveChangesAsync();
        }

        var client = await AdminAsync();
        var body = await (await client.GetAsync("/v1/admin/dashboard"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var falhas = body.GetProperty("webhooksEmFalha").EnumerateArray().ToList();
        var doTenant = falhas.FirstOrDefault(w => w.GetProperty("tenantId").GetGuid() == tenant.Id);

        doTenant.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        doTenant.GetProperty("tipoEvento").GetString().Should().Be("documento.autorizado");
        doTenant.GetProperty("tentativas").GetInt32().Should().Be(8);
        doTenant.GetProperty("ultimoStatusCode").GetInt32().Should().Be(500);
        doTenant.GetProperty("tenantRazaoSocial").GetString().Should().Be("Tenant Webhook Falha LTDA");
    }

    [Fact]
    public async Task Dashboard_lista_rejeicoes_recentes_com_motivo()
    {
        var tenant = await CriarTenantAsync("Tenant Rejeicoes LTDA");
        await CriarDocAsync(tenant, StatusDocumento.REJEITADA, 5555);

        var client = await AdminAsync();
        var body = await (await client.GetAsync("/v1/admin/dashboard"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var rejeicoes = body.GetProperty("rejeicoesRecentes").EnumerateArray().ToList();
        var daEmpresa = rejeicoes.FirstOrDefault(r => r.GetProperty("tenantId").GetGuid() == tenant.Id);

        daEmpresa.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        daEmpresa.GetProperty("motivo").GetString().Should().Contain("NCM inexistente");
        daEmpresa.GetProperty("numero").GetInt64().Should().Be(5555);
    }

    [Fact]
    public async Task Dashboard_mantem_as_contagens_antigas()
    {
        var client = await AdminAsync();
        var body = await (await client.GetAsync("/v1/admin/dashboard"))
            .Content.ReadFromJsonAsync<JsonElement>();

        // Compatibilidade: os campos que o painel antigo usa continuam lá.
        body.TryGetProperty("documentosHoje", out _).Should().BeTrue();
        body.TryGetProperty("documentos7Dias", out _).Should().BeTrue();
        body.TryGetProperty("emContingencia", out _).Should().BeTrue();
        body.TryGetProperty("porStatus", out _).Should().BeTrue();
        body.TryGetProperty("tenantsAtivos", out _).Should().BeTrue();
        body.TryGetProperty("apiKeysAtivas", out _).Should().BeTrue();
        body.TryGetProperty("certificadosVencendo30Dias", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Dashboard_sem_token_retorna_401()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/v1/admin/dashboard")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}