using Fiscal.Api.Authentication;
using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

public record PerfilTenantRequest(
    string? InscricaoEstadual,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cep,
    string? NomeMunicipio,
    string? CscId,
    string? Csc);

[ApiController]
[Route("v1/tenants")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.SchemeName)]
public class TenantsController : ControllerBase
{
    private readonly FiscalDbContext _db;
    private readonly ICertificadoStore _certStore;
    private readonly IRepositorioAuditoria _auditoria;

    public TenantsController(FiscalDbContext db, ICertificadoStore certStore, IRepositorioAuditoria auditoria)
    {
        _db = db;
        _certStore = certStore;
        _auditoria = auditoria;
    }

    /// <summary>Perfil fiscal do tenant (dados do emitente usados na NFe/NFC-e).</summary>
    [HttpGet("perfil")]
    public async Task<IActionResult> ObterPerfil(CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        return Ok(new
        {
            tenant.Cnpj,
            tenant.RazaoSocial,
            tenant.Uf,
            tenant.CodigoMunicipioIbge,
            tenant.RegimeTributario,
            tenant.InscricaoEstadual,
            tenant.Logradouro,
            tenant.Numero,
            tenant.Complemento,
            tenant.Bairro,
            tenant.Cep,
            tenant.NomeMunicipio,
            tenant.CscId,
            cscCadastrado = tenant.CscCriptografado is not null,
        });
    }

    /// <summary>
    /// Atualiza o perfil fiscal do emitente (exigido para emissão real) e,
    /// opcionalmente, o CSC/IdCSC da NFC-e (o CSC é armazenado cifrado com a KEK).
    /// Campos nulos não são alterados.
    /// </summary>
    [HttpPut("perfil")]
    public async Task<IActionResult> AtualizarPerfil([FromBody] PerfilTenantRequest req, CancellationToken ct)
    {
        var tenantId = HttpContext.GetTenantId();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        tenant.InscricaoEstadual = req.InscricaoEstadual ?? tenant.InscricaoEstadual;
        tenant.Logradouro = req.Logradouro ?? tenant.Logradouro;
        tenant.Numero = req.Numero ?? tenant.Numero;
        tenant.Complemento = req.Complemento ?? tenant.Complemento;
        tenant.Bairro = req.Bairro ?? tenant.Bairro;
        tenant.Cep = req.Cep ?? tenant.Cep;
        tenant.NomeMunicipio = req.NomeMunicipio ?? tenant.NomeMunicipio;

        if (req.Csc is not null)
        {
            if (string.IsNullOrWhiteSpace(req.CscId))
                return Problem(statusCode: 422, title: "Informe cscId junto com o csc.");

            tenant.CscCriptografado = await _certStore.CifrarTextoAsync(req.Csc, ct);
            tenant.CscId = req.CscId;
        }
        else if (req.CscId is not null)
        {
            return Problem(statusCode: 422, title: "Informe o csc junto com o cscId.");
        }

        await _db.SaveChangesAsync(ct);
        await _auditoria.RegistrarAsync(new Auditoria
        {
            TenantId = tenantId,
            Acao = "PERFIL_TENANT_ATUALIZADO",
            RecursoId = tenantId,
            Detalhe = $"{{\"cscAtualizado\":{(req.Csc is not null ? "true" : "false").ToLowerInvariant()}}}"
        }, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new { atualizado = true });
    }
}
