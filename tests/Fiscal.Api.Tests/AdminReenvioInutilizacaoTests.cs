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
/// Reenvio e inutilização pelo painel.
///
/// Duas operações que não podem ser confundidas: <b>reenvio</b> corrige o
/// payload do MESMO documento e mantém o número (a rejeição não consumiu
/// numeração na SEFAZ); <b>inutilização</b> libera uma FAIXA de números que
/// nunca foi transmitida. Trocar uma pela outra emite número errado.
/// </summary>
[Collection("fiscal-db")]
public class AdminReenvioInutilizacaoTests : IClassFixture<AdminIntegracaoTests.Factory>
{
    private readonly AdminIntegracaoTests.Factory _factory;

    public AdminReenvioInutilizacaoTests(AdminIntegracaoTests.Factory factory) => _factory = factory;

    private const string PayloadNfe = """
        {
          "ambiente": "homologacao",
          "serie": 1,
          "destinatario": { "cnpjCpf": "12345678000199", "nome": "Dest Teste",
            "endereco": { "logradouro": "Rua X", "numero": "10", "bairro": "Centro",
              "codigoMunicipioIbge": "4106902", "nomeMunicipio": "Curitiba", "uf": "PR" } },
          "itens": [ { "codigo": "SKU1", "descricao": "Produto Teste", "ncm": "12345678",
            "cfop": "5102", "quantidade": 2, "valorUnitario": 50, "valorTotal": 100,
            "impostos": [ { "cst": "00", "baseCalculo": 100, "aliquota": 18, "valor": 18 } ] } ],
          "totais": { "valorProdutos": 100, "valorNota": 100 },
          "pagamento": [ { "forma": "01", "valor": 100 } ],
          "naturezaOperacao": "VENDA DE MERCADORIA"
        }
        """;

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

    private async Task<(Guid DocId, Guid TenantId, long Numero)> CriarDocumentoAsync(
        StatusDocumento status, bool sandbox = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();

        var tenant = new Tenant
        {
            Cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10))),
            RazaoSocial = "Tenant Reenvio LTDA",
            Uf = "PR",
            RegimeTributario = 3,
            AmbientePadrao = (short)Ambiente.Homologacao,
            Sandbox = sandbox,
            Ativo = true,
        };
        db.Tenants.Add(tenant);

        var numero = Random.Shared.Next(1, 999_999);
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
            MotivoStatus = status == StatusDocumento.REJEITADA ? "Rejeicao 999: NCM inexistente" : null,
            PayloadEntrada = PayloadNfe,
        };
        db.DocumentosFiscais.Add(doc);
        await db.SaveChangesAsync();
        return (doc.Id, tenant.Id, numero);
    }

    // ---------------- Reenvio ----------------

    [Fact]
    public async Task Reenvio_sem_token_retorna_401()
    {
        var (docId, _, _) = await CriarDocumentoAsync(StatusDocumento.REJEITADA);
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/documentos-fiscais/{docId}/reenviar", JsonDocument.Parse(PayloadNfe).RootElement);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reenvio_de_documento_inexistente_retorna_404()
    {
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/documentos-fiscais/{Guid.NewGuid()}/reenviar",
            JsonDocument.Parse(PayloadNfe).RootElement);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Autorizada nunca é reenviada — é cancelada. Deixar reenviar duplicaria
    /// o documento fiscal com a mesma chave.
    /// </summary>
    [Theory]
    [InlineData(StatusDocumento.AUTORIZADA)]
    [InlineData(StatusDocumento.CANCELADA)]
    [InlineData(StatusDocumento.PENDENTE)]
    [InlineData(StatusDocumento.PROCESSANDO)]
    public async Task Reenvio_de_documento_em_status_invalido_retorna_409(StatusDocumento status)
    {
        var (docId, _, _) = await CriarDocumentoAsync(status);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/documentos-fiscais/{docId}/reenviar",
            JsonDocument.Parse(PayloadNfe).RootElement);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(StatusDocumento.REJEITADA)]
    [InlineData(StatusDocumento.DENEGADA)]
    [InlineData(StatusDocumento.ERRO_INTERNO)]
    public async Task Reenvio_de_documento_rejeitado_volta_a_pendente_mantendo_o_numero(StatusDocumento status)
    {
        var (docId, _, numeroOriginal) = await CriarDocumentoAsync(status);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/documentos-fiscais/{docId}/reenviar",
            JsonDocument.Parse(PayloadNfe).RootElement);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("PENDENTE");
        // O ponto crítico: o número NÃO muda.
        body.GetProperty("numero").GetInt64().Should().Be(numeroOriginal);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var doc = await db.DocumentosFiscais.FindAsync(docId);
        doc!.Status.Should().Be(StatusDocumento.PENDENTE);
        doc.Numero.Should().Be(numeroOriginal);
        doc.MotivoStatus.Should().BeNull();
        doc.Tentativas.Should().Be(0);
    }

    [Fact]
    public async Task Reenvio_com_payload_inconsistente_retorna_422_e_nao_altera_o_documento()
    {
        var (docId, _, numeroOriginal) = await CriarDocumentoAsync(StatusDocumento.REJEITADA);
        var client = await AdminAsync();

        // Itens somam 100, mas o total da nota diz 200.
        var payloadRuim = PayloadNfe.Replace("\"valorNota\": 100", "\"valorNota\": 200");

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/documentos-fiscais/{docId}/reenviar",
            JsonDocument.Parse(payloadRuim).RootElement);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var doc = await db.DocumentosFiscais.FindAsync(docId);
        doc!.Status.Should().Be(StatusDocumento.REJEITADA);
        doc.Numero.Should().Be(numeroOriginal);
    }

    [Fact]
    public async Task Reenvio_com_ambiente_divergente_do_documento_retorna_422()
    {
        var (docId, _, _) = await CriarDocumentoAsync(StatusDocumento.REJEITADA);
        var client = await AdminAsync();

        var payloadProducao = PayloadNfe.Replace("\"ambiente\": \"homologacao\"", "\"ambiente\": \"producao\"");

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/documentos-fiscais/{docId}/reenviar",
            JsonDocument.Parse(payloadProducao).RootElement);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------- Inutilização ----------------

    [Fact]
    public async Task Inutilizacao_sem_token_retorna_401()
    {
        var (_, tenantId, _) = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/v1/admin/inutilizacoes", new
        {
            tenantId,
            modelo = 55,
            serie = 1,
            numeroInicial = 900,
            numeroFinal = 910,
            justificativa = "Faixa nao utilizada apos rejeicao"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Inutilizacao_de_tenant_inexistente_retorna_404()
    {
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync("/v1/admin/inutilizacoes", new
        {
            tenantId = Guid.NewGuid(),
            modelo = 55,
            serie = 1,
            numeroInicial = 900,
            numeroFinal = 910,
            justificativa = "Faixa nao utilizada apos rejeicao"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Inutilizacao_com_faixa_invertida_retorna_422()
    {
        var (_, tenantId, _) = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync("/v1/admin/inutilizacoes", new
        {
            tenantId,
            modelo = 55,
            serie = 1,
            numeroInicial = 910,
            numeroFinal = 900,
            justificativa = "Faixa nao utilizada apos rejeicao"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Inutilizacao_com_modelo_invalido_retorna_422()
    {
        var (_, tenantId, _) = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync("/v1/admin/inutilizacoes", new
        {
            tenantId,
            modelo = 99,
            serie = 1,
            numeroInicial = 900,
            numeroFinal = 910,
            justificativa = "Faixa nao utilizada apos rejeicao"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Inutilizacao_com_justificativa_curta_retorna_422()
    {
        var (_, tenantId, _) = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync("/v1/admin/inutilizacoes", new
        {
            tenantId,
            modelo = 55,
            serie = 1,
            numeroInicial = 900,
            numeroFinal = 910,
            justificativa = "curta"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Inutilizacao_valida_e_aceita_e_pode_ser_consultada()
    {
        var (_, tenantId, _) = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync("/v1/admin/inutilizacoes", new
        {
            tenantId,
            modelo = 55,
            serie = 1,
            numeroInicial = 900,
            numeroFinal = 910,
            justificativa = "Faixa nao utilizada apos rejeicao de lote"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var eventoId = body.GetProperty("eventoId").GetGuid();
        body.GetProperty("status").GetString().Should().Be("PENDENTE");

        var consulta = await client.GetAsync($"/v1/admin/inutilizacoes/{eventoId}");
        consulta.StatusCode.Should().Be(HttpStatusCode.OK);
        var evento = await consulta.Content.ReadFromJsonAsync<JsonElement>();
        evento.GetProperty("tipo").GetString().Should().Be("INUTILIZACAO");
    }

    [Fact]
    public async Task Inutilizacao_deriva_o_ambiente_do_tenant()
    {
        // Tenant com ambiente padrão produção.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var tenant = new Tenant
        {
            Cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10))),
            RazaoSocial = "Tenant Inut Producao LTDA",
            Uf = "PR",
            AmbientePadrao = (short)Ambiente.Producao,
            Sandbox = true,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var client = await AdminAsync();
        var resp = await client.PostAsJsonAsync("/v1/admin/inutilizacoes", new
        {
            tenantId = tenant.Id,
            modelo = 55,
            serie = 1,
            numeroInicial = 900,
            numeroFinal = 910,
            justificativa = "Faixa nao utilizada apos rejeicao de lote"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var eventoId = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eventoId").GetGuid();

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<FiscalDbContext>();
        var evento = await db2.EventosFiscais.FindAsync(eventoId);
        evento!.DadosEvento.Should().Contain("\"Ambiente\":\"producao\"");
    }

    /// <summary>
    /// FALHA_EMISSAO esgotou as tentativas em contingência — o número nunca foi
    /// consumido na SEFAZ, então o reenvio é a saída. Sem isso o documento era
    /// um beco sem saída pelo painel (a doc mandava reenviar e a API dava 409).
    /// </summary>
    [Fact]
    public async Task Reenvio_de_falha_emissao_e_aceito()
    {
        var (docId, _, numeroOriginal) = await CriarDocumentoAsync(StatusDocumento.FALHA_EMISSAO);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/documentos-fiscais/{docId}/reenviar",
            JsonDocument.Parse(PayloadNfe).RootElement);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("numero").GetInt64().Should().Be(numeroOriginal);
    }

    /// <summary>
    /// A trilha de auditoria do reenvio precisa do IP de origem. O TestServer
    /// não tem RemoteIpAddress, então o serviço é exercitado direto com um IP
    /// conhecido — é o contrato dele que importa.
    /// </summary>
    [Fact]
    public async Task Reenvio_registra_ip_de_origem_na_auditoria()
    {
        var (docId, _, _) = await CriarDocumentoAsync(StatusDocumento.REJEITADA);
        var ip = System.Net.IPAddress.Parse("203.0.113.42");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var servico = scope.ServiceProvider
                .GetRequiredService<Fiscal.Api.Services.ServicoReenvioDocumento>();
            var doc = await db.DocumentosFiscais.FindAsync(docId);
            var req = System.Text.Json.JsonSerializer.Deserialize<Fiscal.Core.Contracts.EmissaoRequest>(
                PayloadNfe, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            var resultado = await servico.ReenviarAsync(doc!, req, apiKeyId: null, ip, CancellationToken.None);
            resultado.Ok.Should().BeTrue();
        }

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<FiscalDbContext>();
        // Sem OrderBy sobre DateTimeOffset (não traduz no SQLite — regra do repo).
        var registro = db2.Auditoria
            .Where(a => a.RecursoId == docId && a.Acao == "DOCUMENTO_REENVIADO")
            .ToList()
            .OrderByDescending(a => a.CriadoEm)
            .FirstOrDefault();

        registro.Should().NotBeNull();
        registro!.IpOrigem.Should().Be(ip);
    }

    /// <summary>
    /// O contrato de erro promete a extensão `campo` nos 422 de aritmética
    /// (docs/integracao-api.md). O refactor do reenvio não pode perdê-la.
    /// </summary>
    [Fact]
    public async Task Reenvio_com_aritmetica_invalida_devolve_extensao_campo()
    {
        var (docId, _, _) = await CriarDocumentoAsync(StatusDocumento.REJEITADA);
        var client = await AdminAsync();

        var payloadRuim = PayloadNfe.Replace("\"valorNota\": 100", "\"valorNota\": 200");
        var resp = await client.PostAsJsonAsync(
            $"/v1/admin/documentos-fiscais/{docId}/reenviar",
            JsonDocument.Parse(payloadRuim).RootElement);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("campo", out var campo).Should().BeTrue();
        campo.GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>A numeração fiscal começa em 1 — a SEFAZ rejeita faixa com 0.</summary>
    [Fact]
    public async Task Inutilizacao_com_numero_inicial_zero_retorna_422()
    {
        var (_, tenantId, _) = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = await AdminAsync();

        var resp = await client.PostAsJsonAsync("/v1/admin/inutilizacoes", new
        {
            tenantId,
            modelo = 55,
            serie = 1,
            numeroInicial = 0,
            numeroFinal = 10,
            justificativa = "Faixa nao utilizada apos rejeicao de lote"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Sem a faixa no GET o operador vê que algo foi inutilizado mas não O QUÊ.
    /// </summary>
    [Fact]
    public async Task Inutilizacao_consultada_devolve_a_faixa_e_a_justificativa()
    {
        var (_, tenantId, _) = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = await AdminAsync();

        var create = await client.PostAsJsonAsync("/v1/admin/inutilizacoes", new
        {
            tenantId,
            modelo = 65,
            serie = 2,
            numeroInicial = 500,
            numeroFinal = 520,
            justificativa = "Faixa pulada por erro de configuracao de serie"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var eventoId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eventoId").GetGuid();

        var body = await (await client.GetAsync($"/v1/admin/inutilizacoes/{eventoId}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("modelo").GetInt16().Should().Be(65);
        body.GetProperty("serie").GetInt16().Should().Be(2);
        body.GetProperty("numeroInicial").GetInt64().Should().Be(500);
        body.GetProperty("numeroFinal").GetInt64().Should().Be(520);
        body.GetProperty("justificativa").GetString().Should().Contain("erro de configuracao");
        body.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
    }

    /// <summary>Replay da mesma Idempotency-Key devolve a mesma inutilização.</summary>
    [Fact]
    public async Task Inutilizacao_com_mesma_idempotency_key_e_idempotente()
    {
        var (_, tenantId, _) = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = await AdminAsync();
        var chave = Guid.NewGuid().ToString();

        async Task<Guid> Enviar()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/inutilizacoes");
            req.Headers.Add("Idempotency-Key", chave);
            req.Content = JsonContent.Create(new
            {
                tenantId,
                modelo = 55,
                serie = 1,
                numeroInicial = 700,
                numeroFinal = 710,
                justificativa = "Faixa nao utilizada apos rejeicao de lote"
            });
            var resp = await client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
            return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eventoId").GetGuid();
        }

        var primeiro = await Enviar();
        var segundo = await Enviar();

        segundo.Should().Be(primeiro);
    }
}