using System.Text.Json;
using Fiscal.Api.Infrastructure;
using Fiscal.Core;
using Fiscal.Core.Contracts;
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
/// Inutilização de faixa de numeração pelo painel.
///
/// Operação distinta do reenvio: reenvio corrige o payload do MESMO documento
/// (mantendo o número); inutilização libera uma FAIXA de números que nunca
/// foi transmitida à SEFAZ. O ambiente é derivado de
/// <c>Tenant.AmbientePadrao</c> — o painel não tem API key para informar.
/// </summary>
[ApiController]
[Route("v1/admin/inutilizacoes")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminInutilizacoesController : ControllerBase
{
    private const string EventoInutilizacao = "INUTILIZACAO";

    private readonly FiscalDbContext _db;
    private readonly IRepositorioEventoFiscal _eventoRepo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IBackgroundJobClient _jobs;

    public AdminInutilizacoesController(
        FiscalDbContext db,
        IRepositorioEventoFiscal eventoRepo,
        IRepositorioAuditoria auditoria,
        IBackgroundJobClient jobs)
    {
        _db = db;
        _eventoRepo = eventoRepo;
        _auditoria = auditoria;
        _jobs = jobs;
    }

    public record AdminInutilizacaoRequest(
        Guid TenantId,
        short Modelo,
        short Serie,
        long NumeroInicial,
        long NumeroFinal,
        string? Justificativa);

    [HttpPost]
    public async Task<IActionResult> Inutilizar(
        [FromBody] AdminInutilizacaoRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == req.TenantId, ct);
        if (tenant is null) return NotFound();

        if (req.Modelo is not (55 or 65))
            return Problem(statusCode: 422, title: "Modelo inválido",
                detail: "Inutilização aplica-se apenas a NF-e (55) e NFC-e (65).");

        if (req.NumeroInicial < 1)
            return Problem(statusCode: 422, title: "Faixa inválida",
                detail: "numeroInicial deve ser >= 1 — a numeração fiscal começa em 1.");

        if (req.NumeroFinal < req.NumeroInicial)
            return Problem(statusCode: 422, title: "Faixa inválida",
                detail: "numeroFinal deve ser >= numeroInicial.");

        // A SEFAZ exige justificativa de 15 a 1000 caracteres no evento 110111.
        var justificativa = (req.Justificativa ?? "").Trim();
        if (justificativa.Length < 15 || justificativa.Length > 1000)
            return Problem(statusCode: 422, title: "Justificativa inválida",
                detail: "Informe de 15 a 1000 caracteres.");

        // Ambiente derivado do tenant — o painel não carrega API key.
        var ambiente = tenant.AmbientePadrao == (short)Ambiente.Producao
            ? "producao"
            : "homologacao";

        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid().ToString()
            : idempotencyKey;

        var existente = await _eventoRepo.ObterPorIdempotencyAsync(
            tenant.Id, null, EventoInutilizacao, idempotencyKey, ct);
        if (existente is not null)
            return Accepted($"/v1/admin/inutilizacoes/{existente.Id}", ParaResponse(existente));

        var evento = new EventoFiscal
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            DocumentoId = null,
            TipoEvento = EventoInutilizacao,
            IdempotencyKey = idempotencyKey,
            Justificativa = justificativa,
            DadosEvento = JsonSerializer.Serialize(new InutilizacaoDados(
                req.Modelo, req.Serie, req.NumeroInicial, req.NumeroFinal, ambiente)),
            Status = "PENDENTE",
            CriadoEm = DateTimeOffset.UtcNow
        };
        await _eventoRepo.AdicionarAsync(evento, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Corrida: outra request com a mesma Idempotency-Key gravou
            // primeiro — devolve a vencedora (replay) em vez de 500.
            var vencedora = await _eventoRepo.ObterPorIdempotencyAsync(
                tenant.Id, null, EventoInutilizacao, idempotencyKey, ct);
            if (vencedora is null) throw;
            return Accepted($"/v1/admin/inutilizacoes/{vencedora.Id}", ParaResponse(vencedora));
        }

        _jobs.Enqueue<ProcessarEventoJob>(j => j.ExecutarAsync(evento.Id, CancellationToken.None));

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenant.Id,
            Acao = "INUTILIZACAO_SOLICITADA",
            RecursoId = evento.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress,
            Detalhe = $"{{\"modelo\":{req.Modelo},\"serie\":{req.Serie}," +
                      $"\"de\":{req.NumeroInicial},\"ate\":{req.NumeroFinal},\"via\":\"admin\"}}"
        }, ct);
        await _db.SaveChangesAsync(ct);

        return Accepted($"/v1/admin/inutilizacoes/{evento.Id}", ParaResponse(evento));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        var evento = await _db.EventosFiscais.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (evento is null || evento.TipoEvento != EventoInutilizacao) return NotFound();

        return Ok(ParaResponse(evento));
    }

    private static object ParaResponse(EventoFiscal e)
    {
        // DadosEvento guarda a faixa como JSON. Sem devolvê-la o operador não
        // consegue ver O QUE foi inutilizado — só que algo foi.
        InutilizacaoDados? faixa = null;
        if (!string.IsNullOrWhiteSpace(e.DadosEvento))
        {
            try { faixa = JsonSerializer.Deserialize<InutilizacaoDados>(e.DadosEvento); }
            catch (JsonException) { faixa = null; }
        }

        return new
        {
            eventoId = e.Id,
            tipo = e.TipoEvento,
            e.Status,
            e.CriadoEm,
            motivo = e.MotivoStatus,
            e.Tentativas,
            e.ProximaTentativaEm,
            e.Protocolo,
            tenantId = e.TenantId,
            justificativa = e.Justificativa,
            modelo = faixa?.Modelo,
            serie = faixa?.Serie,
            numeroInicial = faixa?.NumeroInicial,
            numeroFinal = faixa?.NumeroFinal,
            ambiente = faixa?.Ambiente,
        };
    }
}