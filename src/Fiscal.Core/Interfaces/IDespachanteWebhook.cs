namespace Fiscal.Core.Interfaces;

public record ResultadoEntrega(bool Sucesso, int? StatusCode, string? Erro);

/// <summary>
/// Entrega um payload de webhook (POST HTTP) com assinatura HMAC.
/// Implementação real: DespachanteWebhookHttp (Fiscal.Worker). Testes podem
/// injetar um fake.
/// </summary>
public interface IDespachanteWebhook
{
    Task<ResultadoEntrega> EntregarAsync(string url, string secret, string payload, CancellationToken cancellationToken);
}
