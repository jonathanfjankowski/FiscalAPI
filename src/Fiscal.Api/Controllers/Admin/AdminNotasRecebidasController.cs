using System.Text.Json;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Fiscal.Worker.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

/// <summary>
/// Notas recebidas pela distribuição DFe, visão cross-tenant do painel.
///
/// O integrador consulta as do próprio tenant; o operador precisa ver todas
/// — é assim que se descobre que a sincronização de UMA empresa parou.
/// </summary>
[ApiController]
[Route("v1/admin/notas-recebidas")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminNotasRecebidasController : ControllerBase
{
    private const int PageSizeMax = 200;

    private static readonly string[] TiposValidos = ["210200", "210210", "210220", "210240"];

    // ManifestacaoAtual guarda o NOME (ProcessarManifestacaoJob.NomeAmigavel),
    // não o código do evento. Comparar com "210200" nunca casaria — a nota
    // manifestada aceitaria uma segunda manifestação final.
    private static readonly string[] ManifestacoesFinais = ["confirmacao", "desconhecimento", "nao_realizacao"];

    private readonly FiscalDbContext _db;
    private readonly IRepositorioManifestacao _manifestacaoRepo;
    private readonly IBackgroundJobClient _jobs;

    public AdminNotasRecebidasController(
        FiscalDbContext db,
        IRepositorioManifestacao manifestacaoRepo,
        IBackgroundJobClient jobs)
    {
        _db = db;
        _manifestacaoRepo = manifestacaoRepo;
        _jobs = jobs;
    }

    public record AdminManifestacaoRequest(string? Tipo, string? Justificativa);

    public record AdminNotaItem(
        Guid Id,
        Guid TenantId,
        string? TenantRazaoSocial,
        string Chave,
        string Nsu,
        string Ambiente,
        string? CnpjEmitente,
        string? NomeEmitente,
        decimal? Valor,
        DateTimeOffset? EmitidaEm,
        string? ManifestacaoAtual,
        string TipoSchema,
        DateTimeOffset RecebidaEm);

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 100;
        if (pageSize > PageSizeMax) pageSize = PageSizeMax;

        var query = _db.NotasRecebidas.AsNoTracking();
        if (tenantId is not null) query = query.Where(n => n.TenantId == tenantId);

        var total = await query.CountAsync(ct);

        // SQLite não traduz ORDER BY sobre DateTimeOffset. Ordenar depois do
        // Skip/Take daria páginas não-determinísticas, então no SQLite o
        // conjunto é materializado, ordenado e paginado em memória — o volume
        // da suíte de testes é pequeno por definição.
        var projecao = query.Select(n => new AdminNotaItem(
            n.Id,
            n.TenantId,
            n.Tenant!.RazaoSocial,
            n.Chave,
            n.Nsu,
            n.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
            n.CnpjEmitente,
            n.NomeEmitente,
            n.Valor,
            n.EmitidaEm,
            n.ManifestacaoAtual,
            n.TipoSchema,
            n.RecebidaEm));

        List<AdminNotaItem> itens;
        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // SQLite não traduz ORDER BY sobre DateTimeOffset. Materializa,
            // ordena e pagina em memória — só a suíte usa SQLite.
            var todos = await projecao.ToListAsync(ct);
            itens = todos
                .OrderByDescending(n => n.RecebidaEm)
                .ThenByDescending(n => n.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
        else
        {
            // Postgres: ordenar TEM que ser sobre as colunas da ENTIDADE, antes
            // da projeção para record — o EF não traduz OrderBy sobre um record
            // nomeado construído (lança "could not be translated" e a página
            // inteira morre em 500). Ver TraducaoNpgsqlTests.
            itens = await query
                .OrderByDescending(n => n.RecebidaEm)
                .ThenByDescending(n => n.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new AdminNotaItem(
                    n.Id,
                    n.TenantId,
                    n.Tenant!.RazaoSocial,
                    n.Chave,
                    n.Nsu,
                    n.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
                    n.CnpjEmitente,
                    n.NomeEmitente,
                    n.Valor,
                    n.EmitidaEm,
                    n.ManifestacaoAtual,
                    n.TipoSchema,
                    n.RecebidaEm))
                .ToListAsync(ct);
        }

        return Ok(new { total, page, pageSize, itens });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        var nota = await _db.NotasRecebidas.AsNoTracking()
            .Include(n => n.Tenant)
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nota is null) return NotFound();

        return Ok(new
        {
            nota.Id,
            nota.TenantId,
            tenantRazaoSocial = nota.Tenant?.RazaoSocial,
            nota.Chave,
            nota.Nsu,
            ambiente = nota.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
            nota.CnpjEmitente,
            nota.NomeEmitente,
            nota.Valor,
            nota.EmitidaEm,
            nota.ManifestacaoAtual,
            nota.TipoSchema,
            nota.RecebidaEm,
        });
    }

    /// <summary>
    /// XML completo (procNFe). 409 quando a distribuição devolveu só o resumo
    /// — o XML chega em ciclos posteriores de sincronização.
    /// </summary>
    [HttpGet("{id:guid}/xml-completo")]
    public async Task<IActionResult> XmlCompleto(Guid id, CancellationToken ct)
    {
        var nota = await _db.NotasRecebidas.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nota is null) return NotFound();

        if (nota.XmlCompleto is null)
        {
            return Problem(
                statusCode: 409,
                title: "XML completo ainda não sincronizado",
                detail: "A distribuição DFe devolveu apenas o resumo (resNFe). O procNFe chega em " +
                        "ciclos posteriores de sincronização.");
        }

        return Content(nota.XmlCompleto, "application/xml");
    }

    /// <summary>
    /// Manifesta a nota em nome do destinatário. Processamento assíncrono.
    /// </summary>
    [HttpPost("{id:guid}/manifestacao")]
    public async Task<IActionResult> Manifestar(
        Guid id,
        [FromBody] AdminManifestacaoRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var nota = await _db.NotasRecebidas.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nota is null) return NotFound();

        if (req.Tipo is null || !TiposValidos.Contains(req.Tipo))
        {
            return Problem(statusCode: 422, title: "Tipo inválido",
                detail: "Use 210200 (confirmação), 210210 (ciência), 210220 (desconhecimento) " +
                        "ou 210240 (não realização).");
        }

        if (req.Tipo == "210240" &&
            (string.IsNullOrWhiteSpace(req.Justificativa) ||
             req.Justificativa.Length < 15 || req.Justificativa.Length > 1000))
        {
            return Problem(statusCode: 422,
                title: "Não realização exige justificativa de 15 a 1000 caracteres.");
        }

        if (nota.ManifestacaoAtual is not null && ManifestacoesFinais.Contains(nota.ManifestacaoAtual))
        {
            return Problem(statusCode: 409, title: "Nota já manifestada em estado final.",
                detail: $"Manifestação atual: {nota.ManifestacaoAtual}.");
        }

        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid().ToString()
            : idempotencyKey;

        var existente = await _manifestacaoRepo.ObterPorIdempotencyAsync(
            nota.TenantId, id, idempotencyKey, ct);
        if (existente is not null)
        {
            return Accepted($"/v1/admin/notas-recebidas/{id}", new
            {
                manifestacaoId = existente.Id,
                tipo = existente.Tipo,
                existente.Status,
                existente.CriadoEm,
            });
        }

        var manif = new ManifestacaoDestinatario
        {
            TenantId = nota.TenantId,
            NotaRecebidaId = id,
            Tipo = req.Tipo,
            Justificativa = req.Justificativa,
            IdempotencyKey = idempotencyKey,
            Status = "PENDENTE",
        };
        await _manifestacaoRepo.AdicionarAsync(manif, ct);
        await _db.SaveChangesAsync(ct);

        _jobs.Enqueue<ProcessarManifestacaoJob>(
            j => j.ExecutarAsync(manif.Id, CancellationToken.None));

        return Accepted($"/v1/admin/notas-recebidas/{id}", new
        {
            manifestacaoId = manif.Id,
            tipo = manif.Tipo,
            manif.Status,
            manif.CriadoEm,
        });
    }
}