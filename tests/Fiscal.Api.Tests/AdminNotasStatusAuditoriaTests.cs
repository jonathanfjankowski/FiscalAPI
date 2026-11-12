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
/// Endpoints admin de notas recebidas (distribuição DFe), status-serviço e
/// auditoria. A auditoria é leitura pura: a tabela é append-only por trigger,
/// então o endpoint nunca escreve.
/// </summary>
[Collection("fiscal-db")]
public class AdminNotasStatusAuditoriaTests : IClassFixture<AdminIntegracaoTests.Factory>
{
    private readonly AdminIntegracaoTests.Factory _factory;

    public AdminNotasStatusAuditoriaTests(AdminIntegracaoTests.Factory factory) => _factory = factory;

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

    private async Task<Tenant> CriarTenantAsync(bool sandbox = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var tenant = new Tenant
        {
            Cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10))),
            RazaoSocial = "Tenant Notas LTDA",
            Uf = "PR",
            RegimeTributario = 3,
            AmbientePadrao = (short)Ambiente.Homologacao,
            Sandbox = sandbox,
            Ativo = true,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<(Guid NotaId, string Chave)> CriarNotaAsync(Tenant tenant, bool comXmlCompleto = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var chave = string.Concat(Enumerable.Range(0, 44).Select(_ => Random.Shared.Next(10)));
        var nota = new NotaRecebida
        {
            TenantId = tenant.Id,
            Ambiente = (short)Ambiente.Homologacao,
            Chave = chave,
            Nsu = "000000000000001",
            TipoSchema = comXmlCompleto ? "procNFe" : "resNFe",
            XmlResumo = "<resNFe/>",
            XmlCompleto = comXmlCompleto ? "<nfeProc/>" : null,
            CnpjEmitente = "12345678000199",
            NomeEmitente = "Emitente Teste LTDA",
            Valor = 150.75m,
            EmitidaEm = DateTimeOffset.UtcNow.AddDays(-2),
        };
        db.NotasRecebidas.Add(nota);
        await db.SaveChangesAsync();
        return (nota.Id, chave);
    }

    // ---------------- Notas recebidas ----------------

    [Fact]
    public async Task Notas_sem_token_retorna_401()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/v1/admin/notas-recebidas")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Notas_lista_cross_tenant_com_filtro_por_tenant()
    {
        var t1 = await CriarTenantAsync();
        var t2 = await CriarTenantAsync();
        await CriarNotaAsync(t1);
        await CriarNotaAsync(t2);

        var client = await AdminAsync();

        // Sem filtro: as duas aparecem (a listagem é cross-tenant).
        var todas = await (await client.GetAsync("/v1/admin/notas-recebidas"))
            .Content.ReadFromJsonAsync<JsonElement>();
        todas.GetProperty("itens").EnumerateArray()
            .Count(n => n.GetProperty("tenantId").GetGuid() == t1.Id || n.GetProperty("tenantId").GetGuid() == t2.Id)
            .Should().Be(2);

        // Com filtro: só a do tenant pedido.
        var filtrada = await (await client.GetAsync($"/v1/admin/notas-recebidas?tenantId={t1.Id}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        filtrada.GetProperty("itens").EnumerateArray()
            .All(n => n.GetProperty("tenantId").GetGuid() == t1.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Nota_detalhe_de_inexistente_retorna_404()
    {
        var client = await AdminAsync();
        (await client.GetAsync($"/v1/admin/notas-recebidas/{Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nota_xml_completo_indisponivel_retorna_409()
    {
        var tenant = await CriarTenantAsync();
        var (notaId, _) = await CriarNotaAsync(tenant, comXmlCompleto: false);
        var client = await AdminAsync();

        var resp = await client.GetAsync($"/v1/admin/notas-recebidas/{notaId}/xml-completo");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Nota_xml_completo_disponivel_retorna_xml()
    {
        var tenant = await CriarTenantAsync();
        var (notaId, _) = await CriarNotaAsync(tenant, comXmlCompleto: true);
        var client = await AdminAsync();

        var resp = await client.GetAsync($"/v1/admin/notas-recebidas/{notaId}/xml-completo");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("<nfeProc/>");
    }

    [Fact]
    public async Task Manifestacao_com_tipo_invalido_retorna_422()
    {
        var tenant = await CriarTenantAsync();
        var (notaId, _) = await CriarNotaAsync(tenant);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/notas-recebidas/{notaId}/manifestacao",
            new { tipo = "999999", justificativa = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Manifestacao_de_nao_realizacao_sem_justificativa_retorna_422()
    {
        var tenant = await CriarTenantAsync();
        var (notaId, _) = await CriarNotaAsync(tenant);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/notas-recebidas/{notaId}/manifestacao",
            new { tipo = "210240", justificativa = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Manifestacao_valida_e_aceita()
    {
        var tenant = await CriarTenantAsync();
        var (notaId, _) = await CriarNotaAsync(tenant);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/notas-recebidas/{notaId}/manifestacao",
            new { tipo = "210210", justificativa = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tipo").GetString().Should().Be("210210");
        body.GetProperty("status").GetString().Should().Be("PENDENTE");
    }

    [Fact]
    public async Task Manifestacao_em_nota_ja_manifestada_retorna_409()
    {
        var tenant = await CriarTenantAsync();
        var (notaId, _) = await CriarNotaAsync(tenant);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var nota = await db.NotasRecebidas.FindAsync(notaId);
            nota!.ManifestacaoAtual = "confirmacao"; // estado final
            await db.SaveChangesAsync();
        }

        var client = await AdminAsync();
        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/notas-recebidas/{notaId}/manifestacao",
            new { tipo = "210210", justificativa = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---------------- Status-serviço ----------------

    [Fact]
    public async Task Status_servico_sem_token_retorna_401()
    {
        var tenant = await CriarTenantAsync();
        var client = _factory.CreateClient();
        (await client.GetAsync($"/v1/admin/status-servico?tenantId={tenant.Id}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Status_servico_de_tenant_inexistente_retorna_404()
    {
        var client = await AdminAsync();
        (await client.GetAsync($"/v1/admin/status-servico?tenantId={Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Status_servico_de_tenant_sandbox_retorna_status_do_mock()
    {
        var tenant = await CriarTenantAsync(sandbox: true);
        var client = await AdminAsync();

        var resp = await client.GetAsync($"/v1/admin/status-servico?tenantId={tenant.Id}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("cStat").GetInt32().Should().Be(107); // 107 = serviço em operação
        body.GetProperty("xMotivo").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Status_servico_com_modelo_invalido_retorna_422()
    {
        var tenant = await CriarTenantAsync();
        var client = await AdminAsync();
        (await client.GetAsync($"/v1/admin/status-servico?tenantId={tenant.Id}&modelo=99")).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------- Auditoria ----------------

    [Fact]
    public async Task Auditoria_sem_token_retorna_401()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/v1/admin/auditoria")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Auditoria_lista_paginada_com_filtros()
    {
        var tenant = await CriarTenantAsync();
        var acao = $"TESTE_AUDITORIA_{Guid.NewGuid():N}"[..40];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            for (var i = 0; i < 3; i++)
            {
                db.Auditoria.Add(new Auditoria
                {
                    TenantId = tenant.Id,
                    Acao = acao,
                    RecursoId = Guid.NewGuid(),
                    IpOrigem = System.Net.IPAddress.Parse("203.0.113.10"),
                    Detalhe = $"{{\"i\":{i}}}",
                });
            }
            await db.SaveChangesAsync();
        }

        var client = await AdminAsync();

        var resp = await client.GetAsync($"/v1/admin/auditoria?acao={acao}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("total").GetInt32().Should().Be(3);
        var itens = body.GetProperty("itens");
        itens.GetArrayLength().Should().Be(3);

        var primeiro = itens[0];
        primeiro.GetProperty("acao").GetString().Should().Be(acao);
        primeiro.GetProperty("ipOrigem").GetString().Should().Be("203.0.113.10");
        // O detalhe vem como texto (jsonb no banco), não como objeto aninhado.
        primeiro.GetProperty("detalhe").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Auditoria_respeita_page_e_pageSize()
    {
        var tenant = await CriarTenantAsync();
        var acao = $"TESTE_PAG_{Guid.NewGuid():N}"[..40];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            for (var i = 0; i < 5; i++)
            {
                db.Auditoria.Add(new Auditoria { TenantId = tenant.Id, Acao = acao, Detalhe = "{}" });
            }
            await db.SaveChangesAsync();
        }

        var client = await AdminAsync();
        var body = await (await client.GetAsync($"/v1/admin/auditoria?acao={acao}&page=2&pageSize=2"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("total").GetInt32().Should().Be(5);
        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("pageSize").GetInt32().Should().Be(2);
        body.GetProperty("itens").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Auditoria_de_tenant_sem_registros_vem_vazia()
    {
        var tenant = await CriarTenantAsync();
        var client = await AdminAsync();

        var body = await (await client.GetAsync($"/v1/admin/auditoria?tenantId={tenant.Id}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("total").GetInt32().Should().Be(0);
        body.GetProperty("itens").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// A tabela mostra só os 8 primeiros caracteres do recurso. Exigir o GUID
    /// completo tornaria o filtro inútil a partir do que o operador vê.
    /// </summary>
    [Fact]
    public async Task Auditoria_filtra_por_prefixo_do_recurso()
    {
        var tenant = await CriarTenantAsync();
        var recurso = Guid.NewGuid();
        var prefixo = recurso.ToString()[..8];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            db.Auditoria.Add(new Auditoria
            {
                TenantId = tenant.Id,
                Acao = "TESTE_PREFIXO",
                RecursoId = recurso,
                Detalhe = "{}",
            });
            // Outro recurso, com prefixo diferente — não deve aparecer.
            db.Auditoria.Add(new Auditoria
            {
                TenantId = tenant.Id,
                Acao = "TESTE_PREFIXO",
                RecursoId = Guid.NewGuid(),
                Detalhe = "{}",
            });
            await db.SaveChangesAsync();
        }

        var client = await AdminAsync();
        var body = await (await client.GetAsync($"/v1/admin/auditoria?recursoId={prefixo}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("total").GetInt32().Should().Be(1);
        body.GetProperty("itens")[0].GetProperty("recursoId").GetGuid().Should().Be(recurso);
    }
}