using Fiscal.Api.Authentication;
using Fiscal.Api.Contracts;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Fiscal.Worker.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Fiscal.Api.Controllers;

[ApiController]
[Route("v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.SchemeName)]
public class EventosController : ControllerBase
{
    private const string EventoCancelamento = "CANCELAMENTO";
    private const string EventoCartaCorrecao = "CCE";
    private const string EventoInutilizacao = "INUTILIZACAO";

    private readonly IRepositorioDocumentoFiscal _docRepo;
    private readonly IRepositorioEventoFiscal _eventoRepo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly FiscalDbContext _db;
    private readonly IBackgroundJobClient _jobs;

    public EventosController(
        IRepositorioDocumentoFiscal docRepo,
        IRepositorioEventoFiscal eventoRepo,
        IRepositorioAuditoria auditoria,
        FiscalDbContext db,
        IBackgroundJobClient jobs)
    {
        _docRepo = docRepo;
        _eventoRepo = eventoRepo;
        _auditoria = auditoria;
        _db = db;
        _jobs = jobs;
    }

    [HttpPost("documentos-fiscais/{id:guid}/cancelamento")]
    public async Task<IActionResult> Cancelar(
        Guid id,
        [FromBody] CancelamentoRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' é obrigatório.");

        var (doc, error) = await ObterDocAutorizadoAsync(id, ct);
        if (error is not null) return error;

        // Cancelamento via evento 110111 só existe para NF-e/NFC-e. NFS-e
        // Nacional usa substituição (POST .../substituicao) — sprint NFS-e.
        if (doc!.Modelo is not (55 or 65))
            return Problem(statusCode: 409, title: "Cancelamento por evento não se aplica a NFS-e.",
                detail: "A NFS-e Nacional usa substituição de DPS (endpoint de substituição, próxima sprint).");

        var evento = await RegistrarOuObterEventoAsync(
            doc!, EventoCancelamento, idempotencyKey, req.Justificativa, ct);

        if (evento.Status == "PROCESSADO")
        {
            doc!.Status = StatusDocumento.CANCELADA;
        }
        else
        {
            // Estado assíncrono: o ProcessarEventoJob transmite à SEFAZ e
            // move para CANCELADA (sucesso) ou ERRO_CANCELAMENTO (rejeição).
            doc!.Status = StatusDocumento.CANCELAMENTO_PENDENTE;
            EnfileirarSePendente(evento);
        }
        doc.AtualizadoEm = DateTimeOffset.UtcNow;
        await _docRepo.AtualizarAsync(doc, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            documentoId = doc.Id,
            eventoId = evento.Id,
            tipo = evento.TipoEvento,
            status = evento.Status,
            criadoEm = evento.CriadoEm
        });
    }

    [HttpPost("documentos-fiscais/{id:guid}/carta-correcao")]
    public async Task<IActionResult> CartaCorrecao(
        Guid id,
        [FromBody] CartaCorrecaoRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' é obrigatório.");

        var (doc, error) = await ObterDocAutorizadoAsync(id, ct);
        if (error is not null) return error;

        // CC-e só existe para NF-e (modelo 55).
        if (doc!.Modelo != 55)
        {
            return Problem(
                statusCode: 409,
                title: "Carta de Correção não suportada para este tipo de documento.",
                detail: "CC-e aplica-se apenas a NF-e (modelo 55). NFC-e e NFS-e devem ser canceladas e reemitidas.");
        }

        var evento = await RegistrarOuObterEventoAsync(
            doc, EventoCartaCorrecao, idempotencyKey, req.Correcao, ct);
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

    [HttpPost("inutilizacoes")]
    public async Task<IActionResult> Inutilizar(
        [FromBody] InutilizacaoRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' é obrigatório.");

        if (req.Ambiente != "producao" && req.Ambiente != "homologacao")
            return Problem(statusCode: 422, title: "Ambiente inválido", detail: "Use 'producao' ou 'homologacao'.");

        if (req.NumeroFinal < req.NumeroInicial)
            return Problem(statusCode: 422, title: "Faixa inválida", detail: "numeroFinal deve ser >= numeroInicial.");

        var ambienteKey = HttpContext.GetAmbiente();
        var ambienteReq = req.Ambiente == "producao" ? Ambiente.Producao : Ambiente.Homologacao;
        if (ambienteKey != ambienteReq)
            return Problem(statusCode: 403, title: "API Key não autorizada para o ambiente solicitado.");

        var tenantId = HttpContext.GetTenantId();
        // Inutilização não amarra a um documento existente (DocumentoId = null).
        // Idempotência por (tenant, tipo, idempotency_key).
        var existente = await _eventoRepo.ObterPorIdempotencyAsync(
            tenantId, null, EventoInutilizacao, idempotencyKey, ct);
        if (existente is not null)
        {
            return Ok(new { eventoId = existente.Id, status = existente.Status, criadoEm = existente.CriadoEm });
        }

        var evento = new EventoFiscal
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DocumentoId = null,
            TipoEvento = EventoInutilizacao,
            IdempotencyKey = idempotencyKey,
            Justificativa = req.Justificativa,
            DadosEvento = JsonSerializer.Serialize(new InutilizacaoDados(
                req.Modelo, req.Serie, req.NumeroInicial, req.NumeroFinal, req.Ambiente)),
            Status = "PENDENTE",
            CriadoEm = DateTimeOffset.UtcNow
        };
        await _eventoRepo.AdicionarAsync(evento, ct);
        await _db.SaveChangesAsync(ct);
        EnfileirarSePendente(evento);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenantId,
            ApiKeyId = HttpContext.GetApiKeyId(),
            Acao = "INUTILIZACAO_SOLICITADA",
            RecursoId = evento.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress,
            Detalhe = $"{{\"modelo\":{req.Modelo},\"serie\":{req.Serie},\"de\":{req.NumeroInicial},\"ate\":{req.NumeroFinal}}}"
        }, ct);

        return Accepted($"/v1/inutilizacoes/{evento.Id}", new
        {
            eventoId = evento.Id,
            tipo = evento.TipoEvento,
            status = evento.Status,
            criadoEm = evento.CriadoEm,
            modelo = req.Modelo,
            serie = req.Serie,
            numeroInicial = req.NumeroInicial,
            numeroFinal = req.NumeroFinal
        });
    }

    [HttpGet("inutilizacoes/{id:guid}")]
    public async Task<IActionResult> ObterInutilizacao(Guid id, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var evento = await _eventoRepo.ObterPorIdAsync(id, tenantId, ct);
        if (evento is null || evento.TipoEvento != EventoInutilizacao)
            return NotFound();

        return Ok(new
        {
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

    private async Task<(DocumentoFiscal? doc, IActionResult? error)> ObterDocAutorizadoAsync(
        Guid id, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var doc = await _docRepo.ObterPorIdAsync(id, tenantId, ct);
        if (doc is null) return (null, NotFound());

        if (doc.Status != StatusDocumento.AUTORIZADA)
        {
            return (null, Problem(
                statusCode: 409,
                title: "Documento não está em estado válido para este evento.",
                detail: $"Status atual: {doc.Status}. Apenas documentos AUTORIZADA podem receber cancelamento/CC-e."));
        }

        return (doc, null);
    }

    private async Task<EventoFiscal> RegistrarOuObterEventoAsync(
        DocumentoFiscal doc, string tipo, string idempotencyKey, string? justificativa, CancellationToken ct)
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
        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = doc.TenantId,
            ApiKeyId = HttpContext.GetApiKeyId(),
            Acao = $"{tipo}_SOLICITADO",
            RecursoId = doc.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress,
            Detalhe = $"{{\"eventoId\":\"{evento.Id}\",\"justificativaResumo\":\"{Resumo(justificativa)}\"}}"
        }, ct);

        return evento;
    }

    private static string Resumo(string? texto) =>
        texto is null ? "" : texto.Length <= 80 ? texto : texto[..80].Replace("\"", "'");
}
