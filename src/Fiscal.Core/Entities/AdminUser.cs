namespace Fiscal.Core.Entities;

/// <summary>
/// Operador do painel administrativo. Não é um tenant: autentica via
/// /v1/admin/auth/login (JWT) e não possui credenciais de emissão.
/// </summary>
public class AdminUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public bool Ativo { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
