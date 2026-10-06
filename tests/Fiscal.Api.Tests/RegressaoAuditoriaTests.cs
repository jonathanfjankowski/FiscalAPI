using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using EmissaoIntegracaoFactory = Fiscal.Api.Tests.EmissaoIntegracaoTests;
using FluentAssertions;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Fiscal.Persistence.Repositories;
using Fiscal.Worker.Delivery;
using Fiscal.Worker.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fiscal.Api.Tests;

/// <summary>
/// Regressões da auditoria de 2026-09: cursor de NSU da Distribuição DFe
/// (UltNSU, não MaxNSU), janela SVC de 168h e claim atômico dos jobs (status
/// não regride).
/// </summary>
[Collection("fiscal-db")]
public class RegressaoAuditoriaTests : IClassFixture<EmissaoIntegracaoFactory.Factory>
{
    private readonly EmissaoIntegracaoFactory.Factory _factory;

    public RegressaoAuditoriaTests(EmissaoIntegracaoFactory.Factory factory) => _factory = factory;

    // --- Distribuição DFe: cursor correto e multi-página -----------------------

    private class ConsultaMultiPagina : IConsultaDistribuicaoDfe
    {
        private readonly Queue<ResultadoDistribuicao> _paginas;
        public List<string> NsuConsultados { get; } = new();

        public ConsultaMultiPagina(params ResultadoDistribuicao[] paginas) =>
            _paginas = new Queue<ResultadoDistribuicao>(paginas);

        public Task<ResultadoDistribuicao> ConsultarAsync(
            Tenant tenant, X509Certificate2? certificado, Ambiente ambiente,
            string ultimoNsu, CancellationToken cancellationToken)
        {
            NsuConsultados.Add(ultimoNsu);
            if (_paginas.Count > 0) return Task.FromResult(_paginas.Dequeue());
            // Fila vazia: nada novo (137) — NSU inalterado.
            return Task.FromResult(new ResultadoDistribuicao(
                137, "Nenhum DF-e localizado", ultimoNsu, ultimoNsu,
                Array.Empty<NotaDistDfe>()));
        }
    }

