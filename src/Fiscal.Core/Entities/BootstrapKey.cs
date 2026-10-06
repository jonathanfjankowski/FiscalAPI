namespace Fiscal.Core.Entities;

/// <summary>
/// Chave "master" de provisionamento (header X-Bootstrap-Key em POST /v1/empresas).
/// Só o hash PBKDF2 e o prefixo ficam no banco — a chave em claro aparece uma
/// única vez (seed ou rotação pelo painel admin). Um único registro ativo.
/// </summary>
public class BootstrapKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Prefixo { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
