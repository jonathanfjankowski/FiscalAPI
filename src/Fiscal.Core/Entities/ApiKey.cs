namespace Fiscal.Core.Entities;

public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string Prefixo { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public short Ambiente { get; set; }
    public bool Ativa { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevogadoEm { get; set; }
}
