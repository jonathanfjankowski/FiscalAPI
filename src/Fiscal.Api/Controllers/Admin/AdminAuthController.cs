using System.Text.Json;
using Fiscal.Api.Authentication;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Fiscal.Api.Controllers;

[ApiController]
[Route("v1/admin/auth")]
public class AdminAuthController : ControllerBase
{
    // Hash dummy para equalizar o tempo de resposta quando o e-mail não existe
    // (PBKDF2 custa ~100ms; sem isso, o erro 401 vaza existência de conta).
    private static readonly string DummyHash =
        ApiKeyAuthenticationHandler.HashKey("dummy-para-timing-equalizado");

    private const int MaxFalhas = 5;
    private static readonly TimeSpan JanelaLockout = TimeSpan.FromMinutes(15);

    private readonly FiscalDbContext _db;
    private readonly IAdminTokenService _tokens;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IDistributedCache _cache;

    public AdminAuthController(
        FiscalDbContext db, IAdminTokenService tokens, IRepositorioAuditoria auditoria, IDistributedCache cache)
    {
        _db = db;
        _tokens = tokens;
        _auditoria = auditoria;
        _cache = cache;
    }

    public record LoginRequest(string? Email, string? Senha);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Senha))
            return Problem(statusCode: 400, title: "E-mail e senha são obrigatórios.");

        var email = req.Email.Trim().ToLowerInvariant();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
        var lockoutKey = $"admin-lockout:{ip}:{email}";

        // Lockout progressivo: 5 falhas por (IP, e-mail) em 15 min bloqueiam novas
        // tentativas — bruta força não cabe nem dentro do rate limit global.
        var falhasRaw = await _cache.GetStringAsync(lockoutKey, ct);
        if (falhasRaw is not null && int.TryParse(falhasRaw, out var falhas) && falhas >= MaxFalhas)
            return Problem(statusCode: 429,
                title: "Muitas tentativas de login.",
                detail: "Aguarde alguns minutos antes de tentar novamente.");

        var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
        var senhaOk = ApiKeyAuthenticationHandler.VerifyKey(req.Senha, user?.SenhaHash ?? DummyHash);

        if (user is null || !user.Ativo || !senhaOk)
        {
            await RegistrarFalhaAsync(lockoutKey, ct);
            await _auditoria.RegistrarAsync(new Core.Entities.Auditoria
            {
                Acao = "ADMIN_LOGIN_FALHOU",
                IpOrigem = HttpContext.Connection.RemoteIpAddress,
                Detalhe = JsonSerializer.Serialize(new { email })
            }, ct);
            await _db.SaveChangesAsync(ct);
            return Problem(statusCode: 401, title: "Credenciais inválidas.");
        }

        await _cache.RemoveAsync(lockoutKey, ct);
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

    private async Task RegistrarFalhaAsync(string lockoutKey, CancellationToken ct)
    {
        var atual = await _cache.GetStringAsync(lockoutKey, ct);
        var falhas = int.TryParse(atual, out var n) ? n + 1 : 1;
        await _cache.SetStringAsync(lockoutKey, falhas.ToString(), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = JanelaLockout
        }, ct);
    }
}
