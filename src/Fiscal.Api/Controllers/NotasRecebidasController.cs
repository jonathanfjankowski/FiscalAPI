using Fiscal.Core.Entities;
using Fiscal.Api.Authentication;
using Fiscal.Core.Contracts;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Fiscal.Worker.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

public record ManifestacaoRequest(string Tipo, string? Justificativa);

[ApiController]
[Route("v1/notas-recebidas")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.SchemeName)]
public class NotasRecebidasController : ControllerBase
{
    private static readonly string[] TiposValidos = ["210200", "210210", "210220", "210240"];
    private static readonly string[] TiposFinais = ["210200", "210220", "210240"];

    private readonly FiscalDbContext _db;
    private readonly IRepositorioNotaRecebida _notaRepo;
    private readonly IRepositorioManifestacao _manifestacaoRepo;
    private readonly IBackgroundJobClient _jobs;

    public NotasRecebidasController(
        FiscalDbContext db,
        IRepositorioNotaRecebida notaRepo,
        IRepositorioManifestacao manifestacaoRepo,
        IBackgroundJobClient jobs)
    {
        _db = db;
        _notaRepo = notaRepo;
        _manifestacaoRepo = manifestacaoRepo;
        _jobs = jobs;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var notas = await _notaRepo.ListarPorTenantAsync(tenantId, 200, ct);
        return Ok(notas.Select(n => new
        {
            n.Id,
            n.Chave,
            n.Nsu,
            ambiente = n.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
            n.CnpjEmitente,
            n.NomeEmitente,
            n.Valor,
            n.EmitidaEm,
            manifestacaoAtual = n.ManifestacaoAtual,
            tipoSchema = n.TipoSchema,
            recebidaEm = n.RecebidaEm,
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        var nota = await _notaRepo.ObterPorIdAsync(id, HttpContext.GetTenantId(), ct);
        if (nota is null) return NotFound();

        return Ok(new
        {
            nota.Id,
            nota.Chave,
            nota.Nsu,
            nota.CnpjEmitente,
            nota.NomeEmitente,
            nota.Valor,
            nota.EmitidaEm,
            nota.ManifestacaoAtual,
            nota.TipoSchema,
            nota.RecebidaEm,
        });
    }

    /// <summary>XML completo da nota (procNFe). 409 se só o resumo foi sincronizado.</summary>
    [HttpGet("{id:guid}/xml-completo")]
    public async Task<IActionResult> XmlCompleto(Guid id, CancellationToken ct)
    {
        var nota = await _notaRepo.ObterPorIdAsync(id, HttpContext.GetTenantId(), ct);
        if (nota is null) return NotFound();

        if (nota.XmlCompleto is null)
            return Problem(
                statusCode: 409,
                title: "XML completo ainda não sincronizado",
                detail: "A distribuição DFe devolveu apenas o resumo (resNFe). O procNFe chega em ciclos " +
                        "posteriores de sincronização (ou consulte a nota por chave).");

        return Content(nota.XmlCompleto, "application/xml");
    }

    /// <summary>
    /// Manifesta uma nota recebida: tipo = 210200 (confirmação), 210210 (ciência),
    /// 210220 (desconhecimento) ou 210240 (não realização — exige justificativa
    /// de 15 a 1000 caracteres). Processamento assíncrono (202).
    /// </summary>
    [HttpPost("{id:guid}/manifestacao")]
    public async Task<IActionResult> Manifestar(
        Guid id,
        [FromBody] ManifestacaoRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' é obrigatório.");

        var tenantId = HttpContext.GetTenantId();
        var nota = await _notaRepo.ObterPorIdAsync(id, tenantId, ct);
        if (nota is null) return NotFound();

        if (req.Tipo is null || !TiposValidos.Contains(req.Tipo))
            return Problem(statusCode: 422, title: "Tipo inválido",
                detail: "Use 210200 (confirmação), 210210 (ciência), 210220 (desconhecimento) ou 210240 (não realização).");

        if (req.Tipo == "210240" &&
            (string.IsNullOrWhiteSpace(req.Justificativa) || req.Justificativa.Length < 15 || req.Justificativa.Length > 1000))
            return Problem(statusCode: 422, title: "Não realização exige justificativa de 15 a 1000 caracteres.");

        if (nota.ManifestacaoAtual is not null && TiposFinais.Contains(nota.ManifestacaoAtual))
            return Problem(statusCode: 409, title: "Nota já manifestada em estado final.",
                detail: $"Manifestação atual: {nota.ManifestacaoAtual}.");

        var existente = await _manifestacaoRepo.ObterPorIdempotencyAsync(tenantId, id, idempotencyKey!, ct);
        if (existente is not null)
            return Ok(new { manifestacaoId = existente.Id, status = existente.Status, criadoEm = existente.CriadoEm });

        var manif = new ManifestacaoDestinatario
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NotaRecebidaId = id,
            Tipo = req.Tipo,
            Justificativa = req.Justificativa,
            IdempotencyKey = idempotencyKey!,
            Status = "PENDENTE",
        };
        await _manifestacaoRepo.AdicionarAsync(manif, ct);
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<ProcessarManifestacaoJob>(j => j.ExecutarAsync(manif.Id, CancellationToken.None));

        return Accepted($"/v1/notas-recebidas/{id}", new
        {
            manifestacaoId = manif.Id,
            tipo = manif.Tipo,
            status = manif.Status,
            criadoEm = manif.CriadoEm,
        });
    }
}
