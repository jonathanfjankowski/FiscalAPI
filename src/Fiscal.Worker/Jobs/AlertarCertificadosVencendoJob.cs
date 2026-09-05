using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Worker.Jobs;

/// <summary>
/// Alerta tenants com certificado A1 vencendo em ≤ 15 dias (webhook
/// `certificado.vencendo` via outbox, no máximo 1 por dia por tenant).
/// Recorrente: diário às 12:00 (cron com segundos).
/// </summary>
public class AlertarCertificadosVencendoJob
{
    public const string TipoEvento = "certificado.vencendo";
    public const int JanelaDias = 15;

    private readonly FiscalDbContext _db;
    private readonly ILogger<AlertarCertificadosVencendoJob> _logger;

    public AlertarCertificadosVencendoJob(FiscalDbContext db, ILogger<AlertarCertificadosVencendoJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken ct)
    {
        var limite = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(JanelaDias));
        var umDiaAtras = DateTimeOffset.UtcNow.AddDays(-1);

        var vencendo = await _db.Certificados
            .Where(c => c.Ativo && c.ValidoAte <= limite)
            .ToListAsync(ct);

        foreach (var cert in vencendo)
        {
            // Dedupe: no máximo um alerta por tenant a cada 24h. (SQLite não
            // traduz comparação de DateTimeOffset em SQL — filtra em memória.)
            var alertas = await _db.WebhooksEntrega
                .Where(w => w.TenantId == cert.TenantId && w.TipoEvento == TipoEvento)
                .Select(w => w.CriadoEm)
                .ToListAsync(ct);
            if (alertas.Any(d => d >= umDiaAtras)) continue;

            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == cert.TenantId, ct);
            if (tenant?.WebhookUrl is null) continue;

            var diasRestantes = cert.ValidoAte.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
            _db.WebhooksEntrega.Add(new WebhookEntrega
            {
                TenantId = tenant.Id,
                DocumentoId = null,
                TipoEvento = TipoEvento,
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    tipo = TipoEvento,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    certificado = new
                    {
                        cert.Id,
                        cert.Thumbprint,
                        validoAte = cert.ValidoAte,
                        diasRestantes,
                    },
                    mensagem = diasRestantes < 0
                        ? "Certificado vencido — emissão indisponível até renovar."
                        : $"Certificado vence em {diasRestantes} dia(s).",
                }),
                Status = "PENDENTE",
                ProximaTentativaEm = DateTimeOffset.UtcNow,
            });
            _logger.LogWarning("Certificado {Thumbprint} do tenant {Tenant} vence em {Dias} dia(s).",
                cert.Thumbprint, tenant.Id, diasRestantes);
        }

        await _db.SaveChangesAsync(ct);
    }
}
