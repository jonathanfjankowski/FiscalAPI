using System.ComponentModel.DataAnnotations;

namespace Fiscal.Core.Contracts;

// Contrato REST da NFS-e Nacional em transmissão DPS real (padrão Nacional —
// IN 222/2024, layout 1.01). Morre no Fiscal.Core pelo mesmo motivo de
// EmissaoDtos: o adapter Unimake deserializa doc.PayloadEntrada.
//
// A API não calcula tributos — o integrador envia os valores prontos; a
// validação é declarativa (presença/formato), a crítica fiscal fina é da
// SEFAZ Nacional. Referência: docs/roadmap.md (item 1) e
// docs/cobertura-operacoes-fiscais.md (substituição).

public record NfseDpsRequest(
    [Required] string Ambiente,
    [Range(1, 99999)] short Serie,
    string? DataCompetencia,              // yyyy-MM-dd — default: data de hoje (UTC)
    int? TipoEmissor,                     // tpEmit: 1 prestador (default), 2 tomador, 3 intermediário
    int? CodigoMunicipioEmissor,          // cLocEmi — default: município do tenant
    NfseTomaDto? Tomador,
    [Required] NfseServicoDto Servico,
    [Required] NfseValoresDto Valores,
    NfseIbsCbsDto? IbsCbs,                // bloco RTC (reforma) — layout 1.01
    string? InformacoesComplementares);

public record NfseTomaDto(
    [Required, MaxLength(14)] string CnpjCpf,
    [MaxLength(200)] string? Nome,
    [MaxLength(20)] string? InscricaoMunicipal,
    [MaxLength(20)] string? Telefone,
    [MaxLength(80)] string? Email,
    NfseEnderecoDto? Endereco);

public record NfseEnderecoDto(
    [Required, MaxLength(7)] string CodigoMunicipioIbge,
    [MaxLength(8)] string? Cep,
    [Required, MaxLength(255)] string Logradouro,
    [Required, MaxLength(10)] string Numero,
    [MaxLength(255)] string? Complemento,
    [Required, MaxLength(255)] string Bairro);

public record NfseServicoDto(
    int? CodigoMunicipioPrestacao,        // cLocPrestacao — default: município do tenant
    [Required, MaxLength(6)] string CodigoTributarioNacional,   // cTribNac
    [MaxLength(20)] string? CodigoTributarioMunicipal,          // cTribMun (LC 116)
    [Required, MaxLength(2000)] string DescricaoServico,        // xDescServ
    [MaxLength(9)] string? CodigoNbs);                          // cNBS (9 dígitos)

public record NfseValoresDto(
    [Range(0, double.MaxValue)] decimal ValorServicos,          // vServ
    [Range(0, double.MaxValue)] decimal? ValorRecebido,         // vReceb
    [Range(0, double.MaxValue)] decimal? DescontoIncondicionado,
    [Range(1, 4)] int TributacaoIssqn,                          // 1 tributável, 2 imunidade, 3 exportação, 4 não incidência
    [Range(1, 3)] int RetencaoIssqn,                            // 1 não retido, 2 retido pelo tomador, 3 pelo intermediário
    [Range(0, 100)] decimal? AliquotaIssqn,
    NfseTribFedDto? TributacaoFederal,
    NfseTotTribDto? TotalTributos);

public record NfseTribFedDto(
    [MaxLength(3)] string? CstPisCofins,
    [Range(0, double.MaxValue)] decimal? BaseCalculoPisCofins,
    [Range(0, 100)] decimal? AliquotaPis,
    [Range(0, 100)] decimal? AliquotaCofins,
    [Range(0, double.MaxValue)] decimal? ValorPis,
    [Range(0, double.MaxValue)] decimal? ValorCofins,
    int? TipoRetencaoPisCofins,                                 // tpRetPisCofins (1–3)
    [Range(0, double.MaxValue)] decimal? ValorRetidoCpp,        // contribuição previdenciária
    [Range(0, double.MaxValue)] decimal? ValorRetidoIrrf,
    [Range(0, double.MaxValue)] decimal? ValorRetidoCsll);

public record NfseTotTribDto(
    [Range(0, double.MaxValue)] decimal? Federal,
    [Range(0, double.MaxValue)] decimal? Estadual,
    [Range(0, double.MaxValue)] decimal? Municipal);

public record NfseIbsCbsDto(
    int Finalidade,                                             // finNFSe — 0 regular (único valor vigente)
    int? IndicadorFinal,                                        // indFinal 0/1 — default 1
    [Required, MaxLength(6)] string CodigoIndicadorOperacao,    // cIndOp (tabela SEPEC)
    int? TipoOperacaoGov,                                       // tpOper 1–5 (ente governamental)
    int? TipoEnteGovernamental,                                 // tpEnteGov 1–4
    int? IndicadorDestinatario,                                 // indDest 0/1 — default 0
    [Required] NfseGibsCbsDto GibbsCbs);

public record NfseGibsCbsDto(
    [Required, MaxLength(3)] string Cst,                        // 3 dígitos (tabela SEPEC)
    [Required, MaxLength(6)] string CClassTrib,                 // 6 dígitos (tabela SEPEC)
    [MaxLength(2)] string? CodigoCreditoPresumido);

/// <summary>Corpo da substituição de NFS-e (roadmap item 3). O DPS informado
/// vira a NFS-e substituta; a original (chave no path) é substituída.</summary>
public record NfseDpsSubstituicaoRequest(
    [Required] NfseDpsRequest Dps,
    [Required, Range(1, 99)] int CMotivo, // tabela de motivos de substituição da SEFAZ Nacional (1–5, 99)
    [MaxLength(2000)] string? XMotivo);
