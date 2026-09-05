using System.Text.Json;
using Fiscal.Api.Authentication;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

[ApiController]
[Route("v1/admin/auth")]
public class AdminAuthController : ControllerBase
{
    // Hash dummy para equalizar o tempo de resposta quando o e-mail não existe
    // (PBKDF2 custa ~100ms; sem isso, o erro 401 vaza existência de conta).
    private static readonly string DummyHash =
        ApiKeyAuthenticationHandler.HashKey("dummy-para-timing-equalizado");

    private readonly FiscalDbContext _db;
    private readonly IAdminTokenService _tokens;
    private readonly IRepositorioAuditoria _auditoria;

    public AdminAuthController(FiscalDbContext db, IAdminTokenService tokens, IRepositorioAuditoria auditoria)
    {
        _db = db;
        _tokens = tokens;
        _auditoria = auditoria;
    }

    public record LoginRequest(string? Email, string? Senha);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Senha))
            return Problem(statusCode: 400, title: "E-mail e senha são obrigatórios.");

        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
        var senhaOk = ApiKeyAuthenticationHandler.VerifyKey(req.Senha, user?.SenhaHash ?? DummyHash);

        if (user is null || !user.Ativo || !senhaOk)
        {
            await _auditoria.RegistrarAsync(new Core.Entities.Auditoria
            {
                Acao = "ADMIN_LOGIN_FALHOU",
                IpOrigem = HttpContext.Connection.RemoteIpAddress,
                Detalhe = JsonSerializer.Serialize(new { email })
            }, ct);
            await _db.SaveChangesAsync(ct);
            return Problem(statusCode: 401, title: "Credenciais inválidas.");
        }

        var (token, expiraEm) = _tokens.CriarToken(user);

        await _auditoria.RegistrarAsync(new Core.Entities.Auditoria
        {
            Acao = "ADMIN_LOGIN_OK",
            RecursoId = user.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new { token, expiraEm, email = user.Email });
    }
}
