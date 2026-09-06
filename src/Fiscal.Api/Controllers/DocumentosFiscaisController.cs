using System.Text.Json;
using Fiscal.Api.Authentication;
using Fiscal.Core;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Core.Services;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fiscal.Api.Controllers;

[ApiController]
[Route("v1/documentos-fiscais")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.SchemeName)]
public class DocumentosFiscaisController : ControllerBase
{
    private readonly IRepositorioDocumentoFiscal _docRepo;
    private readonly IRepositorioCertificado _certRepo;
    private readonly IRepositorioTenant _tenantRepo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IFilaEmissao _fila;
    private readonly ValidadorConsistenciaFiscal _validador;
    private readonly ValidadorImpostosV2 _validadorImpostosV2;
    private readonly FiscalDbContext _db;
    private readonly IGeradorPdf _geradorPdf;
    private readonly ILogger<DocumentosFiscaisController> _logger;

    public DocumentosFiscaisController(
        IRepositorioDocumentoFiscal docRepo,
        IRepositorioCertificado certRepo,
        IRepositorioTenant tenantRepo,
        IRepositorioAuditoria auditoria,
        IFilaEmissao fila,
        ValidadorConsistenciaFiscal validador,
        ValidadorImpostosV2 validadorImpostosV2,
        FiscalDbContext db,
        IGeradorPdf geradorPdf,
        ILogger<DocumentosFiscaisController> logger)
    {
        _docRepo = docRepo;
        _certRepo = certRepo;
        _tenantRepo = tenantRepo;
        _auditoria = auditoria;
        _fila = fila;
        _validador = validador;
        _validadorImpostosV2 = validadorImpostosV2;
        _db = db;
        _geradorPdf = geradorPdf;
        _logger = logger;
    }

    [HttpPost("nfe")]
    public Task<IActionResult> EmitirNFe([FromBody] EmissaoRequest req, CancellationToken ct)
        => EmitirAsync(req, TipoDocumento.NFE, modelo: 55, ct);

    [HttpPost("nfce")]
    public Task<IActionResult> EmitirNFCe([FromBody] EmissaoRequest req, CancellationToken ct)
        => EmitirAsync(req, TipoDocumento.NFCE, modelo: 65, ct);

