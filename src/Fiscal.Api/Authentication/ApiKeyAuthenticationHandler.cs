using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Fiscal.Api.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
}

public static class TenantContext
{
    public const string TenantIdItem = "TenantId";
    public const string ApiKeyItem = "ApiKey";
    public const string AmbienteItem = "Ambiente";

    /// <summary>
    /// Resolve o tenant da request. Falha alto se ausente — chegar aqui sem
    /// tenant resolvido significa que a rota não está [Authorize] ou que a
    /// autenticação foi pulada; em ambos os casos o request não deveria
    /// chegar aos controllers.
    /// </summary>
    public static Guid GetTenantId(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue(TenantIdItem, out var v) && v is Guid g && g != Guid.Empty)
            return g;
        throw new InvalidOperationException(
            "Tenant não resolvido no HttpContext. A rota precisa estar [Authorize] e " +
            "o ApiKeyAuthenticationHandler precisa ter rodado antes do controller.");
    }

    public static Guid? GetApiKeyId(this HttpContext ctx) =>
        ctx.Items.TryGetValue(ApiKeyItem, out var v) && v is ApiKey k ? k.Id : null;

    public static Ambiente GetAmbiente(this HttpContext ctx) =>
        ctx.Items.TryGetValue(AmbienteItem, out var v) && v is short s
            ? (Ambiente)s
            : Ambiente.Homologacao;
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IRepositorioApiKey _repo;
    private readonly IRepositorioAuditoria _auditoria;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IRepositorioApiKey repo,
        IRepositorioAuditoria auditoria)
        : base(options, logger, encoder)
    {
        _repo = repo;
        _auditoria = auditoria;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader) ||
            string.IsNullOrWhiteSpace(authHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var raw = authHeader.ToString();
        if (!raw.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var key = raw.Substring("ApiKey ".Length).Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length < 16)
            return AuthenticateResult.Fail("API Key inválida.");

        // PBKDF2 usa salt aleatório, então não dá pra buscar por hash. O prefixo
        // (12 chars desde a 1.3.1; 8 nas chaves antigas) está guardado no banco —
        // usamos pra reduzir o universo de candidatos antes do PBKDF2 (custo de
        // CPU). Sem isso, o prefixo era constante ("fk_live_") e toda request
        // verificaria TODAS as chaves ativas do ambiente.
        var candidatos = await _repo.ListarPorPrefixoAsync(key[..Math.Min(12, key.Length)], Context.RequestAborted);
        if (candidatos.Count == 0)
            candidatos = await _repo.ListarPorPrefixoAsync(key[..8], Context.RequestAborted);
        var apiKey = candidatos.FirstOrDefault(k => VerifyKey(key, k.KeyHash));
        if (apiKey is null || !apiKey.Ativa || apiKey.RevogadoEm is not null)
        {
            // Auditoria de falha (sem tenant).
            await _auditoria.RegistrarAsync(new Auditoria
            {
                Acao = "API_KEY_INVALIDA",
                IpOrigem = Context.Connection.RemoteIpAddress,
                Detalhe = "{\"prefixo\":\"" + (key.Length >= 4 ? key[..4] : "???") + "\"}"
            }, Context.RequestAborted);
            return AuthenticateResult.Fail("API Key inválida ou revogada.");
        }

        // Resolve ambiente do header (se houver) e valida match com o ambiente da chave.
        var ambienteHeader = Context.Request.Headers["X-Fiscal-Ambiente"].ToString();
        if (!string.IsNullOrEmpty(ambienteHeader))
        {
            if (!ambienteHeader.Equals(apiKey.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
                    StringComparison.OrdinalIgnoreCase))
            {
                await _auditoria.RegistrarAsync(new Auditoria
                {
                    TenantId = apiKey.TenantId,
                    ApiKeyId = apiKey.Id,
                    Acao = "API_KEY_AMBIENTE_DIVERGENTE",
                    IpOrigem = Context.Connection.RemoteIpAddress
                }, Context.RequestAborted);
                return AuthenticateResult.Fail("API Key não autorizada para o ambiente solicitado.");
            }
        }

        // Seta contexto para uso nos controllers.
        Context.Items[TenantContext.TenantIdItem] = apiKey.TenantId;
        Context.Items[TenantContext.ApiKeyItem] = apiKey;
        Context.Items[TenantContext.AmbienteItem] = apiKey.Ambiente;

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, apiKey.TenantId.ToString()),
            new Claim("api_key_id", apiKey.Id.ToString())
        }, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    public static string HashKey(string key)
    {
        // PBKDF2-SHA256 com salt aleatório. Formato: $pbkdf2-sha256$iter$salt$hash
        // (compatível com libs comuns). Iterações calibradas para ~100ms em hardware típico.
        const int iterations = 100_000;
        const int saltLen = 16;
        const int hashLen = 32;

        var salt = RandomNumberGenerator.GetBytes(saltLen);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(key),
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: hashLen);

        return $"$pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyKey(string key, string stored)
    {
        // Suporta tanto o novo formato PBKDF2 quanto o legado SHA-256 (hex 64 chars).
        if (stored.StartsWith("$pbkdf2-sha256$", StringComparison.Ordinal))
        {
            var parts = stored.Split('$');
            if (parts.Length != 5) return false;
            if (!int.TryParse(parts[2], out var iter)) return false;
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password: Encoding.UTF8.GetBytes(key),
                salt: salt,
                iterations: iter,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        // Legado: SHA-256 hex puro (64 chars).
        if (stored.Length == 64)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(stored),
                bytes);
        }

        return false;
    }

    public static string GenerateKey(string ambiente)
    {
        // 12 chars de prefixo (fk_live_/fk_test_ + 4 aleatórios): o lookup por
        // prefixo precisa apontar para (quase) uma única chave — ver migration
        // ApiKeyPrefixo16. 24 bytes de entropia no resto.
        var prefixo = ambiente == "producao" ? "fk_live_" : "fk_test_";
        var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace("+", "").Replace("/", "").Replace("=", "");
        return prefixo + random;
    }

    /// <summary>Prefixo guardado no banco para uma chave gerada (12 chars).</summary>
    public static string PrefixoDa(string key) => key[..Math.Min(12, key.Length)];
}
