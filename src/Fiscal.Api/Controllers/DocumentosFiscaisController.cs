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
using Microsoft.EntityFrameworkCore;

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
    private readonly ValidadorNfseDps _validadorNfseDps;
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
        ValidadorNfseDps validadorNfseDps,
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
        _validadorNfseDps = validadorNfseDps;
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

    /// <summary>NFS-e padrão Nacional (DPS). Rota legada (EmissaoRequest) — funciona só em sandbox; transmissão real via POST nfse/dps.</summary>
    [HttpPost("nfse")]
    public Task<IActionResult> EmitirNFSe([FromBody] EmissaoRequest req, CancellationToken ct)
        => EmitirAsync(req, TipoDocumento.NFSE, ModelosDocumento.NFSeNacional, ct);

    /// <summary>
    /// NFS-e padrão Nacional com transmissão DPS real (layout 1.01, síncrona).
    /// Corpo: NfseDpsRequest (docs/integracao-api.md). Fora de sandbox exige certificado A1.
    /// </summary>
    [HttpPost("nfse/dps")]
    public async Task<IActionResult> EmitirNFSeDps([FromBody] NfseDpsRequest req, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var idemKey) || string.IsNullOrWhiteSpace(idemKey))
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' é obrigatório.");
        if (idemKey!.ToString().Length > 100)
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' excede 100 caracteres.");

        var inconsistencias = _validadorNfseDps.Validar(req);
        if (inconsistencias.Count > 0)
        {
            return Problem(statusCode: 422,
                title: "Inconsistência no DPS",
                detail: inconsistencias[0].Mensagem,
                extensions: new Dictionary<string, object?> { ["campo"] = inconsistencias[0].Campo });
        }

        var ambienteReq = req.Ambiente == "producao" ? Ambiente.Producao : Ambiente.Homologacao;
        var ambienteKey = HttpContext.GetAmbiente();
        if (ambienteKey != ambienteReq)
            return Problem(statusCode: 403, title: "API Key não autorizada para o ambiente solicitado.");

        return await AceitarAsync(
            idemKey!, TipoDocumento.NFSE, ModelosDocumento.NFSeNacional, ambienteReq,
            req.Serie, JsonSerializer.Serialize(req), ct);
    }

    /// <summary>
    /// Substituição de NFS-e (roadmap item 3): emite um novo DPS apontando a
    /// NFS-e autorizada do path (grupo &lt;subst&gt;). A SEFAZ desativa a original
    /// quando autoriza a substituta — o novo documento segue o fluxo normal.
    /// </summary>
    [HttpPost("{id:guid}/substituicao")]
    public async Task<IActionResult> SubstituirNFSe(
        Guid id, [FromBody] NfseDpsSubstituicaoRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();

        if (!Request.Headers.TryGetValue("Idempotency-Key", out var idemKey) || string.IsNullOrWhiteSpace(idemKey))
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' é obrigatório.");
        if (idemKey!.ToString().Length > 100)
            return Problem(statusCode: 400, title: "Header 'Idempotency-Key' excede 100 caracteres.");

        if (req.CMotivo is not (1 or 2 or 3 or 4 or 5 or 99))
            return Problem(statusCode: 422, title: "cMotivo inválido",
                detail: "Use 1, 2, 3, 4, 5 ou 99 (outros — exige xMotivo).");
        if (req.CMotivo == 99 && string.IsNullOrWhiteSpace(req.XMotivo))
            return Problem(statusCode: 422, title: "xMotivo obrigatório", detail: "Substituição com cMotivo 99 exige xMotivo.");

        var original = await _docRepo.ObterPorIdAsync(id, tenantId, ct);
        if (original is null) return NotFound();
        if (original.Tipo != TipoDocumento.NFSE)
            return Problem(statusCode: 422, title: "Substituição disponível apenas para NFS-e",
                detail: $"Documento {id} é do tipo {original.Tipo}.");
        if (original.Status != StatusDocumento.AUTORIZADA || string.IsNullOrWhiteSpace(original.ChaveAcesso))
            return Problem(statusCode: 409, title: "Documento não elegível para substituição",
                detail: $"Status atual: {original.Status}. Substituição exige NFS-e AUTORIZADA.");

        var dps = req.Dps;
        var inconsistencias = _validadorNfseDps.Validar(dps);
        if (inconsistencias.Count > 0)
        {
            return Problem(statusCode: 422,
                title: "Inconsistência no DPS substituto",
                detail: inconsistencias[0].Mensagem,
                extensions: new Dictionary<string, object?> { ["campo"] = inconsistencias[0].Campo });
        }

        // Ambiente da substituta = ambiente da original (chave de 50 carrega tpAmb).
        var ambienteOriginal = (Ambiente)original.Ambiente;

        var existente = await _docRepo.ObterPorIdempotencyKeyAsync(tenantId, TipoDocumento.NFSE, idemKey!, ct);
        if (existente is not null)
            return Ok(ParaResponse(existente));

        var numero = await _docRepo.ReservarProximoNumeroAsync(
            tenantId, ModelosDocumento.NFSeNacional, dps.Serie, original.Ambiente, ct);

        var payload = JsonSerializer.Serialize(new
        {
            dps,
            cMotivo = req.CMotivo,
            xMotivo = req.XMotivo,
            chaveSubstituida = original.ChaveAcesso,
        });

        var doc = new DocumentoFiscal
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            IdempotencyKey = idemKey!,
            Tipo = TipoDocumento.NFSE,
            Ambiente = original.Ambiente,
            Modelo = ModelosDocumento.NFSeNacional,
            Serie = dps.Serie,
            Numero = numero,
            PayloadEntrada = payload,
            Status = StatusDocumento.PENDENTE,
            CriadoEm = DateTimeOffset.UtcNow,
            AtualizadoEm = DateTimeOffset.UtcNow
        };
        await _docRepo.AdicionarAsync(doc, ct);
        await _db.SaveChangesAsync(ct);

        await _fila.EnfileirarAsync(doc.Id, ct);

        _logger.LogInformation("Substituição de NFS-e {Original} aceita → novo documento {Novo} (PENDENTE).",
            original.Id, doc.Id);

        return Accepted($"/v1/documentos-fiscais/{doc.Id}", new
        {
            id = doc.Id,
            substituidaId = original.Id,
            status = doc.Status.ToString(),
            ambiente = ambienteOriginal == Ambiente.Producao ? "producao" : "homologacao",
            criadoEm = doc.CriadoEm,
            links = new { consulta = $"/v1/documentos-fiscais/{doc.Id}" }
        });
    }

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

        // v2 F4: devolução exige NF-e referenciada; chaves com 44 dígitos.
        if (req.Finalidade?.Trim().ToLowerInvariant() == "devolucao" &&
            req.NfesReferenciadas is not { Count: > 0 })
        {
            return Problem(statusCode: 422, title: "Devolução exige NF-e referenciada",
                detail: "Informe 'nfesReferenciadas' com a(s) chave(s) de 44 dígitos da(s) NF-e devolvida(s).");
        }
        for (var i = 0; i < (req.NfesReferenciadas?.Count ?? 0); i++)
        {
            var chave = new string((req.NfesReferenciadas![i].ChaveAcesso ?? "").Where(char.IsDigit).ToArray());
            if (chave.Length != 44)
                return Problem(statusCode: 422, title: "Chave de NF-e referenciada inválida",
                    detail: $"nfesReferenciadas[{i}]: esperado 44 dígitos, recebido {chave.Length}.");
        }

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

        // 2) Validação aritmética. Com a fórmula v2 do total (docs/plano-
        // evolucao-contrato-v2.md §5.2), a soma legada (itens = valorNota)
        // não se aplica — confere-se contra os brutos para validar o resto.
        var docValidar = new DocumentoParaValidar(
            ValorTotal: ValidadorImpostosV2.FormulaV2Ativa(req.Totais, req.Itens)
                ? req.Itens.Sum(i => i.ValorTotal)
                : req.Totais.ValorNota,
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

        // 2b) Validação declarativa dos grupos v2 (CST/CSOSN, ST, FCP, DIFAL, IPI/PIS/COFINS).
        var inconsistenciasV2 = _validadorImpostosV2.Validar(req.Itens, nfce: modelo == 65);
        if (inconsistenciasV2.Count > 0)
        {
            var primeiraV2 = inconsistenciasV2[0];
            return Problem(
                statusCode: 422,
                title: "Inconsistência nos grupos de imposto v2",
                detail: primeiraV2.Mensagem,
                extensions: new Dictionary<string, object?> { ["campo"] = primeiraV2.Campo });
        }

        // 2c) Fórmula do total da nota — v2 quando qualquer campo novo está presente.
        var inconsistenciasTotais = _validadorImpostosV2.ValidarTotais(req.Totais, req.Itens);
        if (inconsistenciasTotais.Count > 0)
        {
            return Problem(
                statusCode: 422,
                title: "Inconsistência no total da nota",
                detail: inconsistenciasTotais[0].Mensagem,
                extensions: new Dictionary<string, object?> { ["campo"] = "valorNota" });
        }

        // NFC-e offline (tpEmis 9, v2 §7): marca o documento para o mapper
        // gerar o XML com contingência offline — a transmissão (e a janela de
        // 24h) seguem pelo fluxo de contingência do Worker.
        string? modoContingencia = null;
        if (modelo == 65 && req.ContingenciaOffline == true)
        {
            modoContingencia = "OFFLINE";
        }
        else if (modelo != 65 && req.ContingenciaOffline == true)
        {
            return Problem(statusCode: 422, title: "contingenciaOffline só se aplica a NFC-e.",
                detail: "NF-e usa contingência SVC/EPEC automática do servidor, não emissão offline.");
        }

        return await AceitarAsync(
            idemKey!, tipo, modelo, ambienteReq, req.Serie, JsonSerializer.Serialize(req), ct, modoContingencia);
    }

    /// <summary>
    /// Fim comum do fluxo de aceitação: idempotência, reserva de número, doc
    /// PENDENTE e enfileiramento. PayloadEntrada já validado pelo chamador.
    /// </summary>
    private async Task<IActionResult> AceitarAsync(
        string idemKey, TipoDocumento tipo, short modelo, Ambiente ambienteReq,
        short serie, string payloadJson, CancellationToken ct, string? modoContingencia = null)
    {
        var tenantId = HttpContext.GetTenantId();

        // 1) Idempotência — 2ª chamada com mesma key devolve estado atual.
        var existente = await _docRepo.ObterPorIdempotencyKeyAsync(tenantId, tipo, idemKey, ct);
        if (existente is not null)
            return Ok(ParaResponse(existente));

        var numero = await _docRepo.ReservarProximoNumeroAsync(
            tenantId, modelo, serie, (short)ambienteReq, ct);

        var doc = new DocumentoFiscal
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            IdempotencyKey = idemKey,
            Tipo = tipo,
            Ambiente = (short)ambienteReq,
            Modelo = modelo,
            Serie = serie,
            Numero = numero,
            PayloadEntrada = payloadJson,
            Status = StatusDocumento.PENDENTE,
            ModoContingencia = modoContingencia,
            CriadoEm = DateTimeOffset.UtcNow,
            AtualizadoEm = DateTimeOffset.UtcNow
        };
        await _docRepo.AdicionarAsync(doc, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Corrida: outra request com a mesma Idempotency-Key gravou primeiro —
            // devolve o documento vencedor (replay) em vez de 500. O número
            // reservado nesta corrida fica com gap — aceitável.
            var vencedor = await _docRepo.ObterPorIdempotencyKeyAsync(tenantId, tipo, idemKey, ct);
            if (vencedor is null) throw;
            return Ok(ParaResponse(vencedor));
        }

        await _fila.EnfileirarAsync(doc.Id, ct);

        _logger.LogInformation("DocumentoFiscal {Id} aceito e enfileirado (status PENDENTE).", doc.Id);

        return Accepted($"/v1/documentos-fiscais/{doc.Id}", new
        {
            id = doc.Id,
            status = doc.Status.ToString(),
            ambiente = ambienteReq == Ambiente.Producao ? "producao" : "homologacao",
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