    private class CertificadoStoreStub : ICertificadoStore
    {
        public Task<CertificadoEnvelope> CriarEnvelopeAsync(byte[] pfx, string senha, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<X509Certificate2> CarregarAsync(Certificado certificado, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task SalvarAsync(Guid tenantId, CertificadoEnvelope envelope, CancellationToken ct) => Task.CompletedTask;
        public Task<byte[]> CifrarTextoAsync(string texto, CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
        public Task<string> DecifrarTextoAsync(byte[] dados, CancellationToken ct) => Task.FromResult(string.Empty);
    }

    private static string Chave(long numero) =>
        // cUF+AAMM+CNPJ (20) + modelo (2) + serie (3) + nNF (9) + tpEmis (1)
        // + cNF (8) + cDV (1) = 44
        $"4126091234567800019955001{numero:D9}1000123456";

    private static string ResNFe(string chave) =>
        $@"<resNFe xmlns=""http://www.portalfiscal.inf.br/nfe"" versao=""1.01"">
            <infNFe ChNFe=""{chave}"" tpNF=""1"" vNF=""100.00"">
              <CNPJ>98765432000155</CNPJ><xNome>Fornecedor SA</xNome><dhEmi>2026-09-01T10:00:00-03:00</dhEmi>
            </infNFe></resNFe>";

    private static async Task<FiscalDbContext> NovoDbContextAsync()
    {
        var conn = new SqliteConnection($"Data Source=file:regressao-{Guid.NewGuid():N}?mode=memory&cache=shared");
        await conn.OpenAsync();
        var db = new FiscalDbContext(new DbContextOptionsBuilder<FiscalDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static SincronizarDistribuicaoDFeJob NovoJob(
        FiscalDbContext db, IConsultaDistribuicaoDfe consulta)
    {
        // Sandbox é por tenant (Tenant.Sandbox): o mock é escolhido quando o
        // tenant criado abaixo está em sandbox (default true).
        return new SincronizarDistribuicaoDFeJob(
            db, new RepositorioNotaRecebida(db), new RepositorioNsu(db), new RepositorioCertificado(db),
            new CertificadoStoreStub(), new IConsultaDistribuicaoDfe[] { consulta },
            NullLogger<SincronizarDistribuicaoDFeJob>.Instance);
    }

    [Fact]
    public async Task DFe_com_mais_de_50_notas_consome_todas_as_paginas_e_salva_UltNSU()
    {
        // 100 notas em 2 páginas: o cursor da 2ª consulta DEVE ser o UltNSU da
        // 1ª resposta (000...050) — regressão do cursor saltando para o MaxNSU.
        var pag1 = new ResultadoDistribuicao(138, "Localizado",
            "000000000000050", "000000000000100",
            Enumerable.Range(1, 50).Select(i => new NotaDistDfe(
                $"000000000000{i:D3}", "resNFe_v1.00.xsd", ResNFe(Chave(i)))).ToList());
        var pag2 = new ResultadoDistribuicao(138, "Localizado",
            "000000000000100", "000000000000100",
            Enumerable.Range(51, 50).Select(i => new NotaDistDfe(
                $"000000000000{i:D3}", "resNFe_v1.00.xsd", ResNFe(Chave(i)))).ToList());

        var fake = new ConsultaMultiPagina(pag1, pag2);
        await using var db = await NovoDbContextAsync();
        var tenant = new Tenant { Cnpj = "12345678000199", RazaoSocial = "Tenant DFe", Uf = "PR", Ativo = true };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await NovoJob(db, fake).ExecutarAsync(CancellationToken.None);

        // Todas as 100 notas arquivadas — nada pulado.
        var notas = await db.NotasRecebidas.Where(n => n.TenantId == tenant.Id).ToListAsync();
        notas.Should().HaveCount(100, "as 2 páginas (50+50) devem ser consumidas no mesmo ciclo");

        // A 2ª consulta partiu do UltNSU da 1ª (não do MaxNSU da base).
        fake.NsuConsultados[0].Should().Be("000000000000000");
        fake.NsuConsultados[1].Should().Be("000000000000050",
            "a próxima página começa no último NSU entregue, não no MaxNSU");
        var nsu = await db.Set<NsuDistribuicao>().SingleAsync();
        nsu.UltimoNsu.Should().Be("000000000000100");
    }

    [Fact]
    public async Task DFe_documento_sem_chave_extrai_vel_nao_trava_o_cursor()
    {
        // XML imprestável: nota não arquivada (perda alta, logada) mas o cursor
        // avança — um resNFe quebrado não pode travar a sincronização inteira.
        var fake = new ConsultaMultiPagina(new ResultadoDistribuicao(
            138, "Localizado", "000000000000005", "000000000000005",
            new[]
            {
                new NotaDistDfe("000000000000001", "resNFe_v1.00.xsd", "<lixo/>"),
                new NotaDistDfe("000000000000002", "resNFe_v1.00.xsd",
                    ResNFe(Chave(1))),
            }));

        await using var db = await NovoDbContextAsync();
        var tenant = new Tenant { Cnpj = "12345678000199", RazaoSocial = "Tenant DFe", Uf = "PR", Ativo = true };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        await NovoJob(db, fake).ExecutarAsync(CancellationToken.None);

        var notas = await db.NotasRecebidas.Where(n => n.TenantId == tenant.Id).ToListAsync();
        notas.Should().ContainSingle("o XML sem chave não pode ser arquivado");
        var nsu = await db.Set<NsuDistribuicao>().SingleAsync();
        nsu.UltimoNsu.Should().Be("000000000000005", "o cursor avança mesmo com documento imprestável");
    }

    // --- Concorrência: status nunca regride / janela SVC -----------------------

    private async Task<Guid> CriarDocumentoAutorizadoAsync(HttpClient client)
    {
        var idem = $"regressao-{Guid.NewGuid()}";
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
        req.Headers.Add("Idempotency-Key", idem);
        req.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
            await job.ExecutarAsync(id, CancellationToken.None);
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FindAsync(id);
            doc!.Status.Should().Be(StatusDocumento.AUTORIZADA);
        }
        return id;
    }

    [Fact]
    public async Task Job_de_emissao_nao_regride_documento_AUTORIZADA()
    {
        // Claim atômico: documento em status terminal não é retransmitido
        // (regressão da corrida que sobrescrevia AUTORIZADA com REJEITADA).
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);
        var id = await CriarDocumentoAutorizadoAsync(client);

        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
        await job.ExecutarAsync(id, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var doc = await db.DocumentosFiscais.FindAsync(id);
        doc!.Status.Should().Be(StatusDocumento.AUTORIZADA, "status terminal não pode regredir");
        doc.Tentativas.Should().Be(1, "a execução abortou antes de contar tentativa");
    }

    [Fact]
    public async Task Documento_fora_da_janela_SVC_de_168h_vai_para_FALHA_EMISSAO()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);
        var id = await CriarDocumentoAutorizadoAsync(client);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var doc = await db.DocumentosFiscais.FindAsync(id);
        doc!.Status = StatusDocumento.CONTINGENCIA;
        doc.ModoContingencia = "SVCAN";
        doc.CriadoEm = DateTimeOffset.UtcNow - TimeSpan.FromHours(200); // fora da janela
        await db.SaveChangesAsync();

        var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
        await job.ExecutarAsync(id, CancellationToken.None);

        await db.Entry(doc).ReloadAsync();
        doc.Status.Should().Be(StatusDocumento.FALHA_EMISSAO,
            "janela SVC de 168h expirada → terminal, sem retransmissão em loop");
        doc.MotivoStatus.Should().Contain("SVCAN");
    }
}

/// <summary>
/// Regressão C1 da auditoria: cancelamento via painel admin precisa ser
/// transmitido — o varredor resgata eventos PENDENTES sem agenda e o job
/// conclui (doc → CANCELADA). Lockout de bruta força no login admin também.
/// </summary>
[Collection("fiscal-db")]
public class RegressaoAdminCancelamentoTests : IClassFixture<AdminIntegracaoTests.Factory>
{
    private readonly AdminIntegracaoTests.Factory _factory;

    public RegressaoAdminCancelamentoTests(AdminIntegracaoTests.Factory factory) => _factory = factory;

    private async Task<string> LoginAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/v1/admin/auth/login",
            new { email = AdminIntegracaoTests.Factory.AdminEmail, senha = AdminIntegracaoTests.Factory.AdminSenha });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Cancelamento_via_painel_admin_e_transmitido_ate_CANCELADA()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        // Documento AUTORIZADO.
        var idem = $"admincanc-{Guid.NewGuid()}";
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
        req.Headers.Add("Idempotency-Key", idem);
        req.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var docId = body.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
            await job.ExecutarAsync(docId, CancellationToken.None);
        }

        // Cancelamento via painel (JWT admin).
        var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(admin));
        var cancReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/admin/documentos-fiscais/{docId}/cancelamento")
        {
            Content = JsonContent.Create(new { justificativa = "Cancelamento via painel admin (teste regressão)" })
        };
        var cancResp = await admin.SendAsync(cancReq);
        cancResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Redes de segurança: mesmo que o enqueue se perca, o varredor resgata
        // o evento PENDENTE sem agenda e a transmissão conclui.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            // VarrerEventosJob não tem registro explícito no DI — ativa via
            // ActivatorUtilities (mesmo caminho do Hangfire).
            var varredor = Microsoft.Extensions.DependencyInjection.ActivatorUtilities
                .CreateInstance<VarrerEventosJob>(scope.ServiceProvider);
            await varredor.ExecutarAsync(CancellationToken.None);

            var evento = await db.EventosFiscais
                .SingleAsync(e => e.DocumentoId == docId && e.TipoEvento == "CANCELAMENTO");

            var job = scope.ServiceProvider.GetRequiredService<ProcessarEventoJob>();
            await job.ExecutarAsync(evento.Id, CancellationToken.None);

            await db.Entry(evento).ReloadAsync();
            var doc = await db.DocumentosFiscais.FindAsync(docId);
            evento.Status.Should().Be("PROCESSADO", because: "motivo={0}", evento.MotivoStatus);
            doc!.Status.Should().Be(StatusDocumento.CANCELADA,
                "o cancelamento solicitado no painel precisa chegar à SEFAZ");
        }
    }

    [Fact]
    public async Task Login_com_5_falhas_seguidas_ativa_lockout()
    {
        var client = _factory.CreateClient();
        var email = $"bruteforce-{Guid.NewGuid():N}@test.local";

        for (var i = 0; i < 5; i++)
        {
            var falha = await client.PostAsJsonAsync("/v1/admin/auth/login",
                new { email, senha = "errada" });
            falha.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var bloqueado = await client.PostAsJsonAsync("/v1/admin/auth/login",
            new { email, senha = "qualquer" });
        bloqueado.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

/// <summary>SSRF guard do despachante de webhook — redes reservadas bloqueadas.</summary>
public class GuardaSsrfTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.9", true)]
    [InlineData("172.32.0.9", false)] // fora de 172.16/12
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", true)]
    public void Enderecos_reservados_sao_detectados(string endereco, bool esperado)
    {
        DespachanteWebhookHttp.EhEnderecoReservado(IPAddress.Parse(endereco)).Should().Be(esperado);
    }
}
