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
/// Endpoints admin de PDF e eventos de documento. O PDF de um documento que
/// não está AUTORIZADA/CANCELADA precisa ser 409 — mesma regra do endpoint
/// de tenant; devolver um PDF em branco enganaria o operador.
/// </summary>
[Collection("fiscal-db")]
public class AdminDocumentosPdfEventosTests : IClassFixture<AdminIntegracaoTests.Factory>
{
    private readonly AdminIntegracaoTests.Factory _factory;

    public AdminDocumentosPdfEventosTests(AdminIntegracaoTests.Factory factory) => _factory = factory;

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

    /// <summary>Payload de emissão completo — o gerador de PDF o deserializa.</summary>
    private const string PayloadValido = """
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

    /// <summary>Cria um tenant + documento com o status pedido, direto no banco.</summary>
    private async Task<Guid> CriarDocumentoAsync(StatusDocumento status, string? xmlAssinado = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();

        var tenant = new Tenant
        {
            Cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10))),
            RazaoSocial = "Tenant PDF LTDA",
            Uf = "PR",
            RegimeTributario = 3,
            AmbientePadrao = (short)Ambiente.Homologacao,
            Sandbox = true,
            Ativo = true,
        };
        db.Tenants.Add(tenant);

        var doc = new DocumentoFiscal
        {
            TenantId = tenant.Id,
            IdempotencyKey = Guid.NewGuid().ToString(),
            Tipo = TipoDocumento.NFE,
            Modelo = 55,
            Serie = 1,
            Numero = Random.Shared.Next(1, 999_999),
            Ambiente = (short)Ambiente.Homologacao,
            Status = status,
            ChaveAcesso = "35240800000000000191550010000000011000000018",
            ProtocoloAutorizacao = "135240000000001",
            PayloadEntrada = PayloadValido,
            XmlAssinado = xmlAssinado,
        };
        db.DocumentosFiscais.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    [Fact]
    public async Task Pdf_sem_token_retorna_401()
    {
        var id = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = _factory.CreateClient();

        var resp = await client.GetAsync($"/v1/admin/documentos-fiscais/{id}/pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pdf_de_documento_inexistente_retorna_404()
    {
        var client = await AdminAsync();

        var resp = await client.GetAsync($"/v1/admin/documentos-fiscais/{Guid.NewGuid()}/pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Rejeitada/pendente não têm DANFE — a SEFAZ não autorizou. 409 é a
    /// mesma resposta do endpoint de tenant; a UI usa isso para desabilitar
    /// o botão com o motivo, em vez de baixar um PDF inválido.
    /// </summary>
    [Theory]
    [InlineData(StatusDocumento.PENDENTE)]
    [InlineData(StatusDocumento.PROCESSANDO)]
    [InlineData(StatusDocumento.REJEITADA)]
    [InlineData(StatusDocumento.DENEGADA)]
    [InlineData(StatusDocumento.ERRO_INTERNO)]
    [InlineData(StatusDocumento.FALHA_EMISSAO)]
    [InlineData(StatusDocumento.CONTINGENCIA)]
    public async Task Pdf_de_documento_nao_autorizado_retorna_409(StatusDocumento status)
    {
        var id = await CriarDocumentoAsync(status);
        var client = await AdminAsync();

        var resp = await client.GetAsync($"/v1/admin/documentos-fiscais/{id}/pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Pdf_de_documento_autorizado_retorna_pdf()
    {
        var id = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = await AdminAsync();

        var resp = await client.GetAsync($"/v1/admin/documentos-fiscais/{id}/pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        // Assinatura de arquivo PDF.
        bytes.Take(4).Should().Equal("%PDF"u8.ToArray());
    }

    [Fact]
    public async Task Eventos_sem_token_retorna_401()
    {
        var id = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = _factory.CreateClient();

        var resp = await client.GetAsync($"/v1/admin/documentos-fiscais/{id}/eventos");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Eventos_de_documento_inexistente_retorna_404()
    {
        var client = await AdminAsync();

        var resp = await client.GetAsync($"/v1/admin/documentos-fiscais/{Guid.NewGuid()}/eventos");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Eventos_de_documento_sem_eventos_retorna_lista_vazia()
    {
        var id = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);
        var client = await AdminAsync();

        var resp = await client.GetAsync($"/v1/admin/documentos-fiscais/{id}/eventos");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Eventos_listam_cancelamento_e_cce_com_estado()
    {
        var docId = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FindAsync(docId);
            db.EventosFiscais.Add(new EventoFiscal
            {
                TenantId = doc!.TenantId,
                DocumentoId = docId,
                TipoEvento = "CANCELAMENTO",
                IdempotencyKey = Guid.NewGuid().ToString(),
                Justificativa = "Cancelamento solicitado pelo cliente",
                Status = "PROCESSADO",
                Protocolo = "135240000000001",
                XmlRetorno = "<retEvento/>",
            });
            db.EventosFiscais.Add(new EventoFiscal
            {
                TenantId = doc.TenantId,
                DocumentoId = docId,
                TipoEvento = "CCE",
                IdempotencyKey = Guid.NewGuid().ToString(),
                Justificativa = "Correcao de endereco",
                Status = "REJEITADO",
                MotivoStatus = "Rejeicao 999: teste",
            });
            await db.SaveChangesAsync();
        }

        var client = await AdminAsync();
        var body = await (await client.GetAsync($"/v1/admin/documentos-fiscais/{docId}/eventos"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetArrayLength().Should().Be(2);
        var cancelamento = body.EnumerateArray().Single(e => e.GetProperty("tipo").GetString() == "CANCELAMENTO");
        cancelamento.GetProperty("status").GetString().Should().Be("PROCESSADO");
        cancelamento.GetProperty("protocolo").GetString().Should().Be("135240000000001");
        // O XML não vai cru na listagem — só o indicador de disponibilidade.
        cancelamento.GetProperty("xmlDisponivel").GetBoolean().Should().BeTrue();

        var cce = body.EnumerateArray().Single(e => e.GetProperty("tipo").GetString() == "CCE");
        cce.GetProperty("status").GetString().Should().Be("REJEITADO");
        cce.GetProperty("motivo").GetString().Should().Contain("Rejeicao");
        cce.GetProperty("xmlDisponivel").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Eventos_nao_vazam_documentos_de_outro_tenant()
    {
        var docId = await CriarDocumentoAsync(StatusDocumento.AUTORIZADA);

        // Evento de OUTRO documento, de outro tenant.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var outroTenant = new Tenant
            {
                Cnpj = string.Concat(Enumerable.Range(0, 14).Select(_ => Random.Shared.Next(10))),
                RazaoSocial = "Outro Tenant Eventos LTDA",
                Uf = "SP",
                Sandbox = true,
            };
            db.Tenants.Add(outroTenant);
            var outroDoc = new DocumentoFiscal
            {
                TenantId = outroTenant.Id,
                IdempotencyKey = Guid.NewGuid().ToString(),
                Tipo = TipoDocumento.NFE,
                Status = StatusDocumento.AUTORIZADA,
                PayloadEntrada = "{}",
            };
            db.DocumentosFiscais.Add(outroDoc);
            db.EventosFiscais.Add(new EventoFiscal
            {
                TenantId = outroTenant.Id,
                DocumentoId = outroDoc.Id,
                TipoEvento = "CANCELAMENTO",
                IdempotencyKey = Guid.NewGuid().ToString(),
                Status = "PROCESSADO",
            });
            await db.SaveChangesAsync();
        }

        var client = await AdminAsync();
        var body = await (await client.GetAsync($"/v1/admin/documentos-fiscais/{docId}/eventos"))
            .Content.ReadFromJsonAsync<JsonElement>();

        body.GetArrayLength().Should().Be(0);
    }
}