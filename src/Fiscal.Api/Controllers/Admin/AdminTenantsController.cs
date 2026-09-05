using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Api.Controllers;

public record AdminTenantRequest(
    string? Cnpj,
    string? RazaoSocial,
    string? Uf,
    string? CodigoMunicipioIbge,
    short? RegimeTributario,
    string? AmbientePadrao,
    string? InscricaoEstadual,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cep,
    string? NomeMunicipio,
    string? WebhookUrl,
    string? WebhookSecret,
    bool? Ativo);

public record AdminTenantResponse(
    Guid Id,
    string Cnpj,
    string RazaoSocial,
    string Uf,
    string? CodigoMunicipioIbge,
    short RegimeTributario,
    string AmbientePadrao,
    string? InscricaoEstadual,
    string? Logradouro,
    string? Numero,
    string? Complemento,
    string? Bairro,
    string? Cep,
    string? NomeMunicipio,
    string? WebhookUrl,
    bool Ativo,
    DateTimeOffset CriadoEm,
    int ApiKeysAtivas,
    int CertificadosAtivos);

[ApiController]
[Route("v1/admin/tenants")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminTenantsController : ControllerBase
{
    private readonly FiscalDbContext _db;
    private readonly IRepositorioAuditoria _auditoria;

    public AdminTenantsController(FiscalDbContext db, IRepositorioAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .OrderBy(t => t.RazaoSocial)
            .Select(t => new AdminTenantResponse(
                t.Id, t.Cnpj, t.RazaoSocial, t.Uf, t.CodigoMunicipioIbge,
                t.RegimeTributario,
                t.AmbientePadrao == (short)Ambiente.Producao ? "producao" : "homologacao",
                t.InscricaoEstadual, t.Logradouro, t.Numero, t.Complemento, t.Bairro,
                t.Cep, t.NomeMunicipio, t.WebhookUrl, t.Ativo, t.CriadoEm,
                t.ApiKeys.Count(k => k.Ativa && k.RevogadoEm == null),
                t.Certificados.Count(c => c.Ativo)))
            .ToListAsync(ct);
        return Ok(tenants);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new AdminTenantResponse(
                t.Id, t.Cnpj, t.RazaoSocial, t.Uf, t.CodigoMunicipioIbge,
                t.RegimeTributario,
                t.AmbientePadrao == (short)Ambiente.Producao ? "producao" : "homologacao",
                t.InscricaoEstadual, t.Logradouro, t.Numero, t.Complemento, t.Bairro,
                t.Cep, t.NomeMunicipio, t.WebhookUrl, t.Ativo, t.CriadoEm,
                t.ApiKeys.Count(k => k.Ativa && k.RevogadoEm == null),
                t.Certificados.Count(c => c.Ativo)))
            .FirstOrDefaultAsync(ct);
        if (tenant is null) return NotFound();
        return Ok(tenant);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] AdminTenantRequest req, CancellationToken ct)
    {
        if (req is null) return Problem(statusCode: 400, title: "Corpo da requisição é obrigatório.");

        var cnpj = SomenteDigitos(req.Cnpj);
        if (string.IsNullOrWhiteSpace(req.RazaoSocial))
            return Problem(statusCode: 422, title: "RazaoSocial é obrigatória.");
        if (cnpj.Length != 14)
            return Problem(statusCode: 422, title: "CNPJ inválido", detail: "Informe os 14 dígitos do CNPJ.");
        if (string.IsNullOrWhiteSpace(req.Uf) || req.Uf!.Trim().Length != 2)
            return Problem(statusCode: 422, title: "UF inválida", detail: "Informe a UF com 2 letras (ex.: PR).");
        if (req.AmbientePadrao is not null && req.AmbientePadrao != "producao" && req.AmbientePadrao != "homologacao")
            return Problem(statusCode: 422, title: "AmbientePadrao inválido", detail: "Use 'producao' ou 'homologacao'.");
        if (req.RegimeTributario is < 1 or > 3)
            return Problem(statusCode: 422, title: "RegimeTributario inválido", detail: "Use 1 (Simples), 2 (Simples exceto sublimite) ou 3 (Regime Normal).");

        if (await _db.Tenants.AnyAsync(t => t.Cnpj == cnpj, ct))
            return Problem(statusCode: 409, title: "Já existe um tenant com este CNPJ.");

        var tenant = new Tenant
        {
            Cnpj = cnpj,
            RazaoSocial = req.RazaoSocial!.Trim(),
            Uf = req.Uf.Trim().ToUpperInvariant(),
            CodigoMunicipioIbge = req.CodigoMunicipioIbge,
            RegimeTributario = req.RegimeTributario ?? 3,
            AmbientePadrao = req.AmbientePadrao == "producao" ? (short)Ambiente.Producao : (short)Ambiente.Homologacao,
            InscricaoEstadual = req.InscricaoEstadual,
            Logradouro = req.Logradouro,
            Numero = req.Numero,
            Complemento = req.Complemento,
            Bairro = req.Bairro,
            Cep = SomenteDigitos(req.Cep) is { Length: 8 } cep ? cep : req.Cep,
            NomeMunicipio = req.NomeMunicipio,
            WebhookUrl = req.WebhookUrl,
            WebhookSecret = req.WebhookSecret,
            Ativo = req.Ativo ?? true,
            CriadoEm = DateTimeOffset.UtcNow
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            Acao = "TENANT_CRIADO",
            TenantId = tenant.Id,
            RecursoId = tenant.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress,
            Detalhe = $"{{\"cnpj\":\"{tenant.Cnpj}\"}}"
        }, ct);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Obter), new { id = tenant.Id }, tenant.Id);
    }

    /// <summary>Atualização parcial: campos nulos não são alterados.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AdminTenantRequest req, CancellationToken ct)
    {
        if (req is null) return Problem(statusCode: 400, title: "Corpo da requisição é obrigatório.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        if (req.Cnpj is not null)
        {
            var cnpj = SomenteDigitos(req.Cnpj);
            if (cnpj.Length != 14)
                return Problem(statusCode: 422, title: "CNPJ inválido", detail: "Informe os 14 dígitos do CNPJ.");
            if (cnpj != tenant.Cnpj && await _db.Tenants.AnyAsync(t => t.Cnpj == cnpj, ct))
                return Problem(statusCode: 409, title: "Já existe um tenant com este CNPJ.");
            tenant.Cnpj = cnpj;
        }
        if (req.RazaoSocial is not null) tenant.RazaoSocial = req.RazaoSocial.Trim();
        if (req.Uf is not null)
        {
            if (req.Uf.Trim().Length != 2)
                return Problem(statusCode: 422, title: "UF inválida", detail: "Informe a UF com 2 letras (ex.: PR).");
            tenant.Uf = req.Uf.Trim().ToUpperInvariant();
        }
        if (req.CodigoMunicipioIbge is not null) tenant.CodigoMunicipioIbge = req.CodigoMunicipioIbge;
        if (req.RegimeTributario is not null)
        {
            if (req.RegimeTributario is < 1 or > 3)
                return Problem(statusCode: 422, title: "RegimeTributario inválido", detail: "Use 1, 2 ou 3.");
            tenant.RegimeTributario = req.RegimeTributario.Value;
        }
        if (req.AmbientePadrao is not null)
        {
            if (req.AmbientePadrao != "producao" && req.AmbientePadrao != "homologacao")
                return Problem(statusCode: 422, title: "AmbientePadrao inválido", detail: "Use 'producao' ou 'homologacao'.");
            tenant.AmbientePadrao = req.AmbientePadrao == "producao" ? (short)Ambiente.Producao : (short)Ambiente.Homologacao;
        }
        if (req.InscricaoEstadual is not null) tenant.InscricaoEstadual = req.InscricaoEstadual;
        if (req.Logradouro is not null) tenant.Logradouro = req.Logradouro;
        if (req.Numero is not null) tenant.Numero = req.Numero;
        if (req.Complemento is not null) tenant.Complemento = req.Complemento;
        if (req.Bairro is not null) tenant.Bairro = req.Bairro;
        if (req.Cep is not null) tenant.Cep = SomenteDigitos(req.Cep) is { Length: 8 } cep ? cep : req.Cep;
        if (req.NomeMunicipio is not null) tenant.NomeMunicipio = req.NomeMunicipio;
        if (req.WebhookUrl is not null) tenant.WebhookUrl = req.WebhookUrl;
        if (req.WebhookSecret is not null) tenant.WebhookSecret = req.WebhookSecret;
        if (req.Ativo is not null) tenant.Ativo = req.Ativo.Value;

        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            Acao = "TENANT_ATUALIZADO",
            TenantId = tenant.Id,
            RecursoId = tenant.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Desativação soft: Ativo=false. Não apaga dados nem revoga keys.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        tenant.Ativo = false;
        await _db.SaveChangesAsync(ct);

        await _auditoria.RegistrarAsync(new Auditoria
        {
            Acao = "TENANT_DESATIVADO",
            TenantId = tenant.Id,
            RecursoId = tenant.Id,
            IpOrigem = HttpContext.Connection.RemoteIpAddress
        }, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static string SomenteDigitos(string? valor) =>
        new string((valor ?? "").Where(char.IsDigit).ToArray());
}
