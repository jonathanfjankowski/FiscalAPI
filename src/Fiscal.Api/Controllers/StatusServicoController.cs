using Fiscal.Api.Authentication;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Fiscal.Api.Controllers;

[ApiController]
[Route("v1/status-servico")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.SchemeName)]
public class StatusServicoController : ControllerBase
{
    private readonly IConsultaStatusServico _consulta;
    private readonly IRepositorioCertificado _certRepo;
    private readonly ICertificadoStore _certStore;
    private readonly IRepositorioTenant _tenantRepo;
    private readonly IMemoryCache _cache;
    private readonly bool _sandbox;

    public StatusServicoController(
        IConsultaStatusServico consulta,
        IRepositorioCertificado certRepo,
        ICertificadoStore certStore,
        IRepositorioTenant tenantRepo,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _consulta = consulta;
        _certRepo = certRepo;
        _certStore = certStore;
        _tenantRepo = tenantRepo;
        _cache = cache;
        _sandbox = configuration.GetValue("Fiscal:ModoSandbox", true);
    }

    /// <summary>
    /// Status do serviço SEFAZ para a UF do tenant (cache de 60 s).
    /// Query: modelo=55|65 (default 55), ambiente=homologacao|producao.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Consultar(
        [FromQuery] short modelo = 55,
        [FromQuery] string? ambiente = null,
        CancellationToken ct = default)
    {
        if (modelo is not (55 or 65))
            return Problem(statusCode: 422, title: "Modelo deve ser 55 (NF-e) ou 65 (NFC-e).");

        var ambienteReq = ambiente switch
        {
            "producao" => Ambiente.Producao,
            "homologacao" => Ambiente.Homologacao,
            _ => (Ambiente?)null,
        };
        if (ambiente == "producao" || ambiente == "homologacao")
            if (ambienteReq != HttpContext.GetAmbiente())
                return Problem(statusCode: 403, title: "API Key não autorizada para o ambiente solicitado.");
        var ambienteEfetivo = ambienteReq ?? HttpContext.GetAmbiente();

        var tenantId = HttpContext.GetTenantId();
        var cacheKey = $"status-servico:{tenantId}:{modelo}:{ambienteEfetivo}";
        if (_cache.TryGetValue(cacheKey, out StatusServico? cached) && cached is not null)
            return Ok(ParaResponse(cached, true));

        var tenant = await _tenantRepo.ObterPorIdAsync(tenantId, ct);
        if (tenant is null) return NotFound();

        var cert = await _certRepo.ObterAtivoPorTenantAsync(tenantId, ct);
        if (cert is null && !_sandbox)
            return Problem(statusCode: 409, title: "Nenhum certificado ativo para o tenant.");

        using var x509 = cert is null ? null : await _certStore.CarregarAsync(cert, ct);

        try
        {
            var status = await _consulta.ConsultarAsync(tenant, modelo, x509, ambienteEfetivo, ct);
            _cache.Set(cacheKey, status, TimeSpan.FromSeconds(60));
            return Ok(ParaResponse(status, false));
        }
        catch (Exception ex)
        {
            return Problem(statusCode: 503, title: "Consulta de status indisponível",
                detail: $"{ex.GetType().Name}: {ex.Message}");
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
