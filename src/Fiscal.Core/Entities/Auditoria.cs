using System.Net;

namespace Fiscal.Core.Entities;

public class Auditoria
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public Guid? ApiKeyId { get; set; }
    public string Acao { get; set; } = string.Empty;
    public Guid? RecursoId { get; set; }
    public IPAddress? IpOrigem { get; set; }
    public string? Detalhe { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
