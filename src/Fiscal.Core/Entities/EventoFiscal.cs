namespace Fiscal.Core.Entities;

public class EventoFiscal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    // Null para eventos que não amarram a um documento (inutilização de faixa).
    public Guid? DocumentoId { get; set; }
    public DocumentoFiscal? Documento { get; set; }

    public string TipoEvento { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Justificativa { get; set; }

    // Dados extras para a transmissão (hoje: JSON do pedido de inutilização —
    // modelo/serie/faixa/ambiente, que não amarram a um documento).
    public string? DadosEvento { get; set; }

    public string? XmlRetorno { get; set; }
    public string? Protocolo { get; set; }
    public string? MotivoStatus { get; set; }
    public string Status { get; set; } = "PENDENTE";
    public int Tentativas { get; set; }
    public DateTimeOffset? ProximaTentativaEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
