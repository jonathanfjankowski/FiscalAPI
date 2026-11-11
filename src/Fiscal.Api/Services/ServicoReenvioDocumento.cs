using System.Text.Json;
using Fiscal.Api.Infrastructure;
using Fiscal.Core.Contracts;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Services;

/// <summary>
/// Reenvio de documento REJEITADA/DENEGADA/ERRO_INTERNO com payload corrigido.
///
/// Compartilhado entre o endpoint do integrador e o do painel: o mesmo
/// documento, a mesma validação, a mesma regra — <b>o número é mantido</b>
/// (a rejeição não consumiu numeração na SEFAZ). Autorizada e cancelada nunca
/// reenviam: autorizada é cancelada, nunca reemitida.
/// </summary>
public class ServicoReenvioDocumento
{
    private readonly IRepositorioDocumentoFiscal _docRepo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IFilaEmissao _fila;
    private readonly FiscalDbContext _db;
    private readonly ValidadorEmissao _validadorEmissao;
    private readonly ILogger<ServicoReenvioDocumento> _logger;

    public ServicoReenvioDocumento(
        IRepositorioDocumentoFiscal docRepo,
        IRepositorioAuditoria auditoria,
        IFilaEmissao fila,
        FiscalDbContext db,
        ValidadorEmissao validadorEmissao,
        ILogger<ServicoReenvioDocumento> logger)
    {
        _docRepo = docRepo;
        _auditoria = auditoria;
        _fila = fila;
        _db = db;
        _validadorEmissao = validadorEmissao;
        _logger = logger;
    }

    /// <summary>
    /// Resultado do reenvio: o documento atualizado, ou o problema a devolver.
    /// </summary>
    public record Resultado(
        int Status,
        string Titulo,
        string? Detalhe = null,
        Core.Entities.DocumentoFiscal? Documento = null)
    {
        public bool Ok => Status == 0;
        public static Resultado Sucesso(Core.Entities.DocumentoFiscal doc) => new(0, "", Documento: doc);
    }

    /// <summary>
    /// Reenvia o documento. <paramref name="esperarStatus"/> restringe os
    /// status aceitos; o chamador decide (admin e tenant usam a mesma lista).
    /// </summary>
    public async Task<Resultado> ReenviarAsync(
        Core.Entities.DocumentoFiscal doc,
        EmissaoRequest req,
        Guid? apiKeyId,
        CancellationToken ct)
    {
        if (doc.Status is not (StatusDocumento.REJEITADA or StatusDocumento.DENEGADA
            or StatusDocumento.ERRO_INTERNO))
        {
            return new Resultado(409, "Documento não aceita reenvio",
                $"Status atual: {doc.Status}. Só documentos REJEITADA, DENEGADA ou ERRO_INTERNO " +
                "podem ser corrigidos e reenviados — autorizada é cancelada, nunca reenviada.");
        }

        var validacao = _validadorEmissao.Validar(req, nfce: doc.Modelo == 65);
        if (!validacao.Ok)
            return new Resultado(validacao.Status, validacao.Titulo, validacao.Detalhe);

        // Ambiente da request tem que bater com o do documento: um documento
        // emitido em produção não pode ser reenviado em homologação.
        var ambienteReq = req.Ambiente == "producao" ? Ambiente.Producao : Ambiente.Homologacao;
        if ((short)ambienteReq != doc.Ambiente)
        {
            return new Resultado(422, "Ambiente divergente",
                $"O documento foi criado no ambiente " +
                $"{(doc.Ambiente == (short)Ambiente.Producao ? "producao" : "homologacao")} — " +
                "o reenvio precisa usar o mesmo.");
        }

        doc.PayloadEntrada = JsonSerializer.Serialize(req);
        doc.Status = StatusDocumento.PENDENTE;
        doc.MotivoStatus = null;
        doc.XmlAssinado = null;
        doc.XmlRetornoSefaz = null;
        doc.Tentativas = 0;
        doc.AtualizadoEm = DateTimeOffset.UtcNow;

        await _docRepo.AtualizarAsync(doc, ct);
        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Core.Entities.Auditoria
        {
            TenantId = doc.TenantId,
            ApiKeyId = apiKeyId,
            RecursoId = doc.Id,
            Acao = "DOCUMENTO_REENVIADO",
            Detalhe = $"{{\"numero\":{doc.Numero},\"serie\":{doc.Serie}}}"
        }, ct);
        await _db.SaveChangesAsync(ct);

        await _fila.EnfileirarAsync(doc.Id, ct);

        _logger.LogInformation(
            "DocumentoFiscal {Id} reenviado (número {Numero} mantido) e reenfileirado.",
            doc.Id, doc.Numero);

        return Resultado.Sucesso(doc);
    }
}