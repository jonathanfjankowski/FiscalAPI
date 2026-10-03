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
    List<ImpostoDto>? Impostos,
    ItemImpostosDtoV2? ImpostosV2 = null,
    [MaxLength(7)] string? Cest = null,          // v2 F2 — 7 dígitos
    [MaxLength(14)] string? Gtin = null,         // v2 F2 — EAN 8/12/13/14
    [MaxLength(6)] string? Unidade = null,       // v2 F2 — uCom/uTrib, default "UN"
    [Range(0, double.MaxValue)] decimal? ValorDesconto = null,  // v2 F2 — vDesc do item
    List<DiDto>? Dis = null);                    // v2 §7 — grupo DI (importação) do item

// Contrato v2 dos grupos de imposto (evolução aditiva — ver docs/plano-evolucao-contrato-v2.md).
// Um item usa 'impostos' (legado, ICMS 00/40/41/50) OU 'impostosV2' — nunca os dois.
// A API não calcula tributos: valores chegam prontos; validamos a aritmética (tolerância R$ 0,01).

public record ItemImpostosDtoV2(
    IcmsDto? Icms = null,
    IpiDto? Ipi = null,        // v2 F3 — regime normal
    PisDto? Pis = null,        // v2 F3
    CofinsDto? Cofins = null,  // v2 F3
    IbsCbsDto? IbsCbs = null,  // v2 F5 — reforma (LC 214/2025, NT 2025.x)
    IsDto? Is = null,          // v2 F5 — Imposto Seletivo
    IiDto? Ii = null);         // v2 §7 — Imposto de Importação (grupo II, item importado)

/// <summary>Reforma tributária (IBS/CBS). vIBS informado = UF + municipal.</summary>
public record IbsCbsDto(
    [Required, MaxLength(3)] string CstIbsCbs,       // 3 dígitos (tabela SEPEC)
    [Required, MaxLength(6)] string CClassTrib,      // 6 dígitos (tabela SEPEC) — obrigatório
    decimal? BaseCalculo = null,
    decimal? AliquotaIbsEstadual = null, decimal? ValorIbsEstadual = null,
    decimal? AliquotaIbsMunicipal = null, decimal? ValorIbsMunicipal = null,
    decimal? AliquotaCbs = null, decimal? ValorCbs = null);

public record IsDto(
    [Required, MaxLength(2)] string CstIs,           // 2 dígitos (SEPEC)
    [Required, MaxLength(6)] string CClassTribIs,
    decimal? BaseCalculo = null,
    decimal? Aliquota = null,                        // pIS
    decimal? Valor = null,                           // vIS
    [MaxLength(6)] string? UnidadeTributavel = null, // UTrib (tipo de base "quantidade")
    decimal? QuantidadeTributavel = null);           // QTrib

public record IpiDto(
    [Required, MaxLength(2)] string Cst,   // 00/49/50/99 tributado; 01–05/51 não tributado
    [MaxLength(3)] string? CEnq = null,    // cEnq — default 999
    decimal? BaseCalculo = null,
    decimal? Aliquota = null,
    decimal? Valor = null);

public record PisDto(
    [Required, MaxLength(2)] string Cst,   // 01/02 tributado; 04–09 isento; 99 outras
    decimal? BaseCalculo = null,
    decimal? Aliquota = null,
    decimal? Valor = null);

public record CofinsDto(
    [Required, MaxLength(2)] string Cst,
    decimal? BaseCalculo = null,
    decimal? Aliquota = null,
    decimal? Valor = null);

public record IcmsDto(
    int? Origem = null,                // 0–8 (tabela A) — default 0 (nacional)
    string? Cst = null,                // 00,10,20,40,41,51,60,70,90
    string? Csosn = null,              // 101,102,103,201,202,203,300,400,500,900
    string? ModBc = null,              // 0–3 — default 3 (valor da operação)
    decimal? PercentualReducaoBc = null, // CST 20/51/70 e CSOSN 900
    decimal? BaseCalculo = null,
    decimal? Aliquota = null,
    decimal? Valor = null,
    decimal? PercentualCreditoSimples = null, // pCredSN — CSOSN 101/201/900
    decimal? ValorCreditoSimples = null,      // vCredICMSSN — CSOSN 101/201/900
    decimal? FcpPercentual = null,            // pFCP (base = baseCalculo)
    decimal? ValorFcp = null,
    decimal? ValorIcmsOperacao = null,        // vICMSOp — CST 51
    decimal? PercentualDiferimento = null,    // pDif — CST 51
    decimal? ValorIcmsDiferido = null,        // vICMSDif — CST 51
    decimal? ValorDesonerado = null,          // vICMSDeson — CST 20/40/41/70/90 com motivo (NT 2019.001)
    [MaxLength(2)] string? MotivoDesoneracao = null, // motDesICMS — 3, 9 ou 12
    [MaxLength(10)] string? CodigoBeneficioFiscal = null, // cBenefRBC — código de benefício fiscal na UF (item)
    IcmsStDto? St = null,
    DifalDto? Difal = null,                   // interestadual consumidor final (ICMSUFDest)
    decimal? PercentualBcOperacao = null,     // v2 §7 — pBCOp (CST 10, ICMSPart)
    [MaxLength(2)] string? UfSt = null);      // v2 §7 — UFST (CST 10: UF da ST partilhada)

