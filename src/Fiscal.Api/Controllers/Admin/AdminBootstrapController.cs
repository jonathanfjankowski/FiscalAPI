using System.Security.Cryptography;
using Fiscal.Api.Authentication;
using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers.Admin;

public record BootstrapKeyInfo(string Prefixo, DateTimeOffset CriadoEm);

public record BootstrapKeyRotacionadaResponse(
    Guid Id,
    string Chave,
    string Prefixo,
    string Aviso);

/// <summary>
/// Chave "master" (X-Bootstrap-Key) que o ERP usa em POST /v1/empresas para
/// provisionar tenants. Fica no banco (hash PBKDF2 + prefixo) e é rotacionada
/// aqui — a chave completa aparece UMA única vez, na resposta da rotação.
/// </summary>
[ApiController]
[Route("v1/admin/bootstrap-key")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminBootstrapController : ControllerBase
{
    private readonly FiscalDbContext _db;
    private readonly IRepositorioAuditoria _auditoria;

    public AdminBootstrapController(FiscalDbContext db, IRepositorioAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    /// <summary>Metadados da chave ativa (nunca a chave em claro).</summary>
    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken ct)
    {
        var chave = await _db.BootstrapKeys.AsNoTracking()
            .OrderBy(k => k.CriadoEm)
            .FirstOrDefaultAsync(ct);
        if (chave is null)
            return Ok(new { configurada = false });
        return Ok(new BootstrapKeyInfo(chave.Prefixo, chave.CriadoEm));
    }

    /// <summary>
    /// Gera uma chave nova (invalidando a anterior). A chave completa só existe
    /// no corpo desta resposta — o banco guarda apenas o hash PBKDF2.
    /// </summary>
    [HttpPost("rotacionar")]
    public async Task<IActionResult> Rotacionar(CancellationToken ct)
    {
        var chaveEmClaro = GerarChave();
        var nova = new BootstrapKey
        {
            Prefixo = ApiKeyAuthenticationHandler.PrefixoDa(chaveEmClaro),
            KeyHash = ApiKeyAuthenticationHandler.HashKey(chaveEmClaro),
            CriadoEm = DateTimeOffset.UtcNow
        };

        // Chave única: qualquer registro anterior é descartado.
        var antigas = await _db.BootstrapKeys.ToListAsync(ct);
        _db.BootstrapKeys.RemoveRange(antigas);
        _db.BootstrapKeys.Add(nova);
        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            Acao = "BOOTSTRAP_KEY_ROTACIONADA",
            RecursoId = nova.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new BootstrapKeyRotacionadaResponse(
            nova.Id, chaveEmClaro, nova.Prefixo,
            "Esta é a única vez que a chave completa é exibida. Atualize o ERP com ela — a anterior deixou de valer."));
    }

    /// <summary>fk_boot_ + 32 bytes base64 (mesma entropia das API keys).</summary>
    public static string GerarChave() =>
        "fk_boot_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
}