    /// <summary>NFS-e padrão Nacional (DPS). Emissão real entra na próxima sprint do roadmap NFS-e; sandbox via EmissorMock.</summary>
    [HttpPost("nfse")]
    public Task<IActionResult> EmitirNFSe([FromBody] EmissaoRequest req, CancellationToken ct)
        => EmitirAsync(req, TipoDocumento.NFSE, ModelosDocumento.NFSeNacional, ct);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var doc = await _docRepo.ObterPorIdAsync(id, tenantId, ct);
        if (doc is null) return NotFound();
        return Ok(ParaResponse(doc));
    }

    /// <summary>
    /// DANFE/DANFCe do documento (após AUTORIZADA/CANCELADA). Por padrão devolve
    /// o PDF binário; use ?formato=base64 para JSON { pdfBase64 }.
    /// </summary>
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, [FromQuery] string? formato, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var doc = await _docRepo.ObterPorIdAsync(id, tenantId, ct);
        if (doc is null) return NotFound();

        if (doc.Status is not (StatusDocumento.AUTORIZADA or StatusDocumento.CANCELADA))
            return Problem(statusCode: 409, title: "PDF disponível apenas para documentos AUTORIZADA ou CANCELADA.",
                detail: $"Status atual: {doc.Status}.");

        var tenant = await _tenantRepo.ObterPorIdAsync(tenantId, ct);
        if (tenant is null) return NotFound();

        var bytes = doc.Modelo switch
        {
            65 => await _geradorPdf.GerarDanfceAsync(doc, tenant, ct),
            ModelosDocumento.NFSeNacional => await _geradorPdf.GerarDanfseAsync(doc, tenant, ct),
            _ => await _geradorPdf.GerarDanfeAsync(doc, tenant, ct),
        };

        if (formato == "base64")
            return Ok(new { pdfBase64 = Convert.ToBase64String(bytes), contentType = "application/pdf" });

        return File(bytes, "application/pdf", $"danfe-{doc.Id:N}.pdf");
    }

    private async Task<IActionResult> EmitirAsync(EmissaoRequest req, TipoDocumento tipo, short modelo, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();

        if (!Request.Headers.TryGetValue("Idempotency-Key", out var idemKey) || string.IsNullOrWhiteSpace(idemKey))
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' é obrigatório.");
        if (idemKey!.ToString().Length > 100)
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' excede 100 caracteres.");

        if (string.IsNullOrWhiteSpace(req.Ambiente) ||
            (req.Ambiente != "producao" && req.Ambiente != "homologacao"))
            return Problem(statusCode: 422, title: "Ambiente inválido", detail: "Use 'producao' ou 'homologacao'.");

        // Defesa em profundidade: ambiente da request vs ambiente da key.
        var ambienteKey = HttpContext.GetAmbiente();
        var ambienteReq = req.Ambiente == "producao" ? Ambiente.Producao : Ambiente.Homologacao;
        if (ambienteKey != ambienteReq)
            return Problem(statusCode: 403, title: "API Key não autorizada para o ambiente solicitado.");

        // Grupos de imposto ambíguos: item declara a lista plana legada OU o
        // grupo tipado v2 — nunca os dois (docs/plano-evolucao-contrato-v2.md §8).
        for (var i = 0; i < req.Itens.Count; i++)
        {
            if (req.Itens[i].Impostos is not null && req.Itens[i].ImpostosV2 is not null)
                return Problem(statusCode: 400,
                    title: $"Item {i + 1}: informe apenas 'impostos' (legado) OU 'impostosV2' — nunca os dois.");
        }

        // 1) Idempotência — 2ª chamada com mesma key devolve estado atual.
        var existente = await _docRepo.ObterPorIdempotencyKeyAsync(tenantId, tipo, idemKey!, ct);
        if (existente is not null)
            return Ok(ParaResponse(existente));

        // 2) Validação aritmética.
        var docValidar = new DocumentoParaValidar(
            ValorTotal: req.Totais.ValorNota,
            Itens: req.Itens.Select(i => new ItemFiscal(
                i.Codigo, i.Quantidade, i.ValorUnitario, i.ValorTotal)).ToList(),
            Impostos: req.Itens
                .SelectMany(i => i.Impostos ?? new List<ImpostoDto>())
                .Select(im => new ImpostoFiscal(im.Cst, im.BaseCalculo, im.Aliquota, im.Valor))
                .ToList());
        var inconsistencias = _validador.Validar(docValidar);
        if (inconsistencias.Count > 0)
        {
            var primeira = inconsistencias[0];
            return Problem(
                statusCode: 422,
                title: "Inconsistência nos valores do documento",
                detail: primeira.Mensagem,
                extensions: new Dictionary<string, object?> { ["campo"] = primeira.Campo });
        }

        // 2b) Validação declarativa dos grupos v2 (CST/CSOSN, ST, FCP, DIFAL).
        var inconsistenciasV2 = _validadorImpostosV2.Validar(req.Itens);
        if (inconsistenciasV2.Count > 0)
        {
            var primeiraV2 = inconsistenciasV2[0];
            return Problem(
                statusCode: 422,
                title: "Inconsistência nos grupos de imposto v2",
                detail: primeiraV2.Mensagem,
                extensions: new Dictionary<string, object?> { ["campo"] = primeiraV2.Campo });
        }

        // 3) Reserva número + cria documento PENDENTE.
        var numero = await _docRepo.ReservarProximoNumeroAsync(
            tenantId, modelo, req.Serie, (short)ambienteReq, ct);

        var doc = new DocumentoFiscal
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            IdempotencyKey = idemKey!,
            Tipo = tipo,
            Ambiente = (short)ambienteReq,
            Modelo = modelo,
            Serie = req.Serie,
            Numero = numero,
            PayloadEntrada = JsonSerializer.Serialize(req),
            Status = StatusDocumento.PENDENTE,
            CriadoEm = DateTimeOffset.UtcNow,
            AtualizadoEm = DateTimeOffset.UtcNow
        };
        await _docRepo.AdicionarAsync(doc, ct);
        await _db.SaveChangesAsync(ct);

        // 4) Enfileira para processamento assíncrono.
        await _fila.EnfileirarAsync(doc.Id, ct);

        _logger.LogInformation("DocumentoFiscal {Id} aceito e enfileirado (status PENDENTE).", doc.Id);

        return Accepted($"/v1/documentos-fiscais/{doc.Id}", new
        {
            id = doc.Id,
            status = doc.Status.ToString(),
            ambiente = req.Ambiente,
            criadoEm = doc.CriadoEm,
            links = new
            {
                consulta = $"/v1/documentos-fiscais/{doc.Id}"
            }
        });
    }

    private static EmissaoResponse ParaResponse(DocumentoFiscal d) =>
        new(d.Id, d.Tipo.ToString(), d.Status.ToString(),
            d.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao",
            d.Serie, d.Numero, d.ChaveAcesso, d.ProtocoloAutorizacao,
            d.XmlAssinado, d.XmlRetornoSefaz, d.MotivoStatus,
            d.CriadoEm, d.AtualizadoEm);
}
