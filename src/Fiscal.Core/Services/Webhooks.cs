using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fiscal.Core.Entities;

namespace Fiscal.Core.Services;

/// <summary>Tipos de evento de webhook e montagem do envelope JSON.</summary>
public static class Webhooks
{
    public const string EventoAutorizado = "documento.autorizado";
    public const string EventoRejeitado = "documento.rejeitado";
    public const string EventoDenegado = "documento.denegado";
    public const string EventoCancelado = "documento.cancelado";
    public const string EventoCartaCorrecao = "documento.carta_correcao";
    public const string NotaRecebida = "nota.recebida";
    public const string ManifestacaoProcessada = "manifestacao.processada";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Envelope entregue no corpo do POST. O `timestamp` (unix seconds) entra
    /// na assinatura HMAC — o consumidor deve rejeitar timestamps fora de uma
    /// janela (recomendado: 5 min) para se proteger de replay.
    /// </summary>
    public static string PayloadPara(DocumentoFiscal doc, string tipoEvento, DateTimeOffset agora) =>
        JsonSerializer.Serialize(new
        {
            tipo = tipoEvento,
            timestamp = agora.ToUnixTimeSeconds(),
            documento = new
            {
                doc.Id,
                tipo = doc.Tipo.ToString(),
                status = doc.Status.ToString(),
                ambiente = doc.Ambiente == (short)Enums.Ambiente.Producao ? "producao" : "homologacao",
                doc.Serie,
                doc.Numero,
                doc.ChaveAcesso,
                doc.ProtocoloAutorizacao,
                doc.MotivoStatus,
                doc.CriadoEm,
                doc.AtualizadoEm,
            },
        }, JsonOpts);

    public static string PayloadNotaRecebida(Entities.NotaRecebida nota, DateTimeOffset agora) =>
        JsonSerializer.Serialize(new
        {
            tipo = NotaRecebida,
            timestamp = agora.ToUnixTimeSeconds(),
            nota = new
            {
                nota.Id,
                nota.Chave,
                nota.Nsu,
                nota.TipoSchema,
                nota.CnpjEmitente,
                nota.NomeEmitente,
                nota.Valor,
                nota.EmitidaEm,
            },
        }, JsonOpts);

    public static string PayloadManifestacao(
        Entities.NotaRecebida nota, Entities.ManifestacaoDestinatario manifestacao, DateTimeOffset agora) =>
        JsonSerializer.Serialize(new
        {
            tipo = ManifestacaoProcessada,
            timestamp = agora.ToUnixTimeSeconds(),
            manifestacao = new
            {
                manifestacao.Id,
                manifestacao.Tipo,
                manifestacao.Status,
                manifestacao.Protocolo,
                manifestacao.MotivoStatus,
            },
            nota = new { nota.Id, nota.Chave, nota.Valor },
        }, JsonOpts);
}

/// <summary>
/// HMAC-SHA256 sobre "{timestamp}.{payload}" com o webhook_secret do tenant.
/// Header X-Fiscal-Signature: sha256=&lt;hex&gt; — o consumidor recalcula com
/// o valor do header X-Fiscal-Timestamp e o corpo cru do POST.
/// </summary>
public static class AssinadorWebhook
{
    public static string CalcularAssinatura(string secret, long timestampUnix, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var conteudo = $"{timestampUnix}.{payload}";
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(conteudo))).ToLowerInvariant();
    }
}
