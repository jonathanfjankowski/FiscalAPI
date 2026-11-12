using Fiscal.Core.Enums;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

/// <summary>
/// Visão geral da operação.
///
/// As contagens respondem "quantos"; as listas de atenção respondem "o que
/// está travado e precisa de ação" — fila de transmissão, certificados
/// vencendo (com a empresa), webhooks em falha e rejeições com o motivo da
/// SEFAZ. É a diferença entre um dashboard que informa e um que orienta.
/// </summary>
[ApiController]
[Route("v1/admin/dashboard")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminDashboardController : ControllerBase
{
    private const int LimiteAtencao = 20;

    private readonly FiscalDbContext _db;

    public AdminDashboardController(FiscalDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Resumo(CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        var inicioHoje = new DateTimeOffset(agora.Date, TimeSpan.Zero);
        var inicio7Dias = agora.AddDays(-7);
        var validoAteLimite = DateOnly.FromDateTime(agora.AddDays(30).UtcDateTime);

        // O dashboard é a landing page e atualiza a cada 15s: em Postgres os
        // agregados rodam no SQL (COUNT/GROUP BY), sem trazer a tabela de
        // documentos para a memória. No SQLite da suíte o GroupBy sobre enum
        // e a comparação de DateTimeOffset não traduzem de forma uniforme,
        // então ali a projeção mínima em memória segue — o volume é pequeno.
        var sqlite = _db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        int documentosHoje, documentos7Dias, emContingencia, certificadosVencendo30Dias;
        object porStatus;

        if (sqlite)
        {
            var docs = await _db.DocumentosFiscais.AsNoTracking()
                .Select(d => new { d.CriadoEm, d.Status })
                .ToListAsync(ct);
            var certs = await _db.Certificados.AsNoTracking()
                .Select(c => new { c.Ativo, c.ValidoAte })
                .ToListAsync(ct);

            documentosHoje = docs.Count(d => d.CriadoEm >= inicioHoje);
            documentos7Dias = docs.Count(d => d.CriadoEm >= inicio7Dias);
            emContingencia = docs.Count(d => d.Status == StatusDocumento.CONTINGENCIA);
            certificadosVencendo30Dias = certs.Count(c => c.Ativo && c.ValidoAte <= validoAteLimite);
            porStatus = docs
                .GroupBy(d => d.Status)
                .Select(g => new { status = g.Key.ToString(), total = g.Count() })
                .OrderBy(x => x.status)
                .ToList();
        }
        else
        {
            var docsQuery = _db.DocumentosFiscais.AsNoTracking();
            documentosHoje = await docsQuery.CountAsync(d => d.CriadoEm >= inicioHoje, ct);
            documentos7Dias = await docsQuery.CountAsync(d => d.CriadoEm >= inicio7Dias, ct);
            emContingencia = await docsQuery.CountAsync(d => d.Status == StatusDocumento.CONTINGENCIA, ct);
            certificadosVencendo30Dias = await _db.Certificados.AsNoTracking()
                .CountAsync(c => c.Ativo && c.ValidoAte <= validoAteLimite, ct);

            var grupos = await docsQuery
                .GroupBy(d => d.Status)
                .Select(g => new { Status = g.Key, Total = g.Count() })
                .ToListAsync(ct);
            porStatus = grupos
                .Select(g => new { status = g.Status.ToString(), total = g.Total })
                .OrderBy(x => x.status)
                .ToList();
        }

        return Ok(new
        {
            // ---------- Contagens (compatibilidade + fita do topo) ----------
            documentosHoje,
            documentos7Dias,
            emContingencia,
            porStatus,
            tenantsAtivos = await _db.Tenants.AsNoTracking().CountAsync(t => t.Ativo, ct),
            apiKeysAtivas = await _db.ApiKeys.AsNoTracking()
                .CountAsync(k => k.Ativa && k.RevogadoEm == null, ct),
            certificadosVencendo30Dias,

            // ---------- Fila de transmissão ----------
            fila = await FilaAsync(ct),

            // ---------- Precisa de atenção ----------
            certificadosVencendo = await CertificadosVencendoAsync(validoAteLimite, ct),
            webhooksEmFalha = await WebhooksEmFalhaAsync(ct),
            rejeicoesRecentes = await RejeicoesRecentesAsync(ct),

            // ---------- Emissão recente ----------
            emissaoRecente = await EmissaoRecenteAsync(ct),
        });
    }

    /// <summary>
    /// Últimos documentos, de qualquer status — a leitura de "o que acabou de
    /// acontecer" sem sair da landing page.
    /// </summary>
    private async Task<List<object>> EmissaoRecenteAsync(CancellationToken ct)
    {
        var query = _db.DocumentosFiscais.AsNoTracking();

        // Ordenação no banco em Postgres; em memória no SQLite (OrderBy sobre
        // DateTimeOffset não traduz). Projeta antes para não trazer payload.
        var projecao = query.Select(d => new
        {
            d.Id,
            d.TenantId,
            TenantRazaoSocial = d.Tenant!.RazaoSocial,
            d.Tipo,
            d.Status,
            Ambiente = d.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
            d.Serie,
            d.Numero,
            d.CriadoEm,
        });

        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var todos = await projecao.ToListAsync(ct);
            return todos
                .OrderByDescending(d => d.CriadoEm)
                .ThenByDescending(d => d.Id)
                .Take(LimiteAtencao)
                .Select(d => (object)new
                {
                    d.Id,
                    d.TenantId,
                    d.TenantRazaoSocial,
                    tipo = d.Tipo.ToString(),
                    status = d.Status.ToString(),
                    d.Ambiente,
                    d.Serie,
                    d.Numero,
                    d.CriadoEm,
                })
                .ToList();
        }

        var recentes = await query
            .OrderByDescending(d => d.CriadoEm)
            .ThenByDescending(d => d.Id)
            .Take(LimiteAtencao)
            .Select(d => new
            {
                d.Id,
                d.TenantId,
                TenantRazaoSocial = d.Tenant!.RazaoSocial,
                Tipo = d.Tipo.ToString(),
                Status = d.Status.ToString(),
                Ambiente = d.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
                d.Serie,
                d.Numero,
                d.CriadoEm,
            })
            .ToListAsync(ct);

        return recentes
            .Select(d => (object)new
            {
                d.Id,
                d.TenantId,
                d.TenantRazaoSocial,
                tipo = d.Tipo,
                status = d.Status,
                d.Ambiente,
                d.Serie,
                d.Numero,
                d.CriadoEm,
            })
            .ToList();
    }

    /// <summary>Documentos em trânsito — o que está na mão do Worker agora.</summary>
    private async Task<List<object>> FilaAsync(CancellationToken ct)
    {
        var statusFila = new[]
        {
            StatusDocumento.PENDENTE,
            StatusDocumento.PROCESSANDO,
            StatusDocumento.CONTINGENCIA,
            StatusDocumento.CANCELAMENTO_PENDENTE,
        };

        var docs = await _db.DocumentosFiscais.AsNoTracking()
            .Where(d => statusFila.Contains(d.Status))
            .Select(d => new
            {
                d.Id,
                d.TenantId,
                TenantRazaoSocial = d.Tenant!.RazaoSocial,
                d.Tipo,
                d.Status,
                d.Modelo,
                d.Serie,
                d.Numero,
                d.Tentativas,
                d.ProximaTentativaEm,
                d.CriadoEm,
                d.AtualizadoEm,
            })
            .ToListAsync(ct);

        return docs
            .OrderBy(d => d.CriadoEm)
            .Take(LimiteAtencao)
            .Select(d => (object)new
            {
                d.Id,
                d.TenantId,
                d.TenantRazaoSocial,
                tipo = d.Tipo.ToString(),
                status = d.Status.ToString(),
                d.Modelo,
                d.Serie,
                d.Numero,
                d.Tentativas,
                d.ProximaTentativaEm,
                d.CriadoEm,
            })
            .ToList();
    }

    /// <summary>Certificados A1 que vencem em 30 dias — com a empresa, para agir.</summary>
    private async Task<List<object>> CertificadosVencendoAsync(DateOnly limite, CancellationToken ct)
    {
        var certs = await _db.Certificados.AsNoTracking()
            .Where(c => c.Ativo && c.ValidoAte <= limite)
            .Select(c => new
            {
                c.Id,
                c.TenantId,
                TenantRazaoSocial = c.Tenant != null ? c.Tenant.RazaoSocial : null,
                c.Thumbprint,
                c.ValidoAte,
            })
            .ToListAsync(ct);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        return certs
            .OrderBy(c => c.ValidoAte)
            .Select(c => (object)new
            {
                c.Id,
                c.TenantId,
                c.TenantRazaoSocial,
                c.Thumbprint,
                c.ValidoAte,
                diasRestantes = c.ValidoAte.DayNumber - hoje.DayNumber,
            })
            .ToList();
    }

    /// <summary>Entregas de webhook que esgotaram as tentativas.</summary>
    private async Task<List<object>> WebhooksEmFalhaAsync(CancellationToken ct)
    {
        var entregas = await _db.WebhooksEntrega.AsNoTracking()
            .Where(w => w.Status == "FALHA")
            .Select(w => new
            {
                w.Id,
                w.TenantId,
                w.TipoEvento,
                w.Tentativas,
                w.UltimoStatusCode,
                w.UltimoErro,
                w.CriadoEm,
            })
            .ToListAsync(ct);

        // WebhookEntrega não tem navigation para Tenant — resolve os nomes
        // numa consulta restrita aos tenants da página.
        var tenantIds = entregas.Select(e => e.TenantId).Distinct().ToList();
        var nomes = await _db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.RazaoSocial })
            .ToDictionaryAsync(t => t.Id, t => t.RazaoSocial, ct);

        return entregas
            .OrderByDescending(w => w.CriadoEm)
            .Take(LimiteAtencao)
            .Select(w => (object)new
            {
                w.Id,
                w.TenantId,
                tenantRazaoSocial = nomes.TryGetValue(w.TenantId, out var nome) ? nome : null,
                w.TipoEvento,
                w.Tentativas,
                w.UltimoStatusCode,
                w.UltimoErro,
                w.CriadoEm,
            })
            .ToList();
    }

    /// <summary>Rejeições recentes com o motivo — é o que o operador precisa ler.</summary>
    private async Task<List<object>> RejeicoesRecentesAsync(CancellationToken ct)
    {
        var docs = await _db.DocumentosFiscais.AsNoTracking()
            .Where(d => d.Status == StatusDocumento.REJEITADA
                        || d.Status == StatusDocumento.DENEGADA
                        || d.Status == StatusDocumento.FALHA_EMISSAO)
            .Select(d => new
            {
                d.Id,
                d.TenantId,
                TenantRazaoSocial = d.Tenant!.RazaoSocial,
                d.Serie,
                d.Numero,
                d.MotivoStatus,
                d.CriadoEm,
            })
            .ToListAsync(ct);

        return docs
            .OrderByDescending(d => d.CriadoEm)
            .Take(LimiteAtencao)
            .Select(d => (object)new
            {
                d.Id,
                d.TenantId,
                d.TenantRazaoSocial,
                d.Serie,
                d.Numero,
                motivo = d.MotivoStatus,
                d.CriadoEm,
            })
            .ToList();
    }
}