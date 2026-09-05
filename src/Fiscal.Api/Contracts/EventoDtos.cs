using System.ComponentModel.DataAnnotations;

namespace Fiscal.Api.Contracts;

public record CancelamentoRequest(
    [Required, MinLength(15), MaxLength(1000)] string Justificativa);

public record CartaCorrecaoRequest(
    [Required, MinLength(15), MaxLength(1000)] string Correcao);

public record InutilizacaoRequest(
    [Required] string Ambiente,
    [Range(55, 65)] short Modelo,
    [Range(1, 999)] short Serie,
    [Range(1, long.MaxValue)] long NumeroInicial,
    [Range(1, long.MaxValue)] long NumeroFinal,
    [Required, MinLength(15), MaxLength(1000)] string Justificativa);
