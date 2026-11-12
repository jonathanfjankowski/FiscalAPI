using System.Text.Json;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Fiscal.Api.Controllers;

/// <summary>
/// Status do serviço SEFAZ para a UF de uma empresa, pelo painel.
///
/// O endpoint do integrador deriva o tenant da API key; o operador não tem
/// key, então o tenant vem por query e o backend resolve a UF dele. Em
/// sandbox a consulta usa o mock (sem certificado nem SEFAZ).
/// </summary>
[ApiController]
[Route("v1/admin/status-servico")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminStatusServicoController : ControllerBase
{
    private readonly FiscalDbContext _db;
    private readonly IEnumerable<IConsultaStatusServico> _consultas;
    private readonly IRepositorioCertificado _certRepo;
    private readonly ICertificadoStore _certStore;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AdminStatusServicoController> _logger;

    public AdminStatusServicoController(
        FiscalDbContext db,
        IEnumerable<IConsultaStatusServico> consultas,
        IRepositorioCertificado certRepo,
        ICertificadoStore certStore,
        IDistributedCache cache,
        ILogger<AdminStatusServicoController> logger)
    {
        _db = db;
        _consultas = consultas;
        _certRepo = certRepo;
        _certStore = certStore;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Consultar(
        [FromQuery] Guid tenantId,
        [FromQuery] short modelo = 55,
        [FromQuery] string? ambiente = null,
        CancellationToken ct = default)
    {
        if (modelo is not (55 or 65))
            return Problem(statusCode: 422, title: "Modelo deve ser 55 (NF-e) ou 65 (NFC-e).");

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var ambienteEfetivo = ambiente switch
        {
            "producao" => Ambiente.Producao,
            "homologacao" => Ambiente.Homologacao,
            _ => (Ambiente)tenant.AmbientePadrao,
        };

        // Cache de 60s, mesma chave e mesmo TTL do endpoint de tenant: o
        // topbar consulta a cada seleção de empresa e, sem cache, cada
        // operador martelaria a SEFAZ com SOAP (risco de throttling).
        var cacheKey = $"status-servico:{tenant.Id}:{modelo}:{ambienteEfetivo}";
        var emCache = await _cache.GetStringAsync(cacheKey, ct);
        if (emCache is not null)
        {
            var cached = JsonSerializer.Deserialize<StatusServico>(emCache)!;
            return Ok(ParaResponse(cached, doCache: true));
        }

        // Sandbox por tenant: o mock responde sem certificado e sem SEFAZ.
        var consulta = tenant.Sandbox
            ? _consultas.FirstOrDefault(c => c is Fiscal.Adapters.Unimake.ConsultaStatusServicoMock)
                ?? _consultas.First()
            : _consultas.FirstOrDefault(c => c is not Fiscal.Adapters.Unimake.ConsultaStatusServicoMock)
                ?? _consultas.First();

        var cert = await _certRepo.ObterAtivoPorTenantAsync(tenant.Id, ct);
        if (cert is null && !tenant.Sandbox)
            return Problem(statusCode: 409, title: "Nenhum certificado ativo para a empresa.");

        using var x509 = cert is null ? null : await _certStore.CarregarAsync(cert, ct);

        try
        {
            var status = await consulta.ConsultarAsync(tenant, modelo, x509, ambienteEfetivo, ct);
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(status),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
                }, ct);
            return Ok(ParaResponse(status, doCache: false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Detalhe interno fica no log; o painel recebe um 503 em RFC 7807.
            _logger.LogError(ex,
                "Consulta de status-servico (admin) falhou para tenant {TenantId} (modelo {Modelo}).",
                tenantId, modelo);
            return Problem(statusCode: 503, title: "Consulta de status indisponível",
                detail: "A SEFAZ não respondeu à consulta de status. Tente novamente em instantes.");
        }
    }

    private static object ParaResponse(StatusServico s, bool doCache) => new
    {
        cStat = s.CStat,
        xMotivo = s.XMotivo,
        tMed = s.TMed,
        consultadoEm = s.ConsultadoEm,
        doCache,
    };
}