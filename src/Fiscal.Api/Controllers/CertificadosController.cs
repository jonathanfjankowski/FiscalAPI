using Fiscal.Api.Authentication;
using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fiscal.Api.Controllers;

[ApiController]
[Route("v1/certificados")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.SchemeName)]
public class CertificadosController : ControllerBase
{
    private readonly ICertificadoStore _store;
    private readonly IRepositorioCertificado _repo;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly FiscalDbContext _db;

    public CertificadosController(
        ICertificadoStore store,
        IRepositorioCertificado repo,
        IRepositorioAuditoria auditoria,
        FiscalDbContext db)
    {
        _store = store;
        _repo = repo;
        _auditoria = auditoria;
        _db = db;
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Upload(
        IFormFile pfx,
        [FromForm] string senha,
        CancellationToken ct)
    {
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

        var tenantId = HttpContext.GetTenantId();
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
        await _repo.AdicionarAsync(cert, ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenantId,
            Acao = "CERTIFICADO_ARMAZENADO",
            RecursoId = cert.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress,
            Detalhe = $"{{\"thumbprint\":\"{envelope.Thumbprint}\",\"validoAte\":\"{envelope.ValidoAte:O}\"}}"
        }, ct);
        // BUG corrigido (pego pelos testes de segurança): sem SaveChanges o
        // certificado "criado" nunca chegava ao banco.
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Listar), new
        {
            id = cert.Id,
            thumbprint = cert.Thumbprint,
            validoAte = cert.ValidoAte,
            ativo = cert.Ativo
        });
    }

    public record CertificadoResponse(Guid Id, string Thumbprint, DateOnly ValidoAte, bool Ativo, DateTimeOffset CriadoEm);

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var certs = await _repo.ListarPorTenantAsync(tenantId, ct);
        var resp = certs.Select(c => new CertificadoResponse(c.Id, c.Thumbprint, c.ValidoAte, c.Ativo, c.CriadoEm));
        return Ok(resp);
    }
}