public record IcmsStDto(
    string? ModBcSt = null,            // 0–6 — obrigatório no ST próprio (10/70/90, CSOSN 201/202/203/900)
    decimal? PercentualMva = null,     // pMVAST
    decimal? PercentualReducaoBcSt = null, // pRedBCST
    decimal? BaseCalculoSt = null, decimal? AliquotaSt = null, decimal? ValorSt = null, // ST própria
    decimal? FcpPercentualSt = null, decimal? ValorFcpSt = null,        // FCP da ST própria
    decimal? BaseCalculoStRetido = null,   // vBCSTRet — CST 60 / CSOSN 500
    decimal? AliquotaStRetida = null,      // pST
    decimal? ValorStRetido = null,         // vICMSSTRet
    decimal? ValorIcmsSubstituto = null,   // vICMSSubstituto
    decimal? FcpPercentualStRetido = null, // pFCPSTRet
    decimal? ValorFcpStRetido = null);     // vFCPSTRet

public record DifalDto(
    int? AliquotaInterestadual = null, // pICMSInter: 4, 7 ou 12 — obrigatória quando DIFAL informado
    decimal? BaseDestino = null,       // vBCUFDest
    decimal? AliquotaDestino = null,   // pICMSUFDest
    decimal? ValorIcmsDestino = null,  // vICMSUFDest
    decimal? ValorIcmsOrigem = null,   // vICMSUFRemet (partilha 100% destino — Convênio 190/2017)
    decimal? FcpPercentualDestino = null, // pFCPUFDest
    decimal? ValorFcpDestino = null);  // vFCPUFDest

public record TotaisDto(
    [Range(0, double.MaxValue)] decimal ValorProdutos,
    [Range(0, double.MaxValue)] decimal ValorNota,
    [Range(0, double.MaxValue)] decimal? ValorDesconto = null,   // v2 F2 — desconto no total
    [Range(0, double.MaxValue)] decimal? ValorFrete = null,      // v2 F2 — compõe o total da nota
    [Range(0, double.MaxValue)] decimal? ValorSeguro = null,     // v2 F2
    [Range(0, double.MaxValue)] decimal? OutrasDespesas = null,  // v2 F2
    [Range(0, double.MaxValue)] decimal? ValorDesonerado = null, // Σ vICMSDeson — SUBTRAI do total (vNF −= vICMSDeson)
    [Range(0, double.MaxValue)] decimal? ValorIbs = null,        // v2 F5 — conferência
    [Range(0, double.MaxValue)] decimal? ValorCbs = null,        // v2 F5 — conferência
    [Range(0, double.MaxValue)] decimal? ValorIs = null);        // v2 F5 — conferência

public record PagamentoDto(
    [Required, MaxLength(2)] string Forma,
    [Range(0, double.MaxValue)] decimal Valor,
    [MaxLength(2)] string? TipoIntegracao = null,     // v2 §7 — tpIntegra: "1" integrado/credenciado, "2" não integrado
    [MaxLength(2)] string? Bandeira = null,           // v2 §7 — tBand (01 Visa, 02 Mastercard, 03 Amex…)
    [MaxLength(20)] string? Autorizacao = null,       // v2 §7 — cAut (código de autorização da operação)
    [MaxLength(14)] string? CnpjCredenciadora = null); // v2 §7 — CNPJ da credenciadora (CNPJ do card)

