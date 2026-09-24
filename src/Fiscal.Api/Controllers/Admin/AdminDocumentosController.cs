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

[ApiController]
[Route("v1/admin/documentos-fiscais")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminDocumentosController : ControllerBase
{
    private const int PageSizeMax = 200;
    private const string EventoCancelamento = "CANCELAMENTO";
    private const string EventoCartaCorrecao = "CCE";

    private readonly FiscalDbContext _db;
    private readonly IRepositorioEventoFiscal _eventoRepo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IBackgroundJobClient _jobs;

    public AdminDocumentosController(
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

    public record AdminDocListItem(
        Guid Id,
        Guid TenantId,
        string TenantRazaoSocial,
        string Tipo,
        string Status,
        string Ambiente,
        short? Modelo,
        short? Serie,
        long? Numero,
        string? ChaveAcesso,
        string? MotivoStatus,
        DateTimeOffset CriadoEm,
        DateTimeOffset AtualizadoEm);

    /// <summary>
    /// Listagem cross-tenant com filtros e paginação. 'status' aceita o nome do
    /// enum (ex.: AUTORIZADA, CONTINGENCIA); 'de'/'ate' são DateTimeOffset ISO.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? status,
        [FromQuery] short? modelo,
        [FromQuery] DateTimeOffset? de,
        [FromQuery] DateTimeOffset? ate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > PageSizeMax) pageSize = PageSizeMax;

        StatusDocumento? statusFiltro = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<StatusDocumento>(status, ignoreCase: true, out var parsed))
                return Problem(statusCode: 422, title: "Status inválido.",
                    detail: $"Use um status válido: {string.Join(", ", Enum.GetNames<StatusDocumento>())}.");
            statusFiltro = parsed;
        }

        var query = _db.DocumentosFiscais.AsNoTracking();
        if (tenantId is not null) query = query.Where(d => d.TenantId == tenantId);
        if (statusFiltro is not null) query = query.Where(d => d.Status == statusFiltro);
        if (modelo is not null) query = query.Where(d => d.Modelo == modelo);
        if (de is not null) query = query.Where(d => d.CriadoEm >= de);
        if (ate is not null) query = query.Where(d => d.CriadoEm <= ate);

        var total = await query.CountAsync(ct);

        // SQLite (suíte de testes) não traduz ORDER BY sobre DateTimeOffset;
        // Postgres (produção) sim — mantém "mais recente primeiro" em produção.
        var ordenada = _db.Database.IsSqlite()
            ? query.OrderBy(d => d.Id)
            : query.OrderByDescending(d => d.CriadoEm);

        var itens = await ordenada
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new AdminDocListItem(
                d.Id, d.TenantId, d.Tenant!.RazaoSocial,
                d.Tipo.ToString(), d.Status.ToString(),
                d.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
                d.Modelo, d.Serie, d.Numero, d.ChaveAcesso, d.MotivoStatus,
                d.CriadoEm, d.AtualizadoEm))
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, itens });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        var doc = await _db.DocumentosFiscais.AsNoTracking()
            .Include(d => d.Tenant)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();

        return Ok(new
        {
            doc.Id,
            tenantId = doc.TenantId,
            tenantRazaoSocial = doc.Tenant?.RazaoSocial,
            tipo = doc.Tipo.ToString(),
            status = doc.Status.ToString(),
            ambiente = doc.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
            doc.Modelo,
            doc.Serie,
            doc.Numero,
            doc.ChaveAcesso,
            doc.ProtocoloAutorizacao,
            doc.ReciboLote,
            doc.XmlAssinado,
            doc.XmlRetornoSefaz,
            doc.MotivoStatus,
            doc.Tentativas,
            doc.ModoContingencia,
            doc.ProximaTentativaEm,
            doc.CriadoEm,
            doc.AtualizadoEm
        });
    }

    public record AdminEventoRequest(string? Justificativa, string? Correcao);

    /// <summary>
    /// Cancelamento via painel (mesmas regras do endpoint de tenant: exige
    /// AUTORIZADA). Idempotency-Key gerada se não informada.
    /// </summary>
    [HttpPost("{id:guid}/cancelamento")]
    public async Task<IActionResult> Cancelar(
        Guid id,
        [FromBody] AdminEventoRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Justificativa) || req.Justificativa.Trim().Length < 15)
            return Problem(statusCode: 422, title: "Justificativa é obrigatória (mínimo 15 caracteres).");

        var doc = await _db.DocumentosFiscais.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        if (doc.Status != StatusDocumento.AUTORIZADA)
            return Problem(statusCode: 409, title: "Documento não está AUTORIZADA.",
                detail: $"Status atual: {doc.Status}. Apenas documentos AUTORIZADA podem ser cancelados.");
        // Cancelamento por evento 110111 só existe para NF-e/NFC-e — NFS-e usa substituição de DPS.
        if (doc.Modelo is not (55 or 65))
            return Problem(statusCode: 409, title: "Cancelamento por evento não se aplica a NFS-e.",
                detail: "A NFS-e Nacional usa substituição de DPS (endpoint de substituição).");

        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString() : idempotencyKey;
        var evento = await RegistrarEventoAsync(doc, EventoCancelamento, idempotencyKey, req.Justificativa.Trim(), ct);

        if (evento.Status == "PROCESSADO")
        {
            // Replay idempotente de evento já transmitido.
            doc.Status = StatusDocumento.CANCELADA;
        }
        else
        {
            // Estado assíncrono: o ProcessarEventoJob transmite à SEFAZ e move
            // para CANCELADA (sucesso) ou ERRO_CANCELAMENTO (rejeição).
            doc.Status = StatusDocumento.CANCELAMENTO_PENDENTE;
        }
        doc.AtualizadoEm = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        EnfileirarSePendente(evento);

        return Ok(new
        {
            documentoId = doc.Id,
            eventoId = evento.Id,
            tipo = evento.TipoEvento,
            status = evento.Status,
            criadoEm = evento.CriadoEm
        });
    }

    /// <summary>CC-e via painel (só NF-e modelo 55, mesmas regras do tenant).</summary>
    [HttpPost("{id:guid}/carta-correcao")]
    public async Task<IActionResult> CartaCorrecao(
        Guid id,
        [FromBody] AdminEventoRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Correcao) || req.Correcao.Trim().Length < 15)
            return Problem(statusCode: 422, title: "Correção é obrigatória (mínimo 15 caracteres).");

        var doc = await _db.DocumentosFiscais.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        if (doc.Status != StatusDocumento.AUTORIZADA)
            return Problem(statusCode: 409, title: "Documento não está AUTORIZADA.",
                detail: $"Status atual: {doc.Status}.");
        if (doc.Modelo != 55)
            return Problem(statusCode: 409, title: "CC-e não suportada para este documento.",
                detail: "CC-e aplica-se apenas a NF-e (modelo 55). NFC-e deve ser cancelada e reemitida.");

        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString() : idempotencyKey;
        var evento = await RegistrarEventoAsync(doc, EventoCartaCorrecao, idempotencyKey, req.Correcao.Trim(), ct);
        EnfileirarSePendente(evento);

        return Ok(new
        {
            documentoId = doc.Id,
            eventoId = evento.Id,
            tipo = evento.TipoEvento,
            status = evento.Status,
            criadoEm = evento.CriadoEm
        });
    }

    private void EnfileirarSePendente(EventoFiscal evento)
    {
        // Replays idempotentes de eventos já em fila/terminal não reenfileiram.
        if (evento.Status == "PENDENTE")
            _jobs.Enqueue<ProcessarEventoJob>(j => j.ExecutarAsync(evento.Id, CancellationToken.None));
    }

    private async Task<EventoFiscal> RegistrarEventoAsync(
        DocumentoFiscal doc, string tipo, string idempotencyKey, string justificativa, CancellationToken ct)
    {
        var existente = await _eventoRepo.ObterPorIdempotencyAsync(
            doc.TenantId, doc.Id, tipo, idempotencyKey, ct);
        if (existente is not null) return existente;

        var evento = new EventoFiscal
        {
            Id = Guid.NewGuid(),
            TenantId = doc.TenantId,
            DocumentoId = doc.Id,
            TipoEvento = tipo,
            IdempotencyKey = idempotencyKey,
            Justificativa = justificativa,
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
            // Corrida: outra request com a mesma Idempotency-Key gravou primeiro —
            // devolve o evento vencedor (replay) em vez de 500.
            var vencedor = await _eventoRepo.ObterPorIdempotencyAsync(
                doc.TenantId, doc.Id, tipo, idempotencyKey, ct);
            if (vencedor is null) throw;
            return vencedor;
        }

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = doc.TenantId,
            Acao = $"{tipo}_SOLICITADO",
            RecursoId = doc.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress,
            Detalhe = $"{{\"eventoId\":\"{evento.Id}\",\"via\":\"admin\"}}"
        }, ct);
        await _db.SaveChangesAsync(ct);

        return evento;
    }
}
