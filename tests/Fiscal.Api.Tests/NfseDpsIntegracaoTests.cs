using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fiscal.Core.Enums;
using Fiscal.Persistence;
using Fiscal.Worker.Jobs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fiscal.Api.Tests;

/// <summary>
/// NFS-e Nacional via DPS real: rota /nfse/dps e substituição
/// (POST {id}/substituicao). Transmissão à SEFAZ Nacional fica coberta pelo
/// mock em sandbox — a bateria de homologação é manual (certificado A1).
/// </summary>
[Collection("fiscal-db")]
public class NfseDpsIntegracaoTests(EmissaoIntegracaoTests.Factory factory)
    : IClassFixture<EmissaoIntegracaoTests.Factory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", factory.BootstrapKey);
        return client;
    }

    private static object DpsBody(string ambiente = "homologacao", short serie = 1) => new
    {
        ambiente,
        serie,
        dataCompetencia = "2026-09-05",
        tomador = new
        {
            cnpjCpf = "12345678000199",
            nome = "Tomador Teste",
            endereco = new
            {
                codigoMunicipioIbge = "3550308",
                cep = "01001000",
                logradouro = "Praça da Sé",
                numero = "1",
                bairro = "Sé",
            },
        },
        servico = new
        {
            codigoTributarioNacional = "010701",
            descricaoServico = "Desenvolvimento de software",
            codigoNbs = "112011000",
        },
        valores = new
        {
            valorServicos = 1000,
            tributacaoIssqn = 1,
            retencaoIssqn = 1,
            aliquotaIssqn = 5,
        },
        ibscbs = new
        {
            finalidade = 0,
            indicadorFinal = 1,
            codigoIndicadorOperacao = "000001",
            indicadorDestinatario = 0,
            gibbsCbs = new { cst = "101", cClassTrib = "000001" },
        },
        informacoesComplementares = "teste dps",
    };

    private HttpRequestMessage Post(string url, object body, string idem)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Add("Idempotency-Key", idem);
        return req;
    }

    private static async Task RodarJobEAutorizar(EmissaoIntegracaoTests.Factory factory, Guid documentoId)
    {
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
        await job.ExecutarAsync(documentoId, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var doc = await db.DocumentosFiscais.FindAsync(documentoId);
        doc!.Status.Should().Be(StatusDocumento.AUTORIZADA);
    }

    [Fact]
    public async Task Emissao_nfse_dps_aceita_202_e_autoriza_em_sandbox()
    {
        var client = Client();

        var resp = await client.SendAsync(Post("/v1/documentos-fiscais/nfse/dps", DpsBody(), $"dps-{Guid.NewGuid()}"));

        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted, "body: {0}", raw);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("PENDENTE");
        var id = body.GetProperty("id").GetGuid();

        await RodarJobEAutorizar(factory, id);
    }

    [Fact]
    public async Task Emissao_nfse_dps_sem_idempotency_key_retorna_400()
    {
        var client = Client();

        var resp = await client.PostAsJsonAsync("/v1/documentos-fiscais/nfse/dps", DpsBody());

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Emissao_nfse_dps_com_servico_incompleto_retorna_422()
    {
        var client = Client();
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(DpsBody()))!;
        json["servico"]!["codigoNbs"] = "123"; // passa no DataAnnotations, falha no validador declarativo

        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfse/dps")
        {
            Content = new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Idempotency-Key", $"dps-bad-{Guid.NewGuid()}");
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var conteudo = await resp.Content.ReadAsStringAsync();
        conteudo.Should().Contain("cNBS deve ter 9 dígitos");
    }

    // ---------- substituição ----------

    [Fact]
    public async Task Substituicao_de_nfse_autorizada_cria_documento_e_autoriza()
    {
        var client = Client();

        // 1) NFS-e original (rota legada, sandbox mock).
        var originalReq = Post("/v1/documentos-fiscais/nfse", new
        {
            ambiente = "homologacao",
            serie = 1,
            itens = new[] { new { codigo = "SRV", descricao = "Serviço", quantidade = 1, valorUnitario = 500, valorTotal = 500 } },
            totais = new { valorProdutos = 500, valorNota = 500 },
        }, $"orig-{Guid.NewGuid()}");
        var originalResp = await client.SendAsync(originalReq);
        originalResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var originalBody = await originalResp.Content.ReadFromJsonAsync<JsonElement>();
        var originalId = originalBody.GetProperty("id").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
            await job.ExecutarAsync(originalId, CancellationToken.None);
        }

        // 2) Substituição com DPS completo.
        var substReq = Post($"/v1/documentos-fiscais/{originalId}/substituicao", new
        {
            dps = DpsBody(),
            cMotivo = 5,
            xMotivo = "Rejeitada pelo tomador",
        }, $"subst-{Guid.NewGuid()}");

        var substResp = await client.SendAsync(substReq);

        substResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var substBody = await substResp.Content.ReadFromJsonAsync<JsonElement>();
        substBody.GetProperty("substituidaId").GetGuid().Should().Be(originalId);
        var substitutaId = substBody.GetProperty("id").GetGuid();
        substitutaId.Should().NotBe(originalId);

        // 3) Substituta autoriza em sandbox; payload carrega a chave substituída.
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
            await job.ExecutarAsync(substitutaId, CancellationToken.None);

            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var substituta = await db.DocumentosFiscais.FindAsync(substitutaId);
            substituta!.Status.Should().Be(StatusDocumento.AUTORIZADA);
            substituta.PayloadEntrada.Should().Contain("chaveSubstituida");
            substituta.Numero.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task Substituicao_de_documento_pendente_retorna_409()
    {
        var client = Client();

        var originalResp = await client.SendAsync(Post("/v1/documentos-fiscais/nfse", new
        {
            ambiente = "homologacao",
            serie = 1,
            itens = new[] { new { codigo = "SRV", descricao = "Serviço", quantidade = 1, valorUnitario = 100, valorTotal = 100 } },
            totais = new { valorProdutos = 100, valorNota = 100 },
        }, $"pend-{Guid.NewGuid()}"));
        originalResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var originalId = (await originalResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // Sem rodar o job — documento continua PENDENTE.

        var substResp = await client.SendAsync(Post(
            $"/v1/documentos-fiscais/{originalId}/substituicao", new { dps = DpsBody(), cMotivo = 5 },
            $"subst-pend-{Guid.NewGuid()}"));

        substResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Substituicao_de_nfe_retorna_422()
    {
        var client = Client();

        var nfeResp = await client.SendAsync(Post("/v1/documentos-fiscais/nfe", new
        {
            ambiente = "homologacao",
            serie = 1,
            itens = new[] { new { codigo = "X", descricao = "x", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
            totais = new { valorProdutos = 10, valorNota = 10 },
        }, $"nfe-{Guid.NewGuid()}"));
        nfeResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var nfeId = (await nfeResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var substResp = await client.SendAsync(Post(
            $"/v1/documentos-fiscais/{nfeId}/substituicao", new { dps = DpsBody(), cMotivo = 5 },
            $"subst-nfe-{Guid.NewGuid()}"));

        substResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var conteudo = await substResp.Content.ReadAsStringAsync();
        conteudo.Should().Contain("apenas para NFS-e");
    }

    [Fact]
    public async Task Substituicao_cmotivo_99_sem_xmotivo_retorna_422()
    {
        var client = Client();

        var originalResp = await client.SendAsync(Post("/v1/documentos-fiscais/nfse", new
        {
            ambiente = "homologacao",
            serie = 1,
            itens = new[] { new { codigo = "SRV", descricao = "Serviço", quantidade = 1, valorUnitario = 100, valorTotal = 100 } },
            totais = new { valorProdutos = 100, valorNota = 100 },
        }, $"m99-{Guid.NewGuid()}"));
        var originalId = (await originalResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await RodarJobEAutorizar(factory, originalId);

        var substResp = await client.SendAsync(Post(
            $"/v1/documentos-fiscais/{originalId}/substituicao", new { dps = DpsBody(), cMotivo = 99 },
            $"subst-99-{Guid.NewGuid()}"));

        substResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var conteudo = await substResp.Content.ReadAsStringAsync();
        conteudo.Should().Contain("xMotivo");
    }

}