public record EmissaoRequest(
    [Required] string Ambiente,
    [Range(1, 999)] short Serie,
    DestinatarioDto? Destinatario,
    [Required, MinLength(1)] List<ItemDto> Itens,
    [Required] TotaisDto Totais,
    List<PagamentoDto>? Pagamento,
    string? NaturezaOperacao = null,
    string? Finalidade = null,               // v2 F4: normal|complementar|ajuste|devolucao (finNFe)
    string? TipoOperacao = null,             // v2 F4: saida|entrada (tpNF)
    string? IndicadorPresenca = null,        // v2 F4: presencial|internet|teleatendimento|entrega_domicilio|fora_estabelecimento|outros (indPres)
    string? IndicadorConsumidorFinal = null, // v2 F4: sim|nao (indFinal)
    List<NfRefDto>? NfesReferenciadas = null, // v2 F4 — grupo NFref; devolucao exige
    int? IndicadorIntermediador = null,      // NT 2020.006 (indIntermed): 0=sem intermediador (default na NF-e), 1=site/plataforma de terceiros; só NF-e (mod 55)
    string? CnpjIntermediador = null,        // CNPJ do intermediador — obrigatório quando indicadorIntermediador=1 (grupo infIntermed)
    TransporteDto? Transporte = null,        // v2 §7 — grupo transp (modalidade, transportadora, volumes/lacres)
    [MaxLength(5000)] string? InformacoesComplementares = null, // infCpl
    bool? ContingenciaOffline = null);       // v2 §7 — NFC-e offline (tpEmis 9): emite sem contato com a SEFAZ, transmite depois

public record NfRefDto(
    [Required, MaxLength(44)] string ChaveAcesso);

// ---- v2 §7 — transporte/volumes ------------------------------------------------

public record TransporteDto(
    [MaxLength(2)] string? ModalidadeFrete = null, // modFrete "0"-"9"; default "9" (sem ocorrência) / "0" (CIF) quando há frete
    TransportadoraDto? Transportadora = null,
    List<VolumeDto>? Volumes = null);              // grupo vol (máx. 100)

public record TransportadoraDto(
    [MaxLength(14)] string? CnpjCpf = null,        // CNPJ (14) ou CPF (11)
    [MaxLength(60)] string? Nome = null,
    [MaxLength(14)] string? InscricaoEstadual = null,
    [MaxLength(100)] string? EnderecoLogradouro = null,
    [MaxLength(60)] string? EnderecoMunicipio = null,
    [MaxLength(2)] string? EnderecoUf = null);

public record VolumeDto(
    [Range(0, int.MaxValue)] int? Quantidade = null,              // qVol
    [MaxLength(60)] string? Especie = null,                       // esp (caixa, pallet…)
    [MaxLength(60)] string? Marca = null,                         // marca
    [MaxLength(60)] string? Numeracao = null,                     // nVol
    [Range(0, double.MaxValue)] decimal? PesoLiquido = null,      // pesoL (kg)
    [Range(0, double.MaxValue)] decimal? PesoBruto = null,        // pesoB (kg)
    List<VolumeLacreDto>? Lacres = null);                         // grupo lacres (máx. 5000)

public record VolumeLacreDto(
    [Required, MaxLength(60)] string Numero);                     // nLacre

// ---- v2 §7 — importação (DI + II) ----------------------------------------------

public record DiDto(
    [Required, MaxLength(12)] string NumeroDi,                    // nDI
    [Required] DateTimeOffset DataRegistro,                       // dDI
    [Required, MaxLength(60)] string LocalDesembaraco,            // xLocDesemb
    [Required, MaxLength(2)] string UfDesembaraco,                // UFDesemb
    [Required] DateTimeOffset DataDesembaraco,                    // dDesemb
    [Required] int ViaTransporte,                                 // tpViaTransp 1–12
    [Required] int FormaIntermediacao,                            // tpIntermedio 1–4
    [Required, MaxLength(19)] string CodigoExportador,            // cExportador
    [Range(0, double.MaxValue)] decimal? ValorAfrmm = null,       // vAFRMM
    [MaxLength(14)] string? CnpjAdquirente = null,                // CNPJ adquirente/encomendante
    [MaxLength(14)] string? CnpjProdutor = null,                  // CNPJ produtor estrangeiro
    List<DiAdicaoDto>? Adicoes = null);                           // grupo adi (máx. 999)

public record DiAdicaoDto(
    [Required] int NumeroAdicao,                                  // nAdicao
    [Required] int Sequencial,                                    // nSeqAdic
    [Required, MaxLength(20)] string CodigoFabricante,            // cFabricante
    [Range(0, double.MaxValue)] decimal? ValorDescontoDi = null,  // vDescDI
    [MaxLength(11)] string? NumeroDrawback = null);               // nDraw

/// <summary>Imposto de Importação (grupo II) — item importado. Valores prontos
/// do ERP; compõem o total da nota junto com vIPI/vFrete etc.</summary>
public record IiDto(
    [Range(0, double.MaxValue)] decimal? BaseCalculo = null,             // vBC
    [Range(0, double.MaxValue)] decimal? ValorDespesasAduaneiras = null, // vDespAdu
    [Range(0, double.MaxValue)] decimal? ValorIi = null,                 // vII
    [Range(0, double.MaxValue)] decimal? ValorIof = null);               // vIOF

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
