namespace Fiscal.Core.Entities;

public class Certificado
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public byte[] PfxCriptografado { get; set; } = Array.Empty<byte>();
    public byte[] SenhaCriptografada { get; set; } = Array.Empty<byte>();
    public byte[] ChaveDekCriptografada { get; set; } = Array.Empty<byte>();

    public string Thumbprint { get; set; } = string.Empty;
    public DateOnly ValidoAte { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
