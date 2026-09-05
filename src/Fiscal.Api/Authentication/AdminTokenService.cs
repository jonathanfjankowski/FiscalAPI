using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fiscal.Core.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Fiscal.Api.Authentication;

public interface IAdminTokenService
{
    (string Token, DateTimeOffset ExpiraEm) CriarToken(AdminUser user);
}

public class AdminTokenService : IAdminTokenService
{
    public const string Issuer = "fiscal-api";
    public const string Audience = "fiscal-api-admin";

    private readonly IConfiguration _cfg;

    public AdminTokenService(IConfiguration cfg) => _cfg = cfg;

    /// <summary>
    /// Resolve a chave de assinatura de JWTs de admin. Lazy (por request)
    /// porque em produção a env ADMIN_JWT_SECRET pode não existir — nesse caso
    /// falhamos com mensagem clara em vez de derrubar a API no startup.
    /// </summary>
    public static SymmetricSecurityKey ResolveSigningKey(IConfiguration cfg)
    {
        var secret = cfg["Admin:JwtSecret"] ?? cfg["ADMIN_JWT_SECRET"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException(
                "Admin:JwtSecret (env ADMIN_JWT_SECRET) não configurada ou curta demais — mínimo de 32 caracteres.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public (string Token, DateTimeOffset ExpiraEm) CriarToken(AdminUser user)
    {
        var horas = _cfg.GetValue("Admin:TokenHoras", 8);
        var expiraEm = DateTimeOffset.UtcNow.AddHours(horas);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("role", "admin")
        };

        var creds = new SigningCredentials(ResolveSigningKey(_cfg), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiraEm.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiraEm);
    }
}
