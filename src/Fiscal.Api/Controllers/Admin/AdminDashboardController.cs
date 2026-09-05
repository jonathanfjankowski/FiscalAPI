using Fiscal.Core.Enums;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

[ApiController]
[Route("v1/admin/dashboard")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminDashboardController : ControllerBase
{
    private readonly FiscalDbContext _db;

    public AdminDashboardController(FiscalDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Resumo(CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        var inicioHoje = new DateTimeOffset(agora.Date, TimeSpan.Zero);
        var inicio7Dias = agora.AddDays(-7);
        var validoAteLimite = DateOnly.FromDateTime(agora.AddDays(30).UtcDateTime);

        // SQLite (suíte de testes) não traduz comparações/ordenação de
        // DateTimeOffset nem GroupBy de forma uniforme com o Postgres — então
        // os agregados de documentos/certificados são calculados em memória
        // sobre uma projeção mínima. Suficiente para o MVP do painel.
        var docs = await _db.DocumentosFiscais.AsNoTracking()
            .Select(d => new { d.CriadoEm, d.Status })
            .ToListAsync(ct);
        var certificados = await _db.Certificados.AsNoTracking()
            .Select(c => new { c.Ativo, c.ValidoAte })
            .ToListAsync(ct);

        return Ok(new
        {
            documentosHoje = docs.Count(d => d.CriadoEm >= inicioHoje),
            documentos7Dias = docs.Count(d => d.CriadoEm >= inicio7Dias),
            emContingencia = docs.Count(d => d.Status == StatusDocumento.CONTINGENCIA),
            porStatus = docs
                .GroupBy(d => d.Status)
                .Select(g => new { status = g.Key.ToString(), total = g.Count() })
                .OrderBy(x => x.status),
            tenantsAtivos = await _db.Tenants.AsNoTracking().CountAsync(t => t.Ativo, ct),
            apiKeysAtivas = await _db.ApiKeys.AsNoTracking()
                .CountAsync(k => k.Ativa && k.RevogadoEm == null, ct),
            certificadosVencendo30Dias = certificados
                .Count(c => c.Ativo && c.ValidoAte <= validoAteLimite)
        });
    }
}
