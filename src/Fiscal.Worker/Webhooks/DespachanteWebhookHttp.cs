using System.Net;
using System.Net.Sockets;
using System.Text;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;

namespace Fiscal.Worker.Delivery;

/// <summary>
/// Entrega HTTP real do webhook: POST JSON com
///   X-Fiscal-Timestamp: &lt;unix seconds&gt;
///   X-Fiscal-Signature: sha256=&lt;HMAC-SHA256("timestamp.payload", secret)&gt;
/// 2xx = sucesso; qualquer outra resposta/timeout = falha (retry no job).
///
/// SSRF guard: a conexão é interceptada (ConnectCallback) e IPs privados,
/// loopback e link-local são bloqueados — o tenant nunca aponta o webhook
/// para a rede interna do host (metadata de cloud, serviços vizinhos).
/// Opt-out explícito p/ instalações on-premise: Fiscal:Webhooks:PermitirRedesPrivadas=true.
/// </summary>
public class DespachanteWebhookHttp : IDespachanteWebhook
{
    private readonly HttpClient _http;
    private readonly bool _permitirRedesPrivadas;

    public DespachanteWebhookHttp(IConfiguration configuration)
    {
        _permitirRedesPrivadas = configuration.GetValue("Fiscal:Webhooks:PermitirRedesPrivadas", false);
        _http = CriarHttpClient(_permitirRedesPrivadas);
    }

    private static HttpClient CriarHttpClient(bool permitirRedesPrivadas)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (ctx, ct) =>
            {
                var host = ctx.DnsEndPoint.Host;
                IPAddress ip;
                if (IPAddress.TryParse(host, out var literal))
                {
                    ip = literal;
                }
                else
                {
                    var enderecos = await Dns.GetHostAddressesAsync(host, ct);
                    ip = enderecos.FirstOrDefault()
                        ?? throw new SocketException((int)SocketError.HostNotFound);
                }

                if (!permitirRedesPrivadas && EhEnderecoReservado(ip))
                    throw new HttpRequestException(
                        $"Webhook bloqueado: destino {host} resolve para endereço reservado ({ip}).");

                var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(ip, ctx.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>Loopback, privadas (RFC 1918/4193), link-local, unspecified e multicast.</summary>
    public static bool EhEnderecoReservado(IPAddress ip) =>
        IPAddress.IsLoopback(ip)
        || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal
        || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)
        || (ip.AddressFamily == AddressFamily.InterNetwork && (
            PrimeiroOcteto(ip) is 0 or 10 or 127                                          // 0/8, 10/8, 127/8
            || (PrimeiroOcteto(ip) == 172 && SegundoOcteto(ip) is >= 16 and <= 31)        // 172.16/12
            || (PrimeiroOcteto(ip) == 192 && SegundoOcteto(ip) == 168)                    // 192.168/16
            || (PrimeiroOcteto(ip) == 169 && SegundoOcteto(ip) == 254)))                  // 169.254/16
        || (ip.AddressFamily == AddressFamily.InterNetworkV6
            && ip.IsIPv4MappedToIPv6
            && EhEnderecoReservado(ip.MapToIPv4()));

    private static int PrimeiroOcteto(IPAddress ip) => ip.GetAddressBytes()[0];
    private static int SegundoOcteto(IPAddress ip) => ip.GetAddressBytes()[1];

    public async Task<ResultadoEntrega> EntregarAsync(
        string url, string secret, string payload, CancellationToken cancellationToken)
    {
        try
        {
            // Bloqueio cedo de host literal reservado (o ConnectCallback cobre DNS).
            if (!_permitirRedesPrivadas &&
                Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                IPAddress.TryParse(uri.Host, out var literal) &&
                EhEnderecoReservado(literal))
            {
                return new ResultadoEntrega(false, null,
                    $"Webhook bloqueado: destino em rede reservada ({uri.Host}).");
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var assinatura = AssinadorWebhook.CalcularAssinatura(secret, timestamp, payload);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Fiscal-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            req.Headers.Add("X-Fiscal-Signature", $"sha256={assinatura}");

            using var resp = await _http.SendAsync(req, cancellationToken);
            var code = (int)resp.StatusCode;
            if (code is >= 200 and < 300)
                return new ResultadoEntrega(true, code, null);

            return new ResultadoEntrega(false, code, $"HTTP {code}");
        }
        catch (HttpRequestException ex) when (!_permitirRedesPrivadas && ex.Message.Contains("reservado"))
        {
            return new ResultadoEntrega(false, null, ex.Message);
        }
        catch (Exception ex)
        {
            return new ResultadoEntrega(false, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
