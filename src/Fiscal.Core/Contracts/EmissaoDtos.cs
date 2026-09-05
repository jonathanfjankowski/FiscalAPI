using System.ComponentModel.DataAnnotations;

namespace Fiscal.Core.Contracts;

// Contrato REST de emissão. Mora no Fiscal.Core (e não no Fiscal.Api) porque o
// adapter Unimake precisa deserializar doc.PayloadEntrada (JSON do EmissaoRequest)
// para montar o EnviNFe — e Fiscal.Core não pode referenciar Fiscal.Api.

public record DestinatarioDto(
    [Required, MaxLength(14)] string CnpjCpf,
    [Required, MaxLength(200)] string Nome,
    string? InscricaoEstadual,
    EnderecoDto? Endereco);

public record EnderecoDto(
    [MaxLength(8)] string? Cep,
    [MaxLength(100)] string? Logradouro,
    [MaxLength(10)] string? Numero,
    [MaxLength(100)] string? Complemento,
    [MaxLength(100)] string? Bairro,
    [MaxLength(7)] string? CodigoMunicipioIbge,
    [MaxLength(2)] string? Uf,
    string? NomeMunicipio = null);

public record ImpostoDto(
    [Required, MaxLength(3)] string Cst,
    decimal? BaseCalculo,
    decimal? Aliquota,
    decimal? Valor);

public record ItemDto(
    [Required, MaxLength(60)] string Codigo,
    [Required, MaxLength(200)] string Descricao,
    [MaxLength(8)] string? Ncm,
    [MaxLength(4)] string? Cfop,
    [Range(0.0001, double.MaxValue)] decimal Quantidade,
    [Range(0, double.MaxValue)] decimal ValorUnitario,
    [Range(0, double.MaxValue)] decimal ValorTotal,
    List<ImpostoDto>? Impostos);

public record TotaisDto(
    [Range(0, double.MaxValue)] decimal ValorProdutos,
    [Range(0, double.MaxValue)] decimal ValorNota);

public record PagamentoDto(
    [Required, MaxLength(2)] string Forma,
    [Range(0, double.MaxValue)] decimal Valor);

public record EmissaoRequest(
    [Required] string Ambiente,
    [Range(1, 999)] short Serie,
    DestinatarioDto? Destinatario,
    [Required, MinLength(1)] List<ItemDto> Itens,
    [Required] TotaisDto Totais,
    List<PagamentoDto>? Pagamento,
    string? NaturezaOperacao = null);

public record EmissaoResponse(
    Guid Id,
    string Tipo,
    string Status,
    string Ambiente,
    short? Serie,
    long? Numero,
    string? ChaveAcesso,
    string? ProtocoloAutorizacao,
    string? XmlAssinado,
    string? XmlRetornoSefaz,
    string? MotivoStatus,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm);
