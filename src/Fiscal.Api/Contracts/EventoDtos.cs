using System.ComponentModel.DataAnnotations;

namespace Fiscal.Api.Contracts;

public record CancelamentoRequest(
    [Required, MinLength(15), MaxLength(1000)] string Justificativa);

public record CartaCorrecaoRequest(
    [Required, MinLength(15), MaxLength(1000)] string Correcao);

public record InutilizacaoRequest(
    [Required] string Ambiente,
    short Modelo, // valida 55/65 no controller — Range(55,65) aceitaria 56..64
    [Range(1, 999)] short Serie,
    [Range(1, long.MaxValue)] long NumeroInicial,
    [Range(1, long.MaxValue)] long NumeroFinal,
    [Required, MinLength(15), MaxLength(1000)] string Justificativa);
