using System.Text;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;

namespace Fiscal.Worker.Delivery;

/// <summary>
/// Entrega HTTP real do webhook: POST JSON com
///   X-Fiscal-Timestamp: &lt;unix seconds&gt;
///   X-Fiscal-Signature: sha256=&lt;HMAC-SHA256("timestamp.payload", secret)&gt;
/// 2xx = sucesso; qualquer outra resposta/timeout = falha (retry no job).
/// </summary>
public class DespachanteWebhookHttp : IDespachanteWebhook
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public async Task<ResultadoEntrega> EntregarAsync(
        string url, string secret, string payload, CancellationToken cancellationToken)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var assinatura = AssinadorWebhook.CalcularAssinatura(secret, timestamp, payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Fiscal-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            req.Headers.Add("X-Fiscal-Signature", $"sha256={assinatura}");

            using var resp = await Http.SendAsync(req, cancellationToken);
            var code = (int)resp.StatusCode;
            if (code is >= 200 and < 300)
                return new ResultadoEntrega(true, code, null);

            return new ResultadoEntrega(false, code, $"HTTP {code}");
        }
        catch (Exception ex)
        {
            return new ResultadoEntrega(false, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
