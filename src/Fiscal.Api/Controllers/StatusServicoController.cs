using System.Text.Json;
using Fiscal.Api.Authentication;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Fiscal.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

namespace Fiscal.Api.Controllers;

[ApiController]
[Route("v1/status-servico")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.SchemeName)]
public class StatusServicoController : ControllerBase
{
    private readonly IEnumerable<IConsultaStatusServico> _consultas;
    private readonly IRepositorioCertificado _certRepo;
    private readonly ICertificadoStore _certStore;
    private readonly IRepositorioTenant _tenantRepo;
    private readonly IDistributedCache _cache;
    private readonly ILogger<StatusServicoController> _logger;

    public StatusServicoController(
        IEnumerable<IConsultaStatusServico> consultas,
        IRepositorioCertificado certRepo,
        ICertificadoStore certStore,
        IRepositorioTenant tenantRepo,
        IDistributedCache cache,
        ILogger<StatusServicoController> logger)
    {
        _consultas = consultas;
        _certRepo = certRepo;
        _certStore = certStore;
        _tenantRepo = tenantRepo;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Status do serviço SEFAZ para a UF do tenant (cache de 60 s, compartilhado
    /// via Redis quando Fiscal:Redis:ConnectionString está configurado).
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
        var cachedJson = await _cache.GetStringAsync(cacheKey, ct);
        if (cachedJson is not null)
            return Ok(ParaResponse(JsonSerializer.Deserialize<StatusServico>(cachedJson)!, true));

        var tenant = await _tenantRepo.ObterPorIdAsync(tenantId, ct);
        if (tenant is null) return NotFound();

        // Sandbox por tenant: mock consulta sem certificado e sem SEFAZ.
        var consulta = tenant.Sandbox
            ? _consultas.FirstOrDefault(c => c is Fiscal.Adapters.Unimake.ConsultaStatusServicoMock)
                ?? _consultas.First()
            : _consultas.FirstOrDefault(c => c is not Fiscal.Adapters.Unimake.ConsultaStatusServicoMock)
                ?? _consultas.First();

        var cert = await _certRepo.ObterAtivoPorTenantAsync(tenantId, ct);
        if (cert is null && !tenant.Sandbox)
            return Problem(statusCode: 409, title: "Nenhum certificado ativo para o tenant.");

        using var x509 = cert is null ? null : await _certStore.CarregarAsync(cert, ct);

        try
        {
            var status = await consulta.ConsultarAsync(tenant, modelo, x509, ambienteEfetivo, ct);
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(status),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
                }, ct);
            return Ok(ParaResponse(status, false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Detalhe interno (ex.Message pode carregar endpoint/stack) fica no
            // log — o cliente recebe só o 503 genérico em RFC 7807.
            _logger.LogError(ex, "Consulta de status-servico falhou para tenant {TenantId} (modelo {Modelo}).",
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
