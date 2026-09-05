using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Fiscal.Persistence;
using Fiscal.Persistence.Criptografia;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fiscal.Api.Tests;

/// <summary>
/// Testes de segurança: PBKDF2 das API keys, envelope AES-GCM dos
/// certificados e os endpoints de chaves/certificados (áreas críticas que
/// não tinham cobertura).
/// </summary>
[Collection("fiscal-db")]
public class SegurancaTests(EmissaoIntegracaoTests.Factory factory)
    : IClassFixture<EmissaoIntegracaoTests.Factory>
{
    private static X509Certificate2 CriarCertificadoTeste() =>
        CriarCertificadoTesteComChave(out _);

    private static X509Certificate2 CriarCertificadoTesteComChave(out RSA chave)
    {
        chave = RSA.Create(2048);
        var req = new CertificateRequest("CN=Seguranca Teste", chave,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    // ---------- PBKDF2 (handler de API key) ----------

    [Fact]
    public void HashKey_VerifyKey_roundtrip_pbkdf2()
    {
        var key = Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.GenerateKey("producao");
        var hash = Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.HashKey(key);

        hash.Should().StartWith("$pbkdf2-sha256$100000$");

        Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.VerifyKey(key, hash).Should().BeTrue();
        Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.VerifyKey("fk_live_chave_errada", hash).Should().BeFalse();
    }

    [Fact]
    public void VerifyKey_aceita_formato_legado_sha256_hex()
    {
        var key = "fk_test_legado";
        using var sha = SHA256.Create();
        var legado = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.VerifyKey(key, legado).Should().BeTrue();
        Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.VerifyKey("outra", legado).Should().BeFalse();
    }

    [Fact]
    public void GenerateKey_tem_prefixo_12_chars_para_lookup_e_entropia_suficiente()
    {
        var key = Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.GenerateKey("producao");

        key.Should().StartWith("fk_live_");
        key.Length.Should().BeGreaterThanOrEqualTo(36); // 12 de prefixo + >=24 chars de entropia (base64 sanitizado)
        Fiscal.Api.Authentication.ApiKeyAuthenticationHandler.PrefixoDa(key)
            .Should().Be(key[..12]).And.HaveLength(12);
    }

    // ---------- Envelope AES-GCM (certificados) ----------

    [Fact]
    public async Task EnvelopeEncryption_roundtrip_pfx_e_texto()
    {
        var kek = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Certificados:ChaveMestraKEK"] = kek })
            .Build();
        var store = new EnvelopeEncryptionService(cfg);

        using var cert = CriarCertificadoTeste();
        var pfx = cert.Export(X509ContentType.Pfx, "senha-teste");

        var envelope = await store.CriarEnvelopeAsync(pfx, "senha-teste", CancellationToken.None);
        var entidade = new Fiscal.Core.Entities.Certificado
        {
            PfxCriptografado = envelope.PfxCifrado,
            SenhaCriptografada = envelope.SenhaCifrada,
            ChaveDekCriptografada = envelope.DekCifrada,
        };

        var recarregado = await store.CarregarAsync(entidade, CancellationToken.None);
        recarregado.Thumbprint.Should().Be(cert.Thumbprint);

        // Cifrado nunca contém o material em claro; DEK por registro.
        envelope.PfxCifrado.Should().NotBeEquivalentTo(pfx);
        envelope.PfxCifrado.Length.Should().BeGreaterThan(pfx.Length); // nonce + tag

        // Texto (CSC) round-trip.
        var cifrado = await store.CifrarTextoAsync("meu-csc", CancellationToken.None);
        cifrado.Should().NotBeEquivalentTo(System.Text.Encoding.UTF8.GetBytes("meu-csc"));
        (await store.DecifrarTextoAsync(cifrado, CancellationToken.None)).Should().Be("meu-csc");
    }

    [Fact]
    public async Task EnvelopeEncryption_com_KEK_errada_falha()
    {
        var cfgA = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Certificados:ChaveMestraKEK"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) })
            .Build();
        var cfgB = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Certificados:ChaveMestraKEK"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) })
            .Build();

        var storeA = new EnvelopeEncryptionService(cfgA);
        var storeB = new EnvelopeEncryptionService(cfgB);

        var cifrado = await storeA.CifrarTextoAsync("segredo", CancellationToken.None);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => storeB.DecifrarTextoAsync(cifrado, CancellationToken.None));
    }

    // ---------- Endpoints: API keys e certificados ----------

    [Fact]
    public async Task ApiKey_criar_usar_revogar_e_perder_acesso()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", factory.BootstrapKey);

        // Cria chave nova (resposta única com a chave completa).
        var criar = await client.PostAsJsonAsync("/v1/api-keys", new { descricao = "teste seguranca", ambiente = "homologacao" });
        criar.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await criar.Content.ReadFromJsonAsync<JsonElement>();
        var novaChave = body.GetProperty("chave").GetString()!;
        body.GetProperty("prefixo").GetString()!.Should().HaveLength(12);

        // A chave nova autentica.
        var client2 = factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", novaChave);
        (await client2.GetAsync("/v1/api-keys")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Revoga pela chave principal e a nova perde acesso (401).
        var id = body.GetProperty("id").GetGuid();
        (await client.DeleteAsync($"/v1/api-keys/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client2.GetAsync("/v1/api-keys")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Certificado_upload_com_envelope_real_e_listagem()
    {
        // Factory com o EnvelopeEncryptionService REAL (o fixture usa um stub
        // de decrypt para os testes de emissão em sandbox).
        var factory2 = factory.WithWebHostBuilder(b =>
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

        var client = factory2.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", factory.BootstrapKey);

        using var cert = CriarCertificadoTesteComChave(out _);
        var pfx = cert.Export(X509ContentType.Pfx, "senha-upload");

        using var form = new MultipartFormDataContent();
        var conteudo = new ByteArrayContent(pfx);
        conteudo.Headers.ContentType = new("application/x-pkcs12");
        form.Add(conteudo, "pfx", "teste.pfx");
        form.Add(new StringContent("senha-upload"), "senha");

        var upload = await client.PostAsync("/v1/certificados", form);
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var criado = await upload.Content.ReadFromJsonAsync<JsonElement>();
        criado.GetProperty("thumbprint").GetString().Should().Be(cert.Thumbprint);

        var listar = await client.GetAsync("/v1/certificados");
        listar.StatusCode.Should().Be(HttpStatusCode.OK);
        var lista = await listar.Content.ReadFromJsonAsync<JsonElement>();
        lista.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Certificado_com_senha_errada_retorna_422()
    {
        var factory2 = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            var stub = services.Single(d => d.ServiceType == typeof(Fiscal.Core.Interfaces.ICertificadoStore));
            services.Remove(stub);
            services.AddSingleton<Fiscal.Core.Interfaces.ICertificadoStore, EnvelopeEncryptionService>();
        }));

        var client = factory2.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", factory.BootstrapKey);

        using var cert = CriarCertificadoTesteComChave(out _);
        var pfx = cert.Export(X509ContentType.Pfx, "senha-correta");

        using var form = new MultipartFormDataContent();
        var conteudo = new ByteArrayContent(pfx);
        conteudo.Headers.ContentType = new("application/x-pkcs12");
        form.Add(conteudo, "pfx", "teste.pfx");
        form.Add(new StringContent("senha-ERRADA"), "senha");

        (await client.PostAsync("/v1/certificados", form)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Idempotency_key_maior_que_100_retorna_400()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", factory.BootstrapKey);

        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/documentos-fiscais/nfe")
        {
            Content = JsonContent.Create(new
            {
                ambiente = "homologacao",
                serie = 1,
                itens = new[] { new { codigo = "X", descricao = "x", quantidade = 1, valorUnitario = 1, valorTotal = 1 } },
                totais = new { valorProdutos = 1, valorNota = 1 },
            }),
        };
        req.Headers.Add("Idempotency-Key", new string('k', 101));

        (await client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
