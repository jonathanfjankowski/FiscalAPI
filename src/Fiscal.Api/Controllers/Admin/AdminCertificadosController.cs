using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

/// <summary>Gestão de certificados pelo painel admin (upload em nome de um tenant).</summary>
[ApiController]
[Route("v1/admin/tenants/{tenantId:guid}/certificados")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminCertificadosController : ControllerBase
{
    private readonly FiscalDbContext _db;
    private readonly ICertificadoStore _store;
    private readonly IRepositorioAuditoria _auditoria;

    public AdminCertificadosController(FiscalDbContext db, ICertificadoStore store, IRepositorioAuditoria auditoria)
    {
        _db = db;
        _store = store;
        _auditoria = auditoria;
    }

    public record AdminCertificadoResponse(Guid Id, string Thumbprint, DateOnly ValidoAte, bool Ativo, DateTimeOffset CriadoEm);

    [HttpGet]
    public async Task<IActionResult> Listar(Guid tenantId, CancellationToken ct)
    {
        if (!await _db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) return NotFound("Tenant não encontrado.");
        var certs = await _db.Certificados.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.ValidoAte) // vencendo primeiro; DateOnly ordena como TEXT
            .Select(c => new AdminCertificadoResponse(c.Id, c.Thumbprint, c.ValidoAte, c.Ativo, c.CriadoEm))
            .ToListAsync(ct);
        return Ok(certs);
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Upload(Guid tenantId, IFormFile pfx, [FromForm] string senha, CancellationToken ct)
    {
        if (!await _db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) return NotFound("Tenant não encontrado.");
        if (pfx is null || pfx.Length == 0)
            return Problem(statusCode: 400, title: "Arquivo .pfx é obrigatório.");
        if (string.IsNullOrEmpty(senha))
            return Problem(statusCode: 400, title: "Senha é obrigatória.");

        byte[] bytes;
        await using (var ms = new MemoryStream())
        {
            await pfx.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        CertificadoEnvelope envelope;
        try
        {
            envelope = await _store.CriarEnvelopeAsync(bytes, senha, ct);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or System.Security.Authentication.InvalidCredentialException)
        {
            return Problem(statusCode: 422, title: "Certificado inválido", detail: "Não foi possível abrir o .pfx com a senha informada.");
        }

        var cert = new Certificado
        {
            TenantId = tenantId,
            PfxCriptografado = envelope.PfxCifrado,
            SenhaCriptografada = envelope.SenhaCifrada,
            ChaveDekCriptografada = envelope.DekCifrada,
            Thumbprint = envelope.Thumbprint,
            ValidoAte = envelope.ValidoAte,
            Ativo = true,
            CriadoEm = DateTimeOffset.UtcNow
        };
        var anteriores = await _db.Certificados
            .Where(c => c.TenantId == tenantId && c.Ativo)
            .ToListAsync(ct);
        foreach (var anterior in anteriores)
            anterior.Ativo = false;
        await _db.Certificados.AddAsync(cert, ct);
        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenantId,
            Acao = "CERTIFICADO_ARMAZENADO",
            RecursoId = cert.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress,
            Detalhe = $"{{\"thumbprint\":\"{envelope.Thumbprint}\",\"validoAte\":\"{envelope.ValidoAte:O}\",\"via\":\"admin\"}}"
        }, ct);
        if (anteriores.Count > 0)
        {
            await _auditoria.RegistrarAsync(new Auditoria
            {
                TenantId = tenantId,
                Acao = "CERTIFICADO_SUBSTITUIDO",
                RecursoId = cert.Id,
                IpOrigem = HttpContext.Connection.RemoteIpAddress,
                Detalhe = $"{{\"thumbprintsAnteriores\":[{string.Join(",", anteriores.Select(a => $"\"{a.Thumbprint}\""))}],\"thumbprintNovo\":\"{envelope.Thumbprint}\",\"via\":\"admin\"}}"
            }, ct);
        }
        await _db.SaveChangesAsync(ct);

        return Created("", new AdminCertificadoResponse(cert.Id, cert.Thumbprint, cert.ValidoAte, cert.Ativo, cert.CriadoEm));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Desativar(Guid tenantId, Guid id, CancellationToken ct)
    {
        var cert = await _db.Certificados
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (cert is null) return NotFound("Certificado não encontrado.");

        var jaInativo = !cert.Ativo;
        cert.Ativo = false;
        if (!jaInativo)
        {
            await _auditoria.RegistrarAsync(new Auditoria
            {
                TenantId = tenantId,
                Acao = "CERTIFICADO_REVOGADO",
                RecursoId = cert.Id,
                IpOrigem = HttpContext.Connection.RemoteIpAddress,
                Detalhe = $"{{\"thumbprint\":\"{cert.Thumbprint}\",\"via\":\"admin\"}}"
            }, ct);
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/ativar")]
    public async Task<IActionResult> Ativar(Guid tenantId, Guid id, CancellationToken ct)
    {
        var cert = await _db.Certificados
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (cert is null) return NotFound("Certificado não encontrado.");

        if (!cert.Ativo)
        {
            var outros = await _db.Certificados
                .Where(c => c.TenantId == tenantId && c.Ativo)
                .ToListAsync(ct);
            foreach (var outro in outros)
                outro.Ativo = false;
            cert.Ativo = true;

            await _auditoria.RegistrarAsync(new Auditoria
            {
                TenantId = tenantId,
                Acao = "CERTIFICADO_ATIVADO",
                RecursoId = cert.Id,
                IpOrigem = HttpContext.Connection.RemoteIpAddress,
                Detalhe = $"{{\"thumbprint\":\"{cert.Thumbprint}\",\"via\":\"admin\"}}"
            }, ct);
            await _db.SaveChangesAsync(ct);
        }
        return Ok(new AdminCertificadoResponse(cert.Id, cert.Thumbprint, cert.ValidoAte, cert.Ativo, cert.CriadoEm));
    }
}
