using Fiscal.Core.Enums;

namespace Fiscal.Core.Entities;

public class DocumentoFiscal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
    public TipoDocumento Tipo { get; set; }
    public short Ambiente { get; set; }
    public short? Modelo { get; set; }
    public short? Serie { get; set; }
    public long? Numero { get; set; }
    public string? ChaveAcesso { get; set; }
    public string PayloadEntrada { get; set; } = "{}";
    public string? XmlGerado { get; set; }
    public string? XmlAssinado { get; set; }
    public string? XmlRetornoSefaz { get; set; }
    public string? ProtocoloAutorizacao { get; set; }
    public string? ReciboLote { get; set; }
    public StatusDocumento Status { get; set; } = StatusDocumento.PENDENTE;
    public string? MotivoStatus { get; set; }
    public int Tentativas { get; set; }
    public string? ModoContingencia { get; set; }
    public DateTimeOffset? ProximaTentativaEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
}
