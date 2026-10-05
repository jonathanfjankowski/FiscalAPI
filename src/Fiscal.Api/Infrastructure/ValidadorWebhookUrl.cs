namespace Fiscal.Api.Infrastructure;

/// <summary>
/// Validação de webhookUrl informada por tenant/admin. Defesa na borda —
/// o Worker bloqueia redes privadas na conexão (DespachanteWebhookHttp),
/// independentemente do que foi cadastrado.
/// </summary>
public static class ValidadorWebhookUrl
{
    /// <summary>
    /// Null se válida; senão, mensagem do problema. Exige URL absoluta http(s);
    /// em produção (ModoSandbox=false) exige https — o segredo HMAC sai no corpo
    /// da entrega e em texto claro via http seria interceptável.
    /// </summary>
    public static string? Validar(string? url, bool sandbox)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return "Use uma URL absoluta http(s) (ex.: https://integrador.example.com/webhooks).";

        if (!sandbox && uri.Scheme != Uri.UriSchemeHttps)
            return "Em produção o webhook deve usar https:// — o segredo HMAC trafega no corpo da entrega.";

        return null;
    }
}
