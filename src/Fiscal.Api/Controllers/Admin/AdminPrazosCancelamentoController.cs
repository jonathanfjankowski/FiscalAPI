using Fiscal.Api.Services;
using Fiscal.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fiscal.Api.Controllers;

public record AtualizarPrazoCancelamentoRequest(int PrazoMinutos, string? Observacao);

/// <summary>
/// Gestão dos prazos de cancelamento (NF-e 55 e NFC-e 65) por UF no painel
/// admin. Permite consultar a tabela base SEFAZ das 27 UFs, sobrescrever
/// qualquer UF/modelo quando a SEFAZ alterar o prazo e restaurar o padrão.
/// </summary>
[ApiController]
[Route("v1/admin/prazos-cancelamento")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "Admin")]
public class AdminPrazosCancelamentoController : ControllerBase
{
    private readonly PrazoCancelamentoService _service;

    public AdminPrazosCancelamentoController(PrazoCancelamentoService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var itens = await _service.ListarTodosAsync(ct);
        return Ok(new { itens });
    }

    [HttpPut("{uf}/{modelo:int}")]
    public async Task<IActionResult> Atualizar(
        string uf,
        int modelo,
        [FromBody] AtualizarPrazoCancelamentoRequest req,
        CancellationToken ct)
    {
        if (!TabelaPrazosCancelamentoSefaz.UfValida(uf))
            return Problem(statusCode: 422, title: "UF inválida.");

        if (modelo is not (55 or 65))
            return Problem(statusCode: 422, title: "Modelo deve ser 55 (NF-e) ou 65 (NFC-e).");

        if (req is null || req.PrazoMinutos < PrazoCancelamentoService.MinutosMinimo || req.PrazoMinutos > PrazoCancelamentoService.MinutosMaximo)
        {
            return Problem(
                statusCode: 422,
                title: "Prazo inválido.",
                detail: $"Informe um prazo entre {PrazoCancelamentoService.MinutosMinimo} minuto e {PrazoCancelamentoService.MinutosMaximo} minutos (30 dias).");
        }

        if (req.Observacao is not null && req.Observacao.Length > 300)
            return Problem(statusCode: 422, title: "Observação deve ter no máximo 300 caracteres.");

        var atualizado = await _service.AtualizarAsync(uf, (short)modelo, req.PrazoMinutos, req.Observacao, ct);
        return Ok(atualizado);
    }

    [HttpDelete("{uf}/{modelo:int}")]
    public async Task<IActionResult> RestaurarPadrao(string uf, int modelo, CancellationToken ct)
    {
        if (!TabelaPrazosCancelamentoSefaz.UfValida(uf))
            return Problem(statusCode: 422, title: "UF inválida.");

        if (modelo is not (55 or 65))
            return Problem(statusCode: 422, title: "Modelo deve ser 55 (NF-e) ou 65 (NFC-e).");

        var restaurado = await _service.RestaurarPadraoAsync(uf, (short)modelo, ct);
        return Ok(restaurado);
    }
}
