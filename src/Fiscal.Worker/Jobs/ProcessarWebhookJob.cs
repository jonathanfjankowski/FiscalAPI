using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Entrega uma linha da outbox de webhooks ao tenant (POST assinado com
/// HMAC-SHA256). Retry com backoff próprio (separado do retry de SEFAZ);
/// após esgotar as tentativas, marca FALHA (terminal).
/// </summary>
public class ProcessarWebhookJob
{
    private static readonly TimeSpan[] Backoff = new[]
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(24),
    };
    private const int MaxTentativas = 8;

    private readonly FiscalDbContext _db;
    private readonly IDespachanteWebhook _despachante;
    private readonly ICertificadoStore _certStore;
    private readonly MetricasFiscais _metricas;
    private readonly ILogger<ProcessarWebhookJob> _logger;

    public ProcessarWebhookJob(
        FiscalDbContext db,
        IDespachanteWebhook despachante,
        ICertificadoStore certStore,
        MetricasFiscais metricas,
        ILogger<ProcessarWebhookJob> logger)
    {
        _db = db;
        _despachante = despachante;
        _certStore = certStore;
        _metricas = metricas;
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 1)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecutarAsync(Guid entregaId, CancellationToken ct)
    {
        // Claim atômico: assume apenas se ainda PENDENTE — evita entrega dupla
        // quando varredor e enqueue inicial disparam o mesmo id em paralelo.
        // ProximaTentativaEm vira lease (+30 min): o VarrerWebhooksJob devolve
        // órfãs em ENTREGANDO (crash no meio) para PENDENTE quando ela vence.
        DateTimeOffset? leaseAte = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(30);
        var claimado = await _db.WebhooksEntrega
            .Where(w => w.Id == entregaId && w.Status == "PENDENTE")
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, "ENTREGANDO")
                .SetProperty(w => w.ProximaTentativaEm, leaseAte), ct);
        if (claimado == 0) return;

        var entrega = await _db.WebhooksEntrega.FirstOrDefaultAsync(w => w.Id == entregaId, ct);
        if (entrega is null)
        {
            _logger.LogWarning("WebhookEntrega {Id} não encontrada — descartando job.", entregaId);
            return;
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == entrega.TenantId, ct);
        if (tenant?.WebhookUrl is null)
        {
            entrega.Status = "FALHA";
            entrega.UltimoErro = "Tenant sem webhook configurado (removido?).";
            entrega.ProximaTentativaEm = null;
            await _db.SaveChangesAsync(ct);
            return;
        }

        var segredo = await ResolverSegredoAsync(tenant, ct);
        var resultado = await _despachante.EntregarAsync(
            tenant.WebhookUrl, segredo, entrega.Payload, ct);

        entrega.Tentativas += 1;
        entrega.UltimoStatusCode = resultado.StatusCode;
        entrega.UltimoErro = resultado.Erro;

        if (resultado.Sucesso)
        {
            entrega.Status = "ENTREGUE";
            entrega.EntregueEm = DateTimeOffset.UtcNow;
            entrega.ProximaTentativaEm = null;
        }
        else if (entrega.Tentativas >= MaxTentativas)
        {
            entrega.Status = "FALHA";
            entrega.ProximaTentativaEm = null;
            _logger.LogWarning("WebhookEntrega {Id} esgotou tentativas ({Tentativas}): {Erro}",
                entrega.Id, entrega.Tentativas, resultado.Erro);
        }
        else
        {
            entrega.Status = "PENDENTE";
            entrega.ProximaTentativaEm = DateTimeOffset.UtcNow + ProximoBackoff(entrega.Tentativas);
        }

        await _db.SaveChangesAsync(ct);
        _metricas.WebhookEntregue(entrega.TipoEvento, entrega.Status == "ENTREGUE");
        _logger.LogInformation("WebhookEntrega {Id} ({Tipo}) → {Status} (tentativa {Tentativa})",
            entrega.Id, entrega.TipoEvento, entrega.Status, entrega.Tentativas);
    }

    private static TimeSpan ProximoBackoff(int tentativa) =>
        tentativa <= 0 ? Backoff[0] : Backoff[Math.Min(tentativa - 1, Backoff.Length - 1)];

    /// <summary>
    /// Segredo do tenant para assinar a entrega: decifra o envelope da KEK ou
    /// usa o legado em texto plano — e, neste caso, migra em voo para a coluna
    /// cifrada limpando o texto plano.
    /// </summary>
    private async Task<string> ResolverSegredoAsync(Tenant tenant, CancellationToken ct)
    {
        if (tenant.WebhookSecretCriptografado is not null)
            return await _certStore.DecifrarTextoAsync(tenant.WebhookSecretCriptografado, ct);

        var legado = tenant.WebhookSecret ?? string.Empty;
        tenant.WebhookSecretCriptografado = await _certStore.CifrarTextoAsync(legado, ct);
        tenant.WebhookSecret = null;
        await _db.SaveChangesAsync(ct);
        return legado;
    }
}
