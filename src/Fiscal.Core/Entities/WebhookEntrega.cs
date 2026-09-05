namespace Fiscal.Core.Entities;

/// <summary>
/// Outbox de webhooks: linhas gravadas na MESMA transação que muda o status
/// do documento/evento, entregues por job próprio (retry de webhook separado
/// do retry de SEFAZ — ver docs/planejamento-detalhado-api-fiscal.md).
/// </summary>
public class WebhookEntrega
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    // Null para webhooks futuros que não amarram a um documento.
    public Guid? DocumentoId { get; set; }
    public DocumentoFiscal? Documento { get; set; }

    // documento.autorizado | documento.rejeitado | documento.denegado |
    // documento.cancelado | documento.carta_correcao
    public string TipoEvento { get; set; } = string.Empty;

    // Envelope JSON completo (grava na criação; a assinatura HMAC é calculada
    // na entrega com o timestamp do header).
    public string Payload { get; set; } = "{}";

    // PENDENTE | ENTREGUE | FALHA (terminal após esgotar tentativas).
    public string Status { get; set; } = "PENDENTE";
    public int Tentativas { get; set; }
    public DateTimeOffset? ProximaTentativaEm { get; set; }
    public int? UltimoStatusCode { get; set; }
    public string? UltimoErro { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EntregueEm { get; set; }
}
