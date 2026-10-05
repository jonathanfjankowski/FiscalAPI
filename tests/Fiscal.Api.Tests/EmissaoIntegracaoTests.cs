using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Fiscal.Worker.Jobs;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fiscal.Api.Tests;

[Collection("fiscal-db")]
public class EmissaoIntegracaoTests : IClassFixture<EmissaoIntegracaoTests.Factory>
{
    private readonly Factory _factory;

    public EmissaoIntegracaoTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Emissao_sem_idempotency_key_retorna_400()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var resp = await client.PostAsJsonAsync("/v1/documentos-fiscais/nfe", new
        {
            ambiente = "homologacao",
            serie = 1,
            itens = new[] { new { codigo = "X", descricao = "x", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
            totais = new { valorProdutos = 10, valorNota = 10 }
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Soma_dos_itens_diferente_do_total_retorna_422_com_campo()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                itens = new[]
                {
                    new { codigo = "A", descricao = "a", quantidade = 1, valorUnitario = 50, valorTotal = 50 }
                },
                totais = new { valorProdutos = 999, valorNota = 999 }
            })
        };
        req.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid()}");

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("\"campo\"");
        body.Should().Contain("valorTotal");
    }

    [Fact]
    public async Task Emissao_aceita_retorna_202_com_status_PENDENTE_e_enfileira_job()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var idem = $"pend-{Guid.NewGuid()}";
        var req = BuildEmissaoRequest(idem);
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("PENDENTE");
        body.GetProperty("id").GetGuid().Should().NotBeEmpty();
        body.GetProperty("links").GetProperty("consulta").GetString()
            .Should().StartWith("/v1/documentos-fiscais/");
    }

    [Fact]
    public async Task Reenvio_de_rejeitada_mantem_numero_e_processa()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        // 1) Emissão aceita (número reservado)
        var idem = $"reenv-{Guid.NewGuid()}";
        var resp = await client.SendAsync(BuildEmissaoRequest(idem));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emit = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = emit.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var tenantId = db.DocumentosFiscais.AsNoTracking().First(d => d.Id == id).TenantId;
            // 2) Simula REJEIÇÃO da SEFAZ (mesmo número permanece)
            var doc = await db.DocumentosFiscais.FirstAsync(d => d.Id == id);
            doc.Status = StatusDocumento.REJEITADA;
            doc.MotivoStatus = "Rejeição 598: ambiente errado";
            await db.SaveChangesAsync();
        }

        // 3) Reenvio com payload corrigido
        var reenvio = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{id}/reenviar")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                destinatario = new { cnpjCpf = "12345678000199", nome = "Dest Corrigido" },
                itens = new[]
                {
                    new
                    {
                        codigo = "SKU1", descricao = "Produto Teste", ncm = "12345678", cfop = "5102",
                        quantidade = 2, valorUnitario = 60, valorTotal = 120,
                        impostos = new[]
                        {
                            new { cst = "01", baseCalculo = 120, aliquota = 1.65, valor = 1.98 }
                        }
                    }
                },
                totais = new { valorProdutos = 120, valorNota = 120 },
                pagamento = new[] { new { forma = "01", valor = 120 } }
            })
        };
        reenvio.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var respReenvio = await client.SendAsync(reenvio);
        respReenvio.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var bodyReenvio = await respReenvio.Content.ReadFromJsonAsync<JsonElement>();
        bodyReenvio.GetProperty("id").GetGuid().Should().Be(id, "reenvio é o MESMO documento");

        // 4) Job processa → AUTORIZADA com o MESMO número da rejeitada
        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
            await job.ExecutarAsync(id, CancellationToken.None);

            var db2 = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db2.DocumentosFiscais.FirstAsync(d => d.Id == id);
            doc.Status.Should().Be(StatusDocumento.AUTORIZADA, "motivo: {0}", doc.MotivoStatus);
            doc.Numero.Should().BeGreaterThan(0);
            doc.MotivoStatus.Should().NotContain("598");
            var payload = doc.PayloadEntrada;
            payload.Should().Contain("Dest Corrigido", "payload foi substituído");
        }
    }

    [Fact]
    public async Task Reenvio_de_autorizada_retorna_409()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var resp = await client.SendAsync(BuildEmissaoRequest($"reenv-ok-{Guid.NewGuid()}"));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emit = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = emit.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FirstAsync(d => d.Id == id);
            doc.Status = StatusDocumento.AUTORIZADA;
            await db.SaveChangesAsync();
        }

        var reenvio = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{id}/reenviar")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                itens = new[] { new { codigo = "X", descricao = "x", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
                totais = new { valorProdutos = 10, valorNota = 10 }
            })
        };
        reenvio.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var respReenvio = await client.SendAsync(reenvio);
        respReenvio.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reenvio_com_payload_invalido_retorna_422()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var resp = await client.SendAsync(BuildEmissaoRequest($"reenv-inv-{Guid.NewGuid()}"));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var emit = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = emit.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FirstAsync(d => d.Id == id);
            doc.Status = StatusDocumento.REJEITADA;
            await db.SaveChangesAsync();
        }

        var reenvio = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{id}/reenviar")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                itens = new[] { new { codigo = "X", descricao = "x", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
                totais = new { valorProdutos = 999, valorNota = 999 }
            })
        };
        reenvio.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var respReenvio = await client.SendAsync(reenvio);
        respReenvio.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Idempotencia_retorna_mesmo_documento_em_segunda_chamada_com_200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var idem = $"idem-{Guid.NewGuid()}";
        var req = BuildEmissaoRequest(idem);

        var resp1 = await client.SendAsync(req);
        resp1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        var id1 = body1.GetProperty("id").GetGuid();

        // Segunda chamada com mesma Idempotency-Key: 200 (replay) com mesmo id.
        var req2 = BuildEmissaoRequest(idem);
        var resp2 = await client.SendAsync(req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        var id2 = body2.GetProperty("id").GetGuid();

        id2.Should().Be(id1);
    }

    [Fact]
    public async Task Ambiente_da_request_diferente_do_ambiente_da_key_retorna_403()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "producao",
                serie = 1,
                itens = new[] { new { codigo = "X", descricao = "x", quantidade = 1, valorUnitario = 10, valorTotal = 10 } },
                totais = new { valorProdutos = 10, valorNota = 10 }
            })
        };
        req.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid()}");
        req.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Quando_job_e_executado_em_sandbox_sem_certificado_documento_transita_para_AUTORIZADA()
    {
        // Este teste assume sandbox (EmissorMock) e NENHUM certificado no tenant —
        // prova que o sandbox não exige certificado. Cria o documento via API,
        // captura o id, e executa o ProcessarDocumentoJob inline.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var idem = $"job-{Guid.NewGuid()}";
        var resp = await client.SendAsync(BuildEmissaoRequest(idem));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        // Executa o job in-process — Hangfire não roda dentro do WebApplicationFactory.
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
        await job.ExecutarAsync(id, CancellationToken.None);

        // Reconsulta o documento.
        var get = await client.GetAsync($"/v1/documentos-fiscais/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var docBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        docBody.GetProperty("status").GetString().Should().Be("AUTORIZADA");
        docBody.GetProperty("chaveAcesso").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Cancelamento_de_documento_nao_autorizado_retorna_409()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        // Cria um documento (fica PENDENTE, não AUTORIZADA).
        var idem = $"canc-{Guid.NewGuid()}";
        var resp = await client.SendAsync(BuildEmissaoRequest(idem));
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        var cancReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{id}/cancelamento")
        {
            Content = JsonContent.Create(new { justificativa = "Teste de cancelamento indevido" })
        };
        cancReq.Headers.Add("Idempotency-Key", $"canc-key-{Guid.NewGuid()}");

        var cancResp = await client.SendAsync(cancReq);
        cancResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancelamento_autorizado_transita_para_CANCELADA_via_job()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        // Cria documento e força AUTORIZADA com chave/protocolo (como se o
        // emissor tivesse autorizado) — requisito do evento de cancelamento.
        var idem = $"cancjob-{Guid.NewGuid()}";
        var resp = await client.SendAsync(BuildEmissaoRequest(idem));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var docId = body.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FindAsync(docId);
            doc!.Status = StatusDocumento.AUTORIZADA;
            doc.ChaveAcesso = new string('1', 44);
            doc.ProtocoloAutorizacao = "135000000000000";
            await db.SaveChangesAsync();
        }

        var cancReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{docId}/cancelamento")
        {
            Content = JsonContent.Create(new { justificativa = "Cancelamento via teste de integração do job" })
        };
        cancReq.Headers.Add("Idempotency-Key", $"cancjob-key-{Guid.NewGuid()}");
        var cancResp = await client.SendAsync(cancReq);
        cancResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Processa o evento in-process (Hangfire não roda no WebApplicationFactory;
        // em Testing o transmissor mock responde PROCESSADO).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var evento = await db.EventosFiscais
                .SingleAsync(e => e.DocumentoId == docId && e.TipoEvento == "CANCELAMENTO");

            var job = scope.ServiceProvider.GetRequiredService<Fiscal.Worker.Jobs.ProcessarEventoJob>();
            await job.ExecutarAsync(evento.Id, CancellationToken.None);

            db.Entry(evento).Reload();
            var doc = await db.DocumentosFiscais.FindAsync(docId);

            evento.Status.Should().Be("PROCESSADO", because: "motivoStatus={0}", evento.MotivoStatus);
            evento.Protocolo.Should().NotBeNullOrEmpty();
            doc!.Status.Should().Be(StatusDocumento.CANCELADA);
        }
    }

    [Fact]
    public async Task Xml_do_evento_disponivel_apos_processamento_e_no_replay()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        // Cria documento e força AUTORIZADA (requisito da CC-e).
        var idem = $"xmlev-{Guid.NewGuid()}";
        var resp = await client.SendAsync(BuildEmissaoRequest(idem));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var docId = body.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FindAsync(docId);
            doc!.Status = StatusDocumento.AUTORIZADA;
            doc.ChaveAcesso = new string('3', 44);
            doc.ProtocoloAutorizacao = "135000000000002";
            await db.SaveChangesAsync();
        }

        // CC-e: aceite imediato (PENDENTE) — xml ainda não existe.
        var cceKey = $"xmlev-cce-{Guid.NewGuid()}";
        var cceReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{docId}/carta-correcao")
        {
            Content = JsonContent.Create(new { correcao = "Correcao via teste de integracao do XML do evento" })
        };
        cceReq.Headers.Add("Idempotency-Key", cceKey);
        var cceResp = await client.SendAsync(cceReq);
        cceResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cceBody = await cceResp.Content.ReadFromJsonAsync<JsonElement>();
        var eventoId = cceBody.GetProperty("eventoId").GetGuid();
        cceBody.GetProperty("status").GetString().Should().Be("PENDENTE");
        cceBody.GetProperty("xml").ValueKind.Should().Be(JsonValueKind.Null);

        // Antes do processamento: XML protocolado não existe → 409.
        var antes = await client.GetAsync($"/v1/documentos-fiscais/{docId}/eventos/{eventoId}/xml");
        antes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Processa o evento in-process (mock responde PROCESSADO com XmlRetorno).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var job = scope.ServiceProvider.GetRequiredService<Fiscal.Worker.Jobs.ProcessarEventoJob>();
            await job.ExecutarAsync(eventoId, CancellationToken.None);

            var evento = await db.EventosFiscais.SingleAsync(e => e.Id == eventoId);
            evento.Status.Should().Be("PROCESSADO", because: "motivoStatus={0}", evento.MotivoStatus);
            evento.XmlRetorno.Should().NotBeNullOrEmpty();
        }

        // Após o processamento: XML binário com o retorno protocolado.
        var depois = await client.GetAsync($"/v1/documentos-fiscais/{docId}/eventos/{eventoId}/xml");
        depois.StatusCode.Should().Be(HttpStatusCode.OK);
        depois.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        var xml = await depois.Content.ReadAsStringAsync();
        xml.Should().Contain("retEnvEvento").And.Contain("135");

        // Replay idempotente da CC-e (doc segue AUTORIZADA) traz o xml no corpo.
        var replayReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{docId}/carta-correcao")
        {
            Content = JsonContent.Create(new { correcao = "Correcao via teste de integracao do XML do evento" })
        };
        replayReq.Headers.Add("Idempotency-Key", cceKey);
        var replay = await client.SendAsync(replayReq);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayBody.GetProperty("status").GetString().Should().Be("PROCESSADO");
        replayBody.GetProperty("xml").GetString().Should().Contain("retEnvEvento");

        // Evento de outro documento → 404 (não vaza por id).
        var roubado = await client.GetAsync($"/v1/documentos-fiscais/{Guid.NewGuid()}/eventos/{eventoId}/xml");
        roubado.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Emissao_autorizada_grava_outbox_webhook_e_entrega()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var idem = $"hook-{Guid.NewGuid()}";
        var resp = await client.SendAsync(BuildEmissaoRequest(idem));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var docId = body.GetProperty("id").GetGuid();

        // Configura o webhook do tenant antes de rodar o job de documento.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FindAsync(docId);
            var tenant = await db.Tenants.FindAsync(doc!.TenantId);
            tenant!.WebhookUrl = "http://webhook-fake/hook";
            tenant.WebhookSecret = "segredo-teste";
            await db.SaveChangesAsync();

            // Processa o documento (mock → AUTORIZADA) — a outbox é gravada na
            // mesma SaveChanges da mudança de status.
            var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
            await job.ExecutarAsync(docId, CancellationToken.None);

            var entrega = await db.WebhooksEntrega.SingleAsync(w => w.DocumentoId == docId);
            entrega.Status.Should().Be("PENDENTE");
            entrega.TipoEvento.Should().Be(Fiscal.Core.Services.Webhooks.EventoAutorizado);
            entrega.Payload.Should().Contain("documento.autorizado");
            entrega.Payload.Should().NotContain("segredo-teste");
        }

        // Entrega com despachante fake (2xx) → ENTREGUE.
        var factory2 = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            var d = s.Single(x => x.ServiceType == typeof(IDespachanteWebhook));
            s.Remove(d);
            s.AddSingleton<IDespachanteWebhook>(new FakeDespachanteWebhook());
        }));
        using (var scope2 = factory2.Services.CreateScope())
        {
            var db2 = scope2.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var entrega = await db2.WebhooksEntrega.SingleAsync(w => w.DocumentoId == docId);

            var job = scope2.ServiceProvider.GetRequiredService<Fiscal.Worker.Jobs.ProcessarWebhookJob>();
            await job.ExecutarAsync(entrega.Id, CancellationToken.None);

            db2.Entry(entrega).Reload();
            entrega.Status.Should().Be("ENTREGUE");
            entrega.EntregueEm.Should().NotBeNull();
            entrega.UltimoStatusCode.Should().Be(200);
        }
    }

    [Fact]
    public async Task CartaCorrecao_em_NFCe_retorna_409()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        // Cria uma NFC-e e força o status AUTORIZADA direto no banco
        // (o job não roda aqui; queremos testar a regra de cobertura).
        var idem = $"cce-{Guid.NewGuid()}";
        var resp = await client.SendAsync(BuildEmissaoRequest(idem, modelo: "nfce"));
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FindAsync(id);
            doc!.Status = StatusDocumento.AUTORIZADA;
            await db.SaveChangesAsync();
        }

        var cceReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{id}/carta-correcao")
        {
            Content = JsonContent.Create(new { correcao = "Endereço corrigido de forma alguma" })
        };
        cceReq.Headers.Add("Idempotency-Key", $"cce-key-{Guid.NewGuid()}");

        var cceResp = await client.SendAsync(cceReq);
        cceResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Quando_emissor_devolve_denegada_documento_transita_para_DENEGADA()
    {
        // Substitui todos os IEmissorFiscal por um fake que devolve Denegada.
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            var emissores = s.Where(d => d.ServiceType == typeof(IEmissorFiscal)).ToList();
            foreach (var d in emissores) s.Remove(d);
            s.AddSingleton<IEmissorFiscal>(new DenegadaEmissorFake());
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var idem = $"den-{Guid.NewGuid()}";
        var resp = await client.SendAsync(BuildEmissaoRequest(idem));
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
        await job.ExecutarAsync(id, CancellationToken.None);

        var get = await client.GetAsync($"/v1/documentos-fiscais/{id}");
        var docBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        docBody.GetProperty("status").GetString().Should().Be("DENEGADA");
    }

    [Fact]
    public async Task Inutilizacao_aceita_retorna_202_e_consulta_por_evento_id_retorna_200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/inutilizacoes")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                modelo = 55,
                serie = 1,
                numeroInicial = 10,
                numeroFinal = 12,
                justificativa = "Numeracao pulada por falha de rede no balcao"
            })
        };
        req.Headers.Add("Idempotency-Key", $"inut-{Guid.NewGuid()}");

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var eventoId = body.GetProperty("eventoId").GetGuid();
        body.GetProperty("status").GetString().Should().Be("PENDENTE");

        // Consulta do evento pelo Location do 202.
        var get = await client.GetAsync($"/v1/inutilizacoes/{eventoId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        getBody.GetProperty("eventoId").GetGuid().Should().Be(eventoId);
        getBody.GetProperty("tipo").GetString().Should().Be("INUTILIZACAO");

        // Id inexistente → 404.
        var notFound = await client.GetAsync($"/v1/inutilizacoes/{Guid.NewGuid()}");
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private class DenegadaEmissorFake : IEmissorFiscal
    {
        public Task<ResultadoEmissao> EmitirAsync(
            DocumentoFiscal documento, Fiscal.Core.Entities.Tenant tenant,
            System.Security.Cryptography.X509Certificates.X509Certificate2 certificado,
            Fiscal.Core.Enums.Ambiente ambiente, CancellationToken cancellationToken) =>
            Task.FromResult(new ResultadoEmissao(
                ResultadoEmissaoStatus.Denegada,
                ChaveAcesso: null,
                ProtocoloAutorizacao: null,
                XmlAssinado: null,
                XmlRetornoSefaz: null,
                Motivo: "Emitente denegado pela SEFAZ (teste)."));
    }

    private class FakeDespachanteWebhook : IDespachanteWebhook
    {
        public Task<ResultadoEntrega> EntregarAsync(
            string url, string secret, string payload, CancellationToken cancellationToken) =>
            Task.FromResult(new ResultadoEntrega(true, 200, null));
    }

    [Fact]
    public async Task Distribuicao_dfe_grava_nota_e_manifestacao_flui_ate_processado()
    {
        const string chave = "41260912345678000199550010000001001000123456";
        var resumo = $@"<resNFe xmlns=""http://www.portalfiscal.inf.br/nfe"" versao=""1.01"">
            <infNFe ChNFe=""{chave}"" tpNF=""1"" vNF=""250.00"">
              <CNPJ>98765432000155</CNPJ><xNome>Fornecedor SA</xNome><dhEmi>2026-09-01T10:00:00-03:00</dhEmi>
            </infNFe></resNFe>";

        // Consulta DFe fake que devolve 1 nota (substitui o mock do sandbox).
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            var consultas = s.Where(x => x.ServiceType == typeof(IConsultaDistribuicaoDfe)).ToList();
            foreach (var d in consultas) s.Remove(d);
            s.AddSingleton<IConsultaDistribuicaoDfe>(new ConsultaFake(new[]
            {
                new NotaDistDfe("000000000000001", "resNFe_v1.00.xsd", resumo),
            }));
        }));

        Guid tenantId, notaId;
        using (var scope = factory.Services.CreateScope())
        {
            // Tenant do bootstrap (o banco compartilhado tem vários tenants —
            // todas as queries do teste são escopadas a ele).
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var prefixoBusca = _factory.BootstrapKey[..12];
            tenantId = await db.ApiKeys
                .Where(a => a.Prefixo == prefixoBusca)
                .Select(a => a.TenantId)
                .SingleAsync();
            var tenant = await db.Tenants.FindAsync(tenantId);
            tenant!.WebhookUrl = "http://webhook-fake/hook";
            tenant.WebhookSecret = "segredo-teste";
            await db.SaveChangesAsync();

            var sync = scope.ServiceProvider.GetRequiredService<Fiscal.Worker.Jobs.SincronizarDistribuicaoDFeJob>();
            await sync.ExecutarAsync(CancellationToken.None);

            var nota = await db.NotasRecebidas.SingleAsync(n => n.Chave == chave && n.TenantId == tenantId);
            notaId = nota.Id;
            nota.Nsu.Should().Be("000000000000001");
            nota.CnpjEmitente.Should().Be("98765432000155");
            nota.Valor.Should().Be(250m);

            var webhook = await db.WebhooksEntrega.SingleAsync(w => w.TipoEvento == "nota.recebida" && w.TenantId == tenantId);
            webhook.Payload.Should().Contain(chave[..10]);
        }

        // Manifesta a nota (ciência) — transmissor mock → PROCESSADO + webhook.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);



        var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/notas-recebidas/{notaId}/manifestacao")
        {
            Content = JsonContent.Create(new { tipo = "210210" })
        };
        req.Headers.Add("Idempotency-Key", $"manif-{Guid.NewGuid()}");
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var manifBody = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var manifId = manifBody.GetProperty("manifestacaoId").GetGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var manif = await db.Manifestacoes.FindAsync(manifId);
            var job = scope.ServiceProvider.GetRequiredService<Fiscal.Worker.Jobs.ProcessarManifestacaoJob>();
            await job.ExecutarAsync(manif!.Id, CancellationToken.None);

            db.Entry(manif).Reload();
            manif.Status.Should().Be("PROCESSADO");
            manif.Protocolo.Should().NotBeNullOrEmpty();

            var nota = await db.NotasRecebidas.FindAsync(notaId);
            nota!.ManifestacaoAtual.Should().Be("ciencia");

            var webhook = await db.WebhooksEntrega
                .SingleAsync(w => w.TipoEvento == "manifestacao.processada" && w.TenantId == tenantId);
            webhook.Payload.Should().Contain("210210");
        }
    }

    [Fact]
    public async Task Status_servico_sandbox_responde_livre_e_alerta_certificado_gera_webhook()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        // Status de serviço: mock do sandbox → cStat 107 (livre).
        var resp = await client.GetAsync("/v1/status-servico?modelo=55&ambiente=homologacao");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("cStat").GetInt32().Should().Be(107);

        // Alerta de certificado vencendo: tenant ganha webhook + certificado
        // com validade em 5 dias → outbox recebe certificado.vencendo.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var tenantId = await db.ApiKeys.Select(a => a.TenantId).FirstAsync();
            var tenant = await db.Tenants.FindAsync(tenantId);
            tenant!.WebhookUrl = "http://webhook-fake/hook";
            db.Certificados.Add(new Certificado
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PfxCriptografado = new byte[] { 0x01 },
                SenhaCriptografada = new byte[] { 0x02 },
                ChaveDekCriptografada = new byte[] { 0x03 },
                Thumbprint = "VENCENDO_" + Guid.NewGuid().ToString("N")[..10],
                ValidoAte = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                Ativo = true,
                CriadoEm = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            var job = scope.ServiceProvider.GetRequiredService<Fiscal.Worker.Jobs.AlertarCertificadosVencendoJob>();
            await job.ExecutarAsync(CancellationToken.None);

            var webhook = await db.WebhooksEntrega
                .SingleAsync(w => w.TipoEvento == "certificado.vencendo");
            webhook.Payload.Should().Contain("diasRestantes");
        }
    }

    [Fact]
    public async Task NFSe_sandbox_emite_via_job_e_cancelamento_retorna_409()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", _factory.BootstrapKey);

        // Envelope NFS-e: mesmo contrato de payload, modelo interno 115.
        var idem = $"nfse-{Guid.NewGuid()}";
        var req = BuildEmissaoRequest(idem, modelo: "nfse");
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var docId = body.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var job = scope.ServiceProvider.GetRequiredService<ProcessarDocumentoJob>();
            await job.ExecutarAsync(docId, CancellationToken.None);

            var doc = await db.DocumentosFiscais.FindAsync(docId);
            doc!.Status.Should().Be(StatusDocumento.AUTORIZADA);
            doc.Modelo.Should().Be(Fiscal.Core.ModelosDocumento.NFSeNacional);
        }

        // Cancelamento por evento 110111 não se aplica a NFS-e → 409.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
            var doc = await db.DocumentosFiscais.FindAsync(docId);
            doc!.ChaveAcesso = new string('9', 44);
            doc.ProtocoloAutorizacao = "135000000000001";
            await db.SaveChangesAsync();
        }

        var cancReq = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{docId}/cancelamento")
        {
            Content = JsonContent.Create(new { justificativa = "Cancelamento não se aplica a NFS-e" })
        };
        cancReq.Headers.Add("Idempotency-Key", $"nfse-canc-{Guid.NewGuid()}");
        var cancResp = await client.SendAsync(cancReq);
        cancResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private class ConsultaFake(Fiscal.Core.Interfaces.NotaDistDfe[] documentos) : IConsultaDistribuicaoDfe
    {
        public Task<ResultadoDistribuicao> ConsultarAsync(
            Fiscal.Core.Entities.Tenant tenant, System.Security.Cryptography.X509Certificates.X509Certificate2? certificado,
            Fiscal.Core.Enums.Ambiente ambiente, string ultimoNsu, CancellationToken cancellationToken) =>
            Task.FromResult(new ResultadoDistribuicao(138, "Localizado", ultimoNsu, "000000000000001", documentos));
    }

    private HttpRequestMessage BuildEmissaoRequest(string idemKey, string modelo = "nfe")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/documentos-fiscais/{modelo}")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                destinatario = new { cnpjCpf = "12345678000199", nome = "Dest Teste" },
                itens = new[]
                {
                    new
                    {
                        codigo = "SKU1", descricao = "Produto Teste", ncm = "12345678", cfop = "5102",
                        quantidade = 2, valorUnitario = 50, valorTotal = 100,
                        impostos = new[]
                        {
                            new { cst = "01", baseCalculo = 100, aliquota = 1.65, valor = 1.65 }
                        }
                    }
                },
                totais = new { valorProdutos = 100, valorNota = 100 },
                pagamento = new[] { new { forma = "01", valor = 100 } }
            })
        };
        req.Headers.Add("Idempotency-Key", idemKey);
        return req;
    }

    public class StubCertificadoStore : Fiscal.Core.Interfaces.ICertificadoStore
    {
        public Task<CertificadoEnvelope> CriarEnvelopeAsync(byte[] pfx, string senha, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<X509Certificate2> CarregarAsync(Certificado certificado, CancellationToken ct)
        {
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=Stub", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return Task.FromResult(cert);
        }

        public Task SalvarAsync(Guid tenantId, CertificadoEnvelope envelope, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<byte[]> CifrarTextoAsync(string texto, CancellationToken ct) =>
            Task.FromResult(System.Text.Encoding.UTF8.GetBytes(texto));

        public Task<string> DecifrarTextoAsync(byte[] dados, CancellationToken ct) =>
            Task.FromResult(System.Text.Encoding.UTF8.GetString(dados));
    }

    public class Factory : WebApplicationFactory<Fiscal.Api.Program>
    {
        public string BootstrapKey { get; } = ApiKeyAuthenticationHandler_FactoryShim.GenerateKey("homologacao");
        private readonly Guid _tenantId = Guid.NewGuid();
        private readonly string _keyHash = string.Empty;

        public Factory()
        {
            _keyHash = ApiKeyAuthenticationHandler_FactoryShim.HashKey(BootstrapKey);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = "Data Source=file::memory:?cache=shared",
                    ["Certificados:ChaveMestraKEK"] = Convert.ToBase64String(new byte[32]),
                    ["Fiscal:ModoSandbox"] = "true",
                    ["Fiscal:RateLimit:PorMinuto"] = "100000"
                });
            });

            builder.ConfigureServices(services =>
            {
                var npgsql = services.Where(d =>
                    d.ServiceType.FullName?.Contains("Npgsql") == true ||
                    d.ImplementationType?.FullName?.Contains("Npgsql") == true).ToList();
                foreach (var d in npgsql) services.Remove(d);

                var storeDescriptor = services.Single(d => d.ServiceType == typeof(ICertificadoStore));
                services.Remove(storeDescriptor);
                services.AddSingleton<ICertificadoStore>(new StubCertificadoStore());

                var bgJob = services.SingleOrDefault(d => d.ServiceType == typeof(IBackgroundJobClient));
                if (bgJob is not null) services.Remove(bgJob);
                services.AddSingleton<IBackgroundJobClient, NoopBackgroundJobClient>();

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FiscalDbContext>();
                db.Database.OpenConnection();
                db.Database.EnsureCreated();

                // Idempotente: o ConfigureWebHost pode rodar mais de uma vez no
                // lifecycle do WebApplicationFactory (a cada CreateClient se algo muda).
                if (db.Tenants.Any(t => t.Id == _tenantId)) return;

                var tenant = new Tenant
                {
                    Id = _tenantId,
                    // CNPJ único por instância do fixture: cada classe de teste
                    // cria a própria Factory sobre o MESMO SQLite compartilhado —
                    // CNPJ fixo colidia (tenants.cnpj é único).
                    Cnpj = Guid.NewGuid().ToString("N")[..14],
                    RazaoSocial = "Tenant Teste",
                    Uf = "PR",
                    CodigoMunicipioIbge = "4106902",
                    RegimeTributario = 3,
                    AmbientePadrao = (short)Ambiente.Homologacao,
                    Ativo = true,
                    CriadoEm = DateTimeOffset.UtcNow
                };
                db.Tenants.Add(tenant);

                db.ApiKeys.Add(new ApiKey
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    Prefixo = BootstrapKey[..12],
                    KeyHash = _keyHash,
                    Ambiente = (short)Ambiente.Homologacao,
                    Ativa = true,
                    CriadoEm = DateTimeOffset.UtcNow
                });

                // Sem certificado de propósito: em sandbox o job deve emitir sem
                // certificado (EmissorMock não usa o X509).

                db.SaveChanges();
            });
        }
    }

    /// <summary>
    /// Shim para acessar os métodos estáticos do ApiKeyAuthenticationHandler sem importar
    /// o tipo direto (mantém o teste desacoplado da implementação).
    /// </summary>
    internal static class ApiKeyAuthenticationHandler_FactoryShim
    {
        public static string GenerateKey(string ambiente) =>
            Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.GenerateKey(ambiente);

        public static string HashKey(string key) =>
            Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.HashKey(key);
    }

    /// <summary>
    /// IBackgroundJobClient no-op para testes — não fala com Hangfire, apenas "enfileira"
    /// retornando um id fictício. O ProcessarDocumentoJob é executado manualmente.
    /// </summary>
    internal class NoopBackgroundJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => Guid.NewGuid().ToString();
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
        public string CreateJob(Job job, IState state) => Guid.NewGuid().ToString();
        public string CreateJob<T>(Expression<Action<T>> methodCall, IState state) => Guid.NewGuid().ToString();
        public string CreateJob<T>(Expression<Func<T, Task>> methodCall, IState state) => Guid.NewGuid().ToString();
        public string CreateJob<T>(Expression<Action<T>> methodCall) => Guid.NewGuid().ToString();
        public string CreateJob<T>(Expression<Func<T, Task>> methodCall) => Guid.NewGuid().ToString();
        public string Enqueue<T>(Expression<Action<T>> methodCall) => Guid.NewGuid().ToString();
        public string Enqueue<T>(Expression<Func<T, Task>> methodCall) => Guid.NewGuid().ToString();
        public string Enqueue<T>(Expression<Action<T>> methodCall, IState state) => Guid.NewGuid().ToString();
        public string Enqueue<T>(Expression<Func<T, Task>> methodCall, IState state) => Guid.NewGuid().ToString();
        public string Enqueue<T>(Expression<Action<T>> methodCall, string queue) => Guid.NewGuid().ToString();
        public string Enqueue<T>(Expression<Func<T, Task>> methodCall, string queue) => Guid.NewGuid().ToString();
        public string Schedule<T>(Expression<Action<T>> methodCall, DateTimeOffset enqueueAt) => Guid.NewGuid().ToString();
        public string Schedule<T>(Expression<Func<T, Task>> methodCall, DateTimeOffset enqueueAt) => Guid.NewGuid().ToString();
        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay) => Guid.NewGuid().ToString();
        public string Schedule<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay) => Guid.NewGuid().ToString();
        public string AddOrUpdate<T>(string recurringJobId, Expression<Action<T>> methodCall, string cronExpression, TimeZoneInfo timeZone, string queue) => recurringJobId;
        public string AddOrUpdate<T>(string recurringJobId, Expression<Func<T, Task>> methodCall, string cronExpression, TimeZoneInfo timeZone, string queue) => recurringJobId;
        public string AddOrUpdate<T>(string recurringJobId, Expression<Action<T>> methodCall, string cronExpression, string queue) => recurringJobId;
        public string AddOrUpdate<T>(string recurringJobId, Expression<Func<T, Task>> methodCall, string cronExpression, string queue) => recurringJobId;
        public void AddOrUpdate<T>(string recurringJobId, Expression<Action<T>> methodCall, string cronExpression, TimeZoneInfo timeZone) { }
        public void AddOrUpdate<T>(string recurringJobId, Expression<Func<T, Task>> methodCall, string cronExpression, TimeZoneInfo timeZone) { }
        public void AddOrUpdate<T>(string recurringJobId, Expression<Action<T>> methodCall, string cronExpression) { }
        public void AddOrUpdate<T>(string recurringJobId, Expression<Func<T, Task>> methodCall, string cronExpression) { }
        public void TriggerJob(string jobId) { }
        public bool DeleteJob(string jobId) => true;
        public bool Delete(string jobId) => true;
    }
}
