using Fiscal.Api.Infrastructure;
using Fiscal.Api.Services;
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
    private readonly IGeradorPdf _geradorPdf;
    private readonly ServicoReenvioDocumento _servicoReenvio;

    public AdminDocumentosController(
        FiscalDbContext db,
        IRepositorioEventoFiscal eventoRepo,
        IRepositorioAuditoria auditoria,
        IBackgroundJobClient jobs,
        IGeradorPdf geradorPdf,
        ServicoReenvioDocumento servicoReenvio)
    {
        _db = db;
        _eventoRepo = eventoRepo;
        _auditoria = auditoria;
        _jobs = jobs;
        _geradorPdf = geradorPdf;
        _servicoReenvio = servicoReenvio;
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
        [FromQuery] string? q,
        [FromQuery] string? ordenarPor,
        [FromQuery] string? ordem,
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

        // Campos de ordenação permitidos — lista fechada, não nome de coluna
        // vindo da query (isso seria injeção de ordenação).
        var camposOrdenacao = new[] { "criadoEm", "atualizadoEm", "numero", "serie" };
        string? campoOrdenacao = null;
        if (!string.IsNullOrWhiteSpace(ordenarPor))
        {
            campoOrdenacao = camposOrdenacao
                .FirstOrDefault(c => c.Equals(ordenarPor.Trim(), StringComparison.OrdinalIgnoreCase));
            if (campoOrdenacao is null)
            {
                return Problem(statusCode: 422, title: "Ordenação inválida",
                    detail: $"Use um destes campos: {string.Join(", ", camposOrdenacao)}.");
            }
        }

        var descendente = !string.Equals(ordem, "asc", StringComparison.OrdinalIgnoreCase);

        var query = _db.DocumentosFiscais.AsNoTracking();
        if (tenantId is not null) query = query.Where(d => d.TenantId == tenantId);
        if (statusFiltro is not null) query = query.Where(d => d.Status == statusFiltro);
        if (modelo is not null) query = query.Where(d => d.Modelo == modelo);
        if (de is not null) query = query.Where(d => d.CriadoEm >= de);
        if (ate is not null) query = query.Where(d => d.CriadoEm <= ate);

        // Busca livre: chave de acesso, número ou razão social do emitente.
        // O termo é aplicado no banco quando é numérico (chave/número) e via
        // projeção em memória quando é texto (razão social) — o SQLite da
        // suíte não traduz tudo, então o caminho é o mesmo do resto do repo.
        var busca = q?.Trim();
        if (!string.IsNullOrEmpty(busca))
        {
            var apenasDigitos = new string(busca.Where(char.IsDigit).ToArray());
            query = query.Where(d =>
                (d.ChaveAcesso != null && d.ChaveAcesso.Contains(busca)) ||
                (apenasDigitos.Length > 0 && d.Numero != null && d.Numero.ToString() == apenasDigitos) ||
                (d.Tenant != null && d.Tenant.RazaoSocial.Contains(busca)));
        }

        var total = await query.CountAsync(ct);

        var projecao = query.Select(d => new AdminDocListItem(
            d.Id, d.TenantId, d.Tenant!.RazaoSocial,
            d.Tipo.ToString(), d.Status.ToString(),
            d.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
            d.Modelo, d.Serie, d.Numero, d.ChaveAcesso, d.MotivoStatus,
            d.CriadoEm, d.AtualizadoEm));

        List<AdminDocListItem> itens;
        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // SQLite não traduz ORDER BY sobre DateTimeOffset. Materializa,
            // ordena e pagina em memória — volume da suíte é pequeno.
            var todos = await projecao.ToListAsync(ct);
            itens = Ordenar(todos, campoOrdenacao, descendente)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
        else
        {
            // Postgres: ordenar TEM que ser sobre as colunas da entidade, antes
            // da projeção para record — o EF não traduz OrderBy sobre um
            // record construído (tentar gerava 500 "could not be translated").
            IOrderedQueryable<DocumentoFiscal> ordenada = (campoOrdenacao, descendente) switch
            {
                ("atualizadoEm", true) => query.OrderByDescending(d => d.AtualizadoEm),
                ("atualizadoEm", false) => query.OrderBy(d => d.AtualizadoEm),
                ("numero", true) => query.OrderByDescending(d => d.Numero),
                ("numero", false) => query.OrderBy(d => d.Numero),
                ("serie", true) => query.OrderByDescending(d => d.Serie),
                ("serie", false) => query.OrderBy(d => d.Serie),
                (_, false) => query.OrderBy(d => d.CriadoEm),
                _ => query.OrderByDescending(d => d.CriadoEm),
            };

            itens = await ordenada
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new AdminDocListItem(
                    d.Id, d.TenantId, d.Tenant!.RazaoSocial,
                    d.Tipo.ToString(), d.Status.ToString(),
                    d.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
                    d.Modelo, d.Serie, d.Numero, d.ChaveAcesso, d.MotivoStatus,
                    d.CriadoEm, d.AtualizadoEm))
                .ToListAsync(ct);
        }

        return Ok(new { total, page, pageSize, itens });
    }

    private static IEnumerable<AdminDocListItem> Ordenar(
        IEnumerable<AdminDocListItem> itens, string? campo, bool descendente)
    {
        IEnumerable<AdminDocListItem> ordenada = campo switch
        {
            "atualizadoEm" => descendente
                ? itens.OrderByDescending(d => d.AtualizadoEm)
                : itens.OrderBy(d => d.AtualizadoEm),
            "numero" => descendente
                ? itens.OrderByDescending(d => d.Numero)
                : itens.OrderBy(d => d.Numero),
            "serie" => descendente
                ? itens.OrderByDescending(d => d.Serie)
                : itens.OrderBy(d => d.Serie),
            _ => descendente
                ? itens.OrderByDescending(d => d.CriadoEm)
                : itens.OrderBy(d => d.CriadoEm),
        };
        return ordenada;
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
            doc.AtualizadoEm,
            // Payload original: base do reenvio com correção (o operador
            // ajusta o que a SEFAZ rejeitou, não redigita a nota inteira).
            payloadEntrada = doc.PayloadEntrada
        });
    }

    /// <summary>
    /// DANFE/DANFCe/DANFSe do documento, pelo painel.
    ///
    /// Só existe PDF para AUTORIZADA/CANCELADA — a SEFAZ não autorizou os
    /// demais. 409 (mesma regra do endpoint de tenant): a UI usa o código
    /// para desabilitar o botão com o motivo, em vez de baixar um arquivo
    /// fiscal inválido.
    /// </summary>
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var doc = await _db.DocumentosFiscais.AsNoTracking()
            .Include(d => d.Tenant)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();

        if (doc.Status is not (StatusDocumento.AUTORIZADA or StatusDocumento.CANCELADA))
            return Problem(
                statusCode: 409,
                title: "PDF disponível apenas para documentos AUTORIZADA ou CANCELADA.",
                detail: $"Status atual: {doc.Status}.");

        if (doc.Tenant is null) return NotFound();

        var bytes = doc.Modelo switch
        {
            65 => await _geradorPdf.GerarDanfceAsync(doc, doc.Tenant, ct),
            ModelosDocumento.NFSeNacional => await _geradorPdf.GerarDanfseAsync(doc, doc.Tenant, ct),
            _ => await _geradorPdf.GerarDanfeAsync(doc, doc.Tenant, ct),
        };

        return File(bytes, "application/pdf", $"danfe-{doc.Id:N}.pdf");
    }

    /// <summary>
    /// Eventos fiscais do documento (cancelamento, CC-e) com o estado de
    /// cada um. O XML de retorno não vai cru na listagem — só o indicador
    /// <c>xmlDisponivel</c>, porque o payload é grande e a maioria dos usos
    /// nem precisa dele.
    /// </summary>
    [HttpGet("{id:guid}/eventos")]
    public async Task<IActionResult> Eventos(Guid id, CancellationToken ct)
    {
        if (!await _db.DocumentosFiscais.AnyAsync(d => d.Id == id, ct))
            return NotFound();

        // Sem ordenação por DateTimeOffset no SQLite (não traduz): ordena em
        // memória sobre uma projeção pequena — mesmo padrão do dashboard.
        var eventos = await _db.EventosFiscais.AsNoTracking()
            .Where(e => e.DocumentoId == id)
            .Select(e => new
            {
                e.Id,
                e.TipoEvento,
                e.Status,
                e.MotivoStatus,
                e.Protocolo,
                e.Tentativas,
                e.ProximaTentativaEm,
                e.CriadoEm,
                TemXml = e.XmlRetorno != null,
            })
            .ToListAsync(ct);

        return Ok(eventos
            .OrderByDescending(e => e.CriadoEm)
            .Select(e => new
            {
                e.Id,
                tipo = e.TipoEvento,
                e.Status,
                motivo = e.MotivoStatus,
                e.Protocolo,
                e.Tentativas,
                e.ProximaTentativaEm,
                e.CriadoEm,
                xmlDisponivel = e.TemXml,
            }));
    }

    public record AdminEventoRequest(string? Justificativa, string? Correcao);

    /// <summary>
    /// Reenvia um documento REJEITADA/DENEGADA/ERRO_INTERNO com o payload
    /// corrigido. Mantém o <b>mesmo número</b>: a rejeição não consumiu
    /// numeração na SEFAZ. Compartilha a validação e a regra com o endpoint
    /// do integrador — o painel não pode aceitar payload que o integrador
    /// recusaria.
    /// </summary>
    [HttpPost("{id:guid}/reenviar")]
    public async Task<IActionResult> Reenviar(
        Guid id,
        [FromBody] EmissaoRequest req,
        CancellationToken ct)
    {
        var doc = await _db.DocumentosFiscais.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();

        var resultado = await _servicoReenvio.ReenviarAsync(doc, req, apiKeyId: null, ct);
        if (!resultado.Ok)
            return Problem(statusCode: resultado.Status, title: resultado.Titulo, detail: resultado.Detalhe);

        var atualizado = resultado.Documento!;
        return Accepted($"/v1/admin/documentos-fiscais/{atualizado.Id}", new
        {
            atualizado.Id,
            status = atualizado.Status.ToString(),
            atualizado.Numero,
            atualizado.Serie,
            atualizado.AtualizadoEm,
            links = new { consulta = $"/v1/admin/documentos-fiscais/{atualizado.Id}" }
        });
    }

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
