using System.Security.Cryptography;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Xml.NFe;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Monta o EnviNFe (layout 4.00, modelo 55/65) a partir do DocumentoFiscal +
/// do EmissaoRequest serializado em doc.PayloadEntrada + do perfil fiscal do
/// tenant (emitente). A chave de acesso de 44 dígitos, o cDV e o QR code da
/// NFC-e são calculados pela própria Unimake (a partir do Ide e do CSC da
/// Configuracao, respectivamente) — não montamos chave aqui.
///
/// Defaults adotados (payload da API não os tem; documentados no README):
/// natOp = req.NaturezaOperacao ?? "VENDA", tpNF = saída, finNFe = normal,
/// indFinal = consumidor final, indPres = presencial, modBC = 3 (valor da
/// operação), unidade = "UN", transp sem ocorrência de transporte.
/// ICMS: lista plana legada ('impostos', CST 00/40/41/50) ou grupo tipado v2
/// ('impostosV2' — CST 00–90, CSOSN 101–900, ST, FCP e DIFAL; ver
/// docs/plano-evolucao-contrato-v2.md). Item rico (GTIN/CEST/unidade/
/// desconto), frete/seguro/outras e IPI/PIS/COFINS desde a F2/F3; Ide
/// configurável (finalidade, tipoOperacao, indPres, indFinal) e NF-ref
/// (devolução) desde a F4.
/// </summary>
public static class MapperEnviNFe
{
    private const string VerProc = "FiscalAPI 1.10.0";

    /// <summary>
    /// Dados do responsável técnico (grupo infRespTec — obrigatório em algumas
    /// SEFAZ, ex.: PR rejeita com 972 quando ausente). Vem da configuração
    /// (Fiscal:RespTec:*); quando ausente o grupo não é emitido.
    /// </summary>
    public sealed record RespTecDados(string Cnpj, string Contato, string Email, string Fone);

    public static EnviNFe Criar(DocumentoFiscal doc, Tenant tenant, EmissaoRequest req, Ambiente ambiente, RespTecDados? respTec = null)
    {
        var nfce = doc.Tipo == TipoDocumento.NFCE;
        if (doc.Serie is null || doc.Numero is null)
            throw new ErroNaoRecuperavelException("Documento sem série/número reservados.");

        if (!Enum.TryParse<UFBrasil>(tenant.Uf, ignoreCase: true, out var ufEmit) || ufEmit == UFBrasil.NaoDefinido)
            throw new ErroNaoRecuperavelException($"UF do tenant inválida para emissão: '{tenant.Uf}'.");

        if (string.IsNullOrWhiteSpace(tenant.Cnpj) || tenant.Cnpj.Length != 14)
            throw new ErroNaoRecuperavelException("CNPJ do tenant ausente ou inválido (esperado: 14 dígitos).");
        if (string.IsNullOrWhiteSpace(tenant.CodigoMunicipioIbge) || tenant.CodigoMunicipioIbge.Length != 7)
            throw new ErroNaoRecuperavelException("codigo_municipio_ibge do tenant ausente ou inválido (esperado: 7 dígitos).");
        if (string.IsNullOrWhiteSpace(tenant.InscricaoEstadual) ||
            string.IsNullOrWhiteSpace(tenant.Logradouro) ||
            string.IsNullOrWhiteSpace(tenant.Numero) ||
            string.IsNullOrWhiteSpace(tenant.Bairro) ||
            string.IsNullOrWhiteSpace(tenant.NomeMunicipio) ||
            string.IsNullOrWhiteSpace(tenant.Cep))
            throw new ErroNaoRecuperavelException(
                "Perfil fiscal do emitente incompleto (inscrição estadual e endereço são obrigatórios " +
                "para emissão real). Configure via PUT /v1/tenants/perfil.");

        if (!nfce && req.Destinatario is null)
            throw new ErroNaoRecuperavelException(
                "Emissão de NF-e (modelo 55) exige 'destinatario' no payload. " +
                "NFC-e permite consumidor não identificado.");

        var nfe = new NFe
        {
            InfNFeField = new InfNFe
            {
                Versao = "4.00",
                Ide = MapearIde(doc, tenant, req, ambiente, nfce, ufEmit),
                Emit = MapearEmit(tenant, ufEmit),
                Det = MapearDets(req, nfce),
                Total = MapearTotal(req),
                Transp = new Transp
                {
                    ModFrete = req.Totais.ValorFrete is > 0
                        ? ModalidadeFrete.ContratacaoFretePorContaRemetente_CIF
                        : ModalidadeFrete.SemOcorrenciaTransporte,
                },
                Pag = MapearPag(req, nfce),
            }
        };
        if (req.Destinatario is not null)
            nfe.InfNFeField.Dest = MapearDest(req.Destinatario, ufEmit);

        if (respTec is not null)
            nfe.InfNFeField.InfRespTec = new InfRespTec
            {
                CNPJ = respTec.Cnpj,
                XContato = respTec.Contato,
                Email = respTec.Email,
                Fone = respTec.Fone,
            };

        if (!nfce && req.CnpjIntermediador is { } cnpjInt && !string.IsNullOrWhiteSpace(cnpjInt))
            nfe.InfNFeField.InfIntermed = new InfIntermed
            {
                CNPJ = new string(cnpjInt.Where(char.IsDigit).ToArray()),
            };

        return new EnviNFe
        {
            Versao = "4.00",
            IdLote = LoteDe(doc.Id),
            // Lote sempre tem exatamente 1 NF-e → processamento síncrono é
            // obrigatório (SEFAZ-PR rejeita com 452 lote unitário assíncrono;
            // demais UFs aceitam sincronizado). EmissorNFe interpreta o
            // protNFe vindo na própria resposta; o caminho assíncrono (103 +
            // recibo) permanece como fallback.
            IndSinc = SimNao.Sim,
            NFe = new List<NFe> { nfe },
        };
    }

    private static Ide MapearIde(DocumentoFiscal doc, Tenant tenant, EmissaoRequest req, Ambiente ambiente, bool nfce, UFBrasil ufEmit)
    {
        var indPres = req.IndicadorPresenca?.Trim().ToLowerInvariant() switch
        {
            "presencial" => IndicadorPresenca.OperacaoPresencial,
            "internet" => IndicadorPresenca.OperacaoInternet,
            "teleatendimento" => IndicadorPresenca.OperacaoTeleAtendimento,
            "entrega_domicilio" => IndicadorPresenca.NFCeEntregaDomicilio,
            "fora_estabelecimento" => IndicadorPresenca.PresencialForaEstabelecimento,
            "outros" => IndicadorPresenca.OperacaoOutros,
            null or "" => IndicadorPresencaPadrao(req, nfce, tenant),
            _ => throw new ErroNaoRecuperavelException(
                $"indicadorPresenca '{req.IndicadorPresenca}' inválido — use presencial, internet, teleatendimento, " +
                "entrega_domicilio, fora_estabelecimento ou outros."),
        };

        return new Ide
        {
            CUF = ufEmit,
            CNF = CNFDe(doc.Id),
            NatOp = string.IsNullOrWhiteSpace(req.NaturezaOperacao) ? "VENDA" : req.NaturezaOperacao.Trim(),
            Mod = nfce ? ModeloDFe.NFCe : ModeloDFe.NFe,
            Serie = doc.Serie!.Value,
            NNF = checked((int)doc.Numero!.Value),
            // DhEmi = aceitação da request: mantém a chave determinística entre
            // tentativas (SVC retransmite com a MESMA chave; agora no fluxo
            // normal também — a chave não muda se o job tentar de novo em outro mês).
            DhEmi = doc.CriadoEm,
            TpNF = req.TipoOperacao?.Trim().ToLowerInvariant() switch
            {
                "entrada" => TipoOperacao.Entrada,
                "saida" => TipoOperacao.Saida,
                null or "" => TipoOperacao.Saida,
                _ => throw new ErroNaoRecuperavelException(
                    $"tipoOperacao '{req.TipoOperacao}' inválido — use saida ou entrada."),
            },
            IdDest = IdDestDe(req, nfce, tenant),
            CMunFG = int.Parse(tenant.CodigoMunicipioIbge!),
            TpImp = nfce ? FormatoImpressaoDANFE.NFCe : FormatoImpressaoDANFE.NormalRetrato,
            TpEmis = doc.ModoContingencia switch
            {
                "SVCAN" => TipoEmissao.ContingenciaSVCAN,
                "SVCRS" => TipoEmissao.ContingenciaSVCRS,
                _ => TipoEmissao.Normal,
            },
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            FinNFe = req.Finalidade?.Trim().ToLowerInvariant() switch
            {
                "normal" => FinalidadeNFe.Normal,
                "complementar" => FinalidadeNFe.Complementar,
                "ajuste" => FinalidadeNFe.Ajuste,
                "devolucao" => FinalidadeNFe.Devolucao,
                null or "" => FinalidadeNFe.Normal,
                _ => throw new ErroNaoRecuperavelException(
                    $"finalidade '{req.Finalidade}' inválida — use normal, complementar, ajuste ou devolucao."),
            },
            IndFinal = req.IndicadorConsumidorFinal?.Trim().ToLowerInvariant() switch
            {
                "nao" => SimNao.Nao,
                "sim" => SimNao.Sim,
                null or "" => SimNao.Sim,
                _ => throw new ErroNaoRecuperavelException(
                    $"indicadorConsumidorFinal '{req.IndicadorConsumidorFinal}' inválido — use sim ou nao."),
            },
            IndPres = indPres,
            // indIntermed (NT 2020.006): PR rejeita NF-e sem o campo (434).
            // Default 0 = operação sem intermediador; só existe no leiaute mod 55.
            IndIntermed = MapearIndIntermed(req, nfce),
            ProcEmi = ProcessoEmissao.AplicativoContribuinte,
            VerProc = VerProc,
            NFref = MapearNfref(req, numeroItem: 0),
        };
    }

    private static IndicadorPresenca IndicadorPresencaPadrao(EmissaoRequest req, bool nfce, Tenant tenant)
    {
        // Default histórico: presencial; internet quando a UF do destinatário difere (NF-e).
        if (!nfce && req.Destinatario?.Endereco?.Uf is { Length: 2 } ufDest
            && !string.Equals(ufDest, tenant.Uf, StringComparison.OrdinalIgnoreCase))
            return IndicadorPresenca.OperacaoInternet;
        return IndicadorPresenca.OperacaoPresencial;
    }

    /// <summary>IdDest: interna quando NFC-e ou sem UF de destinatário; senão,
    /// interestadual quando a UF do destinatário difere da UF do emitente.</summary>
    private static DestinoOperacao IdDestDe(EmissaoRequest req, bool nfce, Tenant tenant)
    {
        if (nfce || req.Destinatario?.Endereco?.Uf is not { Length: 2 } ufDest)
            return DestinoOperacao.OperacaoInterna;
        return string.Equals(ufDest, tenant.Uf, StringComparison.OrdinalIgnoreCase)
            ? DestinoOperacao.OperacaoInterna
            : DestinoOperacao.OperacaoInterestadual;
    }

    private static IndicadorIntermediario? MapearIndIntermed(EmissaoRequest req, bool nfce)
    {
        if (nfce)
            return null; // grupo só existe no leiaute da NF-e (mod 55)

        var v = req.IndicadorIntermediador;
        if (v is null)
            return IndicadorIntermediario.OperacaoSemIntermediador; // default: sem intermediador
        if (v == 0)
            return IndicadorIntermediario.OperacaoSemIntermediador;
        if (v == 1)
        {
            if (string.IsNullOrWhiteSpace(req.CnpjIntermediador))
                throw new ErroNaoRecuperavelException(
                    "indicadorIntermediador=1 exige cnpjIntermediador (grupo infIntermed).");
            return IndicadorIntermediario.OperacaoSitePlataformaTerceiro;
        }
        throw new ErroNaoRecuperavelException(
            $"indicadorIntermediador {v} inválido — use 0 (sem intermediador) ou 1 (plataforma de terceiros).");
    }

    private static List<NFref> MapearNfref(EmissaoRequest req, int numeroItem)
    {
        if (req.NfesReferenciadas is not { Count: > 0 })
            return [];

        return req.NfesReferenciadas.Select(refDto =>
        {
            var chave = new string((refDto.ChaveAcesso ?? "").Where(char.IsDigit).ToArray());
            if (chave.Length != 44)
                throw new ErroNaoRecuperavelException(
                    $"Chave de NF-e referenciada inválida (esperado 44 dígitos): '{refDto.ChaveAcesso}'.");
            return new NFref { RefNFe = chave };
        }).ToList();
    }

    private static Emit MapearEmit(Tenant tenant, UFBrasil ufEmit) => new()
    {
        CNPJ = tenant.Cnpj,
        XNome = tenant.RazaoSocial,
        IE = tenant.InscricaoEstadual,
        CRT = tenant.RegimeTributario switch
        {
            1 => CRT.SimplesNacional,
            2 => CRT.SimplesNacionalExcessoSublimite,
            4 => CRT.SimplesNacionalMEI,
            _ => CRT.RegimeNormal,
        },
        EnderEmit = new EnderEmit
        {
            XLgr = tenant.Logradouro,
            Nro = tenant.Numero,
            XCpl = tenant.Complemento,
            XBairro = tenant.Bairro,
            CMun = int.Parse(tenant.CodigoMunicipioIbge!),
            XMun = tenant.NomeMunicipio,
            UF = ufEmit,
            CEP = tenant.Cep,
        },
    };

    private static Dest MapearDest(DestinatarioDto dest, UFBrasil ufEmit)
    {
        var documento = new string(dest.CnpjCpf.Where(char.IsDigit).ToArray());
        var ie = dest.InscricaoEstadual?.Trim();

        var result = new Dest
        {
            XNome = dest.Nome,
            IndIEDest = ie is null
                ? IndicadorIEDestinatario.NaoContribuinte
                : ie.Equals("ISENTO", StringComparison.OrdinalIgnoreCase)
                    ? IndicadorIEDestinatario.ContribuinteIsento
                    : IndicadorIEDestinatario.ContribuinteICMS,
            IE = ie is null || ie.Equals("ISENTO", StringComparison.OrdinalIgnoreCase) ? null : ie,
        };

        if (documento.Length == 11) result.CPF = documento;
        else if (documento.Length == 14) result.CNPJ = documento;
        else throw new ErroNaoRecuperavelException(
            $"CnpjCpf do destinatário inválido (esperado 11 ou 14 dígitos): '{dest.CnpjCpf}'.");

        var end = dest.Endereco;
        if (end is not null)
        {
            if (string.IsNullOrWhiteSpace(end.Logradouro) || string.IsNullOrWhiteSpace(end.Numero) ||
                string.IsNullOrWhiteSpace(end.Bairro) || string.IsNullOrWhiteSpace(end.CodigoMunicipioIbge) ||
                string.IsNullOrWhiteSpace(end.NomeMunicipio) || string.IsNullOrWhiteSpace(end.Uf))
                throw new ErroNaoRecuperavelException(
                    "Endereço do destinatário incompleto: logradouro, numero, bairro, " +
                    "codigoMunicipioIbge, nomeMunicipio e uf são obrigatórios quando informado.");

            if (!Enum.TryParse<UFBrasil>(end.Uf, ignoreCase: true, out var ufDest) || ufDest == UFBrasil.NaoDefinido)
                throw new ErroNaoRecuperavelException($"UF do destinatário inválida: '{end.Uf}'.");

            result.EnderDest = new EnderDest
            {
                XLgr = end.Logradouro,
                Nro = end.Numero,
                XCpl = end.Complemento,
                XBairro = end.Bairro,
                CMun = int.Parse(end.CodigoMunicipioIbge),
                XMun = end.NomeMunicipio,
                UF = ufDest,
                CEP = end.Cep,
            };
        }
        else if (result.IndIEDest == IndicadorIEDestinatario.ContribuinteICMS)
        {
            throw new ErroNaoRecuperavelException(
                "Destinatário contribuinte de ICMS exige endereço completo no payload.");
        }

        return result;
    }

    private static List<Det> MapearDets(EmissaoRequest req, bool nfce)
    {
        var dets = new List<Det>(req.Itens.Count);
        for (var i = 0; i < req.Itens.Count; i++)
        {
            var item = req.Itens[i];
            var impostoV2 = item.ImpostosV2?.Icms;

            ICMS icms;
            ICMSUFDest? icmsUfDest = null;
            if (impostoV2 is not null)
                icms = MapearIcmsV2(impostoV2, i + 1, item.Codigo, out icmsUfDest);
            else
                icms = MapearICMS((item.Impostos ?? []).FirstOrDefault(), i + 1, item.Codigo);

            var imposto = new Imposto { ICMS = icms };
            if (icmsUfDest is not null)
                imposto.ICMSUFDest = icmsUfDest;

            // Grupos federais (v2 F3) — só NF-e; NFC-e não os admite.
            var v2 = item.ImpostosV2;
            if (v2 is not null && nfce && (v2.Ipi is not null || v2.Pis is not null || v2.Cofins is not null))
                throw new ErroNaoRecuperavelException(
                    $"Item {i + 1} ('{item.Codigo}'): NFC-e não admite os grupos IPI/PIS/COFINS.");
            if (v2?.Ipi is not null) imposto.IPI = MapearIpi(v2.Ipi, i + 1, item.Codigo);
            if (v2?.Pis is not null) imposto.PIS = MapearPis(v2.Pis, i + 1, item.Codigo);
            if (v2?.Cofins is not null) imposto.COFINS = MapearCofins(v2.Cofins, i + 1, item.Codigo);
            if (v2?.IbsCbs is not null) imposto.IBSCBS = MapearIbsCbs(v2.IbsCbs, i + 1, item.Codigo);
            if (v2?.Is is not null) imposto.IS = MapearIs(v2.Is, i + 1, item.Codigo);

            var unidade = string.IsNullOrWhiteSpace(item.Unidade) ? "UN" : item.Unidade;

            dets.Add(new Det
            {
                NItem = i + 1,
                Prod = new Prod
                {
                    CProd = item.Codigo,
                    CEAN = string.IsNullOrWhiteSpace(item.Gtin) ? "SEM GTIN" : item.Gtin,
                    CEANTrib = string.IsNullOrWhiteSpace(item.Gtin) ? "SEM GTIN" : item.Gtin,
                    CEST = string.IsNullOrWhiteSpace(item.Cest) ? null : item.Cest,
                    XProd = item.Descricao,
                    NCM = string.IsNullOrWhiteSpace(item.Ncm)
                        ? throw new ErroNaoRecuperavelException($"Item {i + 1} ('{item.Codigo}') sem NCM — obrigatório na NF-e.")
                        : item.Ncm,
                    CFOP = string.IsNullOrWhiteSpace(item.Cfop)
                        ? throw new ErroNaoRecuperavelException($"Item {i + 1} ('{item.Codigo}') sem CFOP — obrigatório na NF-e.")
                        : item.Cfop,
                    UCom = unidade,
                    QCom = item.Quantidade,
                    VUnCom = item.ValorUnitario,
                    VProd = (double)item.ValorTotal,
                    UTrib = unidade,
                    QTrib = item.Quantidade,
                    VUnTrib = item.ValorUnitario,
                    IndTot = SimNao.Sim,
                },
                Imposto = imposto,
            });
            if (item.ValorDesconto is { } desconto)
                dets[^1].Prod.VDesc = (double)desconto;
        }
        return dets;
    }

    private static IPI MapearIpi(IpiDto ipi, int numeroItem, string codigoItem)
    {
        var cst = ipi.Cst.Trim();
        var grupo = new IPI
        {
            CEnq = string.IsNullOrWhiteSpace(ipi.CEnq) ? "999" : ipi.CEnq,
        };

        if (cst is "00" or "49" or "50" or "99")
        {
            if (ipi.BaseCalculo is null || ipi.Aliquota is null || ipi.Valor is null)
                throw new ErroNaoRecuperavelException(
                    $"Item {numeroItem} ('{codigoItem}'): IPI CST {cst} exige baseCalculo, aliquota e valor.");
            grupo.IPITrib = new IPITrib
            {
                CST = cst,
                VBC = (double)ipi.BaseCalculo.Value,
                PIPI = (double)ipi.Aliquota.Value,
                VIPI = (double)ipi.Valor.Value,
            };
            return grupo;
        }

        if (cst is "01" or "02" or "03" or "04" or "05" or "51")
        {
            if (ipi.Valor is > 0)
                throw new ErroNaoRecuperavelException(
                    $"Item {numeroItem} ('{codigoItem}'): IPI CST {cst} não tributado — valor não é permitido.");
            grupo.IPINT = new IPINT { CST = cst };
            return grupo;
        }

        throw new ErroNaoRecuperavelException(
            $"Item {numeroItem} ('{codigoItem}'): IPI CST '{cst}' fora do contrato (00, 01–05, 49, 50, 51, 99).");
    }

    private static PIS MapearPis(PisDto pis, int numeroItem, string codigoItem)
    {
        var cst = pis.Cst.Trim();

        if (cst is "01" or "02")
        {
            if (pis.BaseCalculo is null || pis.Aliquota is null || pis.Valor is null)
                throw new ErroNaoRecuperavelException(
                    $"Item {numeroItem} ('{codigoItem}'): PIS CST {cst} exige baseCalculo, aliquota e valor.");
            return new PIS
            {
                PISAliq = new PISAliq
                {
                    CST = cst,
                    VBC = (double)pis.BaseCalculo.Value,
                    PPIS = (double)pis.Aliquota.Value,
                    VPIS = (double)pis.Valor.Value,
                },
            };
        }

        if (cst is "03")
            throw new ErroNaoRecuperavelException(
                $"Item {numeroItem} ('{codigoItem}'): PIS CST 03 (por quantidade) não suportado no contrato atual.");

        if (cst is "04" or "05" or "06" or "07" or "08" or "09")
        {
            if (pis.Valor is > 0)
                throw new ErroNaoRecuperavelException(
                    $"Item {numeroItem} ('{codigoItem}'): PIS CST {cst} isento — valor não é permitido.");
            return new PIS { PISNT = new PISNT { CST = cst } };
        }

        if (cst is "99")
        {
            var outr = new PISOutr { CST = cst };
            if (pis.BaseCalculo is { } bc) outr.VBC = (double)bc;
            if (pis.Aliquota is { } p) outr.PPIS = (double)p;
            if (pis.Valor is { } v) outr.VPIS = (double)v;
            return new PIS { PISOutr = outr };
        }

        throw new ErroNaoRecuperavelException(
            $"Item {numeroItem} ('{codigoItem}'): PIS CST '{cst}' fora do contrato (01, 02, 04–09, 99).");
    }

    private static COFINS MapearCofins(CofinsDto cofins, int numeroItem, string codigoItem)
    {
        var cst = cofins.Cst.Trim();

        if (cst is "01" or "02")
        {
            if (cofins.BaseCalculo is null || cofins.Aliquota is null || cofins.Valor is null)
                throw new ErroNaoRecuperavelException(
                    $"Item {numeroItem} ('{codigoItem}'): COFINS CST {cst} exige baseCalculo, aliquota e valor.");
            return new COFINS
            {
                COFINSAliq = new COFINSAliq
                {
                    CST = cst,
                    VBC = (double)cofins.BaseCalculo.Value,
                    PCOFINS = (double)cofins.Aliquota.Value,
                    VCOFINS = (double)cofins.Valor.Value,
                },
            };
        }

        if (cst is "03")
            throw new ErroNaoRecuperavelException(
                $"Item {numeroItem} ('{codigoItem}'): COFINS CST 03 (por quantidade) não suportado no contrato atual.");

        if (cst is "04" or "05" or "06" or "07" or "08" or "09")
        {
            if (cofins.Valor is > 0)
                throw new ErroNaoRecuperavelException(
                    $"Item {numeroItem} ('{codigoItem}'): COFINS CST {cst} isento — valor não é permitido.");
            return new COFINS { COFINSNT = new COFINSNT { CST = cst } };
        }

        if (cst is "99")
        {
            var outr = new COFINSOutr { CST = cst };
            if (cofins.BaseCalculo is { } bc) outr.VBC = (double)bc;
            if (cofins.Aliquota is { } p) outr.PCOFINS = (double)p;
            if (cofins.Valor is { } v) outr.VCOFINS = (double)v;
            return new COFINS { COFINSOutr = outr };
        }

        throw new ErroNaoRecuperavelException(
            $"Item {numeroItem} ('{codigoItem}'): COFINS CST '{cst}' fora do contrato (01, 02, 04–09, 99).");
    }

    private static IBSCBS MapearIbsCbs(IbsCbsDto dto, int numeroItem, string codigoItem)
    {
        Exigir(!string.IsNullOrWhiteSpace(dto.CClassTrib),
            "IBS/CBS exige cClassTrib (6 dígitos, tabela SEPEC).", numeroItem, codigoItem);

        var ibsUf = new GIBSUF();
        if (dto.AliquotaIbsEstadual is { } pUf) ibsUf.PIBSUF = (double)pUf;
        if (dto.ValorIbsEstadual is { } vUf) ibsUf.VIBSUF = (double)vUf;

        var ibsMun = new GIBSMun();
        if (dto.AliquotaIbsMunicipal is { } pMun) ibsMun.PIBSMun = (double)pMun;
        if (dto.ValorIbsMunicipal is { } vMun) ibsMun.VIBSMun = (double)vMun;

        var gcbs = new GCBS();
        if (dto.AliquotaCbs is { } pCbs) gcbs.PCBS = (double)pCbs;
        if (dto.ValorCbs is { } vCbs) gcbs.VCBS = (double)vCbs;

        var grupo = new GIBSCBS
        {
            GIBSUF = ibsUf,
            GIBSMun = ibsMun,
            GCBS = gcbs,
            // vIBS do item = vIBSUF + vIBSMun (SEFAZ valida: rejeição 1150).
            VIBS = (double)(dto.ValorIbsEstadual ?? 0m) + (double)(dto.ValorIbsMunicipal ?? 0m),
        };
        if (dto.BaseCalculo is { } bc) grupo.VBC = (double)bc;

        return new IBSCBS
        {
            CST = dto.CstIbsCbs,
            CClassTrib = dto.CClassTrib,
            GIBSCBS = grupo,
        };
    }

    private static IS MapearIs(IsDto dto, int numeroItem, string codigoItem)
    {
        Exigir(!string.IsNullOrWhiteSpace(dto.CClassTribIs),
            "IS exige cClassTribIs (6 dígitos, tabela SEPEC).", numeroItem, codigoItem);

        var is_ = new IS
        {
            CSTIS = dto.CstIs,
            CClassTribIS = dto.CClassTribIs,
        };
        if (dto.BaseCalculo is { } bc)
        {
            is_.VBCIS = (double)bc;
            is_.UTrib = dto.UnidadeTributavel ?? "UN";
            is_.QTrib = dto.QuantidadeTributavel is { } q ? (double)q : 1;
        }
        if (dto.Aliquota is { } p) is_.PIS = (double)p;
        if (dto.Valor is { } v) is_.VIS = (double)v;
        return is_;
    }

    private static ICMS MapearICMS(ImpostoDto? imposto, int numeroItem, string codigoItem)
    {
        if (imposto is null)
            throw new ErroNaoRecuperavelException(
                $"Item {numeroItem} ('{codigoItem}') sem grupo de impostos — informe 'impostos' (legado) ou 'impostosV2'.");

        var cst = imposto.Cst.Trim();
        var origem = OrigemMercadoria.Nacional;

        if (cst == "00")
        {
            if (imposto.BaseCalculo is null || imposto.Aliquota is null || imposto.Valor is null)
                throw new ErroNaoRecuperavelException(
                    $"Item {numeroItem} ('{codigoItem}'): CST 00 exige baseCalculo, aliquota e valor.");

            return new ICMS
            {
                ICMS00 = new ICMS00
                {
                    Orig = origem,
                    CST = "00",
                    ModBC = ModalidadeBaseCalculoICMS.ValorOperacao,
                    VBC = (double)imposto.BaseCalculo.Value,
                    PICMS = (double)imposto.Aliquota.Value,
                    VICMS = (double)imposto.Valor.Value,
                },
            };
        }

        if (cst is "40" or "41" or "50")
            return new ICMS { ICMS40 = new ICMS40 { Orig = origem, CST = cst } };

        throw new ErroNaoRecuperavelException(
            $"Item {numeroItem} ('{codigoItem}'): CST '{cst}' não suportado pela lista plana 'impostos' " +
            "(suporta 00, 40, 41 e 50; os demais CSTs e CSOSN do Simples Nacional usam o grupo 'impostosV2').");
    }

    private static ICMS MapearIcmsV2(IcmsDto icms, int numeroItem, string codigoItem, out ICMSUFDest? icmsUfDest)
    {
        icmsUfDest = MapearDifal(icms.Difal, numeroItem, codigoItem);
        var origem = MapearOrigem(icms.Origem);
        var st = icms.St;

        if (icms.Cst is { } cst)
        {
            switch (cst)
            {
                case "00":
                    Exigir(icms.BaseCalculo is not null && icms.Aliquota is not null && icms.Valor is not null,
                        $"CST 00 exige baseCalculo, aliquota e valor.", numeroItem, codigoItem);
                    var icms00 = new ICMS00
                    {
                        Orig = origem,
                        CST = "00",
                        ModBC = MapearModBc(icms.ModBc, numeroItem, codigoItem),
                        VBC = (double)icms.BaseCalculo!.Value,
                        PICMS = (double)icms.Aliquota!.Value,
                        VICMS = (double)icms.Valor!.Value,
                    };
                    PreencherFcp(icms00, icms);
                    return new ICMS { ICMS00 = icms00 };

                case "10":
                    var st10 = ExigirSt(st, numeroItem, codigoItem, "10");
                    var icms10 = new ICMS10
                    {
                        Orig = origem,
                        CST = "10",
                        ModBC = MapearModBc(icms.ModBc, numeroItem, codigoItem),
                        VBC = (double)icms.BaseCalculo!.Value,
                        PICMS = (double)icms.Aliquota!.Value,
                        VICMS = (double)icms.Valor!.Value,
                    };
                    PreencherFcp(icms10, icms);
                    PreencherSt(icms10, st10);
                    return new ICMS { ICMS10 = icms10 };

                case "20":
                    Exigir(icms.PercentualReducaoBc is not null, "CST 20 exige percentualReducaoBc.", numeroItem, codigoItem);
                    var icms20 = new ICMS20
                    {
                        Orig = origem,
                        CST = "20",
                        ModBC = MapearModBc(icms.ModBc, numeroItem, codigoItem),
                        PRedBC = (double)icms.PercentualReducaoBc!.Value,
                        VBC = (double)icms.BaseCalculo!.Value,
                        PICMS = (double)icms.Aliquota!.Value,
                        VICMS = (double)icms.Valor!.Value,
                    };
                    PreencherFcp(icms20, icms);
                    return new ICMS { ICMS20 = icms20 };

                case "40" or "41" or "50":
                    return new ICMS { ICMS40 = new ICMS40 { Orig = origem, CST = cst } };

                case "51":
                    Exigir(icms.ValorIcmsOperacao is not null, "CST 51 exige valorIcmsOperacao (vICMSOp).", numeroItem, codigoItem);
                    var icms51 = new ICMS51
                    {
                        Orig = origem,
                        CST = "51",
                        VICMSOp = (double)icms.ValorIcmsOperacao!.Value,
                    };
                    if (icms.ModBc is not null) icms51.ModBC = MapearModBc(icms.ModBc, numeroItem, codigoItem);
                    if (icms.PercentualReducaoBc is not null) icms51.PRedBC = (double)icms.PercentualReducaoBc.Value;
                    if (icms.BaseCalculo is not null) icms51.VBC = (double)icms.BaseCalculo.Value;
                    if (icms.Aliquota is not null) icms51.PICMS = (double)icms.Aliquota.Value;
                    if (icms.PercentualDiferimento is not null) icms51.PDif = (double)icms.PercentualDiferimento.Value;
                    if (icms.ValorIcmsDiferido is not null) icms51.VICMSDif = (double)icms.ValorIcmsDiferido.Value;
                    if (icms.Valor is not null) icms51.VICMS = (double)icms.Valor.Value;
                    PreencherFcp(icms51, icms);
                    return new ICMS { ICMS51 = icms51 };

                case "60":
                    var icms60 = new ICMS60 { Orig = origem, CST = "60" };
                    if (st is not null) PreencherStRetido(icms60, st);
                    return new ICMS { ICMS60 = icms60 };

                case "70":
                    var st70 = ExigirSt(st, numeroItem, codigoItem, "70");
                    Exigir(icms.PercentualReducaoBc is not null, "CST 70 exige percentualReducaoBc.", numeroItem, codigoItem);
                    var icms70 = new ICMS70
                    {
                        Orig = origem,
                        CST = "70",
                        ModBC = MapearModBc(icms.ModBc, numeroItem, codigoItem),
                        PRedBC = (double)icms.PercentualReducaoBc!.Value,
                        VBC = (double)icms.BaseCalculo!.Value,
                        PICMS = (double)icms.Aliquota!.Value,
                        VICMS = (double)icms.Valor!.Value,
                    };
                    PreencherFcp(icms70, icms);
                    PreencherSt(icms70, st70);
                    return new ICMS { ICMS70 = icms70 };

                case "90":
                    var icms90 = new ICMS90 { Orig = origem, CST = "90" };
                    if (icms.BaseCalculo is not null)
                    {
                        icms90.VBC = (double)icms.BaseCalculo.Value;
                        icms90.ModBC = MapearModBc(icms.ModBc, numeroItem, codigoItem);
                    }
                    if (icms.PercentualReducaoBc is not null) icms90.PRedBC = (double)icms.PercentualReducaoBc.Value;
                    if (icms.Aliquota is not null) icms90.PICMS = (double)icms.Aliquota.Value;
                    if (icms.Valor is not null) icms90.VICMS = (double)icms.Valor.Value;
                    PreencherFcp(icms90, icms);
                    if (st is not null) PreencherSt(icms90, st);
                    return new ICMS { ICMS90 = icms90 };

                default:
                    throw new ErroNaoRecuperavelException(
                        $"Item {numeroItem} ('{codigoItem}'): CST '{cst}' fora do contrato v2 atual.");
            }
        }

        if (icms.Csosn is { } csosn)
        {
            switch (csosn)
            {
                case "101":
                    var sn101 = new ICMSSN101 { Orig = origem, CSOSN = csosn };
                    if (icms.PercentualCreditoSimples is not null)
                        sn101.PCredSN = (double)icms.PercentualCreditoSimples.Value;
                    if (icms.ValorCreditoSimples is not null)
                        sn101.VCredICMSSN = (double)icms.ValorCreditoSimples.Value;
                    return new ICMS { ICMSSN101 = sn101 };

                case "102" or "103" or "300" or "400":
                    return new ICMS { ICMSSN102 = new ICMSSN102 { Orig = origem, CSOSN = csosn } };

                case "201":
                    var st201 = ExigirSt(st, numeroItem, codigoItem, "CSOSN 201");
                    var sn201 = new ICMSSN201
                    {
                        Orig = origem,
                        CSOSN = csosn,
                        PCredSN = (double?)icms.PercentualCreditoSimples,
                        VCredICMSSN = (double?)icms.ValorCreditoSimples,
                    };
                    PreencherSt(sn201, st201);
                    return new ICMS { ICMSSN201 = sn201 };

                case "202" or "203":
                    var st202 = ExigirSt(st, numeroItem, codigoItem, $"CSOSN {csosn}");
                    var sn202 = new ICMSSN202 { Orig = origem, CSOSN = csosn };
                    PreencherSt(sn202, st202);
                    return new ICMS { ICMSSN202 = sn202 };

                case "500":
                    var sn500 = new ICMSSN500 { Orig = origem, CSOSN = csosn };
                    if (st is not null) PreencherStRetido(sn500, st);
                    return new ICMS { ICMSSN500 = sn500 };

                case "900":
                    var sn900 = new ICMSSN900 { Orig = origem, CSOSN = csosn };
                    if (icms.BaseCalculo is not null)
                    {
                        sn900.VBC = (double)icms.BaseCalculo.Value;
                        sn900.ModBC = MapearModBc(icms.ModBc, numeroItem, codigoItem);
                    }
                    if (icms.PercentualReducaoBc is not null) sn900.PRedBC = (double)icms.PercentualReducaoBc.Value;
                    if (icms.Aliquota is not null) sn900.PICMS = (double)icms.Aliquota.Value;
                    if (icms.Valor is not null) sn900.VICMS = (double)icms.Valor.Value;
                    if (icms.PercentualCreditoSimples is not null) sn900.PCredSN = (double)icms.PercentualCreditoSimples.Value;
                    if (icms.ValorCreditoSimples is not null) sn900.VCredICMSSN = (double)icms.ValorCreditoSimples.Value;
                    if (st is not null) PreencherSt(sn900, st);
                    return new ICMS { ICMSSN900 = sn900 };

                default:
                    throw new ErroNaoRecuperavelException(
                        $"Item {numeroItem} ('{codigoItem}'): CSOSN '{csosn}' fora do contrato v2 atual.");
            }
        }

        throw new ErroNaoRecuperavelException(
            $"Item {numeroItem} ('{codigoItem}'): impostosV2.icms sem 'cst' nem 'csosn'.");
    }

    private static OrigemMercadoria MapearOrigem(int? origem)
    {
        var valor = origem ?? 0;
        if (valor is < 0 or > 8)
            throw new ErroNaoRecuperavelException($"Origem {valor} inválida — use 0 a 8 (tabela A do layout 4.00).");
        return (OrigemMercadoria)valor;
    }

    private static ModalidadeBaseCalculoICMS MapearModBc(string? modBc, int numeroItem, string codigoItem)
    {
        if (string.IsNullOrWhiteSpace(modBc))
            return ModalidadeBaseCalculoICMS.ValorOperacao; // default 3
        return modBc.Trim() switch
        {
            "0" => ModalidadeBaseCalculoICMS.MargemValorAgregado,
            "1" => ModalidadeBaseCalculoICMS.Pauta,
            "2" => ModalidadeBaseCalculoICMS.PrecoTabeladoMaximo,
            "3" => ModalidadeBaseCalculoICMS.ValorOperacao,
            _ => throw new ErroNaoRecuperavelException(
                $"Item {numeroItem} ('{codigoItem}'): modBc '{modBc}' inválido — use 0 a 3."),
        };
    }

    private static void Exigir(bool condicao, string mensagem, int numeroItem, string codigoItem)
    {
        if (!condicao)
            throw new ErroNaoRecuperavelException($"Item {numeroItem} ('{codigoItem}'): {mensagem}");
    }

    private static IcmsStDto ExigirSt(IcmsStDto? st, int numeroItem, string codigoItem, string codigo) =>
        st is not null && st.ModBcSt is not null && st.BaseCalculoSt is not null &&
        st.AliquotaSt is not null && st.ValorSt is not null
            ? st
            : throw new ErroNaoRecuperavelException(
                $"Item {numeroItem} ('{codigoItem}'): {codigo} exige grupo 'st' com modBcSt, baseCalculoSt, aliquotaSt e valorSt.");

    private static void PreencherSt(ICMS10 grupo, IcmsStDto st) =>
        PreencherStComum(v => grupo.VBCST = v, p => grupo.PICMSST = p, v => grupo.VICMSST = v, st,
            mbc => grupo.ModBCST = mbc,
            mva => grupo.PMVAST = mva, red => grupo.PRedBCST = red,
            (pFcp, vBcFcp, vFcp) => { grupo.PFCPST = pFcp; grupo.VBCFCPST = vBcFcp; grupo.VFCPST = vFcp; });

    private static void PreencherSt(ICMS70 grupo, IcmsStDto st) =>
        PreencherStComum(v => grupo.VBCST = v, p => grupo.PICMSST = p, v => grupo.VICMSST = v, st,
            mbc => grupo.ModBCST = mbc,
            mva => grupo.PMVAST = mva, red => grupo.PRedBCST = red,
            (pFcp, vBcFcp, vFcp) => { grupo.PFCPST = pFcp; grupo.VBCFCPST = vBcFcp; grupo.VFCPST = vFcp; });

    private static void PreencherSt(ICMS90 grupo, IcmsStDto st)
    {
        if (st.ModBcSt is null && st.BaseCalculoSt is null && st.AliquotaSt is null && st.ValorSt is null)
            return; // CST 90 só declara ST quando fizer sentido — sem ST própria não há o que mapear.
        PreencherStComum(v => grupo.VBCST = v, p => grupo.PICMSST = p, v => grupo.VICMSST = v, st,
            mbc => grupo.ModBCST = mbc,
            mva => grupo.PMVAST = mva, red => grupo.PRedBCST = red,
            (pFcp, vBcFcp, vFcp) => { grupo.PFCPST = pFcp; grupo.VBCFCPST = vBcFcp; grupo.VFCPST = vFcp; });
    }

    private static void PreencherSt(ICMSSN201 grupo, IcmsStDto st) =>
        PreencherStComum(v => grupo.VBCST = v, p => grupo.PICMSST = p, v => grupo.VICMSST = v, st,
            mbc => grupo.ModBCST = mbc,
            mva => grupo.PMVAST = mva, red => grupo.PRedBCST = red,
            (pFcp, vBcFcp, vFcp) => { grupo.PFCPST = pFcp; grupo.VBCFCPST = vBcFcp; grupo.VFCPST = vFcp; });

    private static void PreencherSt(ICMSSN202 grupo, IcmsStDto st) =>
        PreencherStComum(v => grupo.VBCST = v, p => grupo.PICMSST = p, v => grupo.VICMSST = v, st,
            mbc => grupo.ModBCST = mbc,
            mva => grupo.PMVAST = mva, red => grupo.PRedBCST = red,
            (pFcp, vBcFcp, vFcp) => { grupo.PFCPST = pFcp; grupo.VBCFCPST = vBcFcp; grupo.VFCPST = vFcp; });

    private static void PreencherSt(ICMSSN900 grupo, IcmsStDto st)
    {
        if (st.ModBcSt is null && st.BaseCalculoSt is null && st.AliquotaSt is null && st.ValorSt is null)
            return; // CSOSN 900: ST é opcional — sem ST própria não há o que mapear.
        PreencherStComum(v => grupo.VBCST = v, p => grupo.PICMSST = p, v => grupo.VICMSST = v, st,
            mbc => grupo.ModBCST = mbc,
            mva => grupo.PMVAST = mva, red => grupo.PRedBCST = red,
            (pFcp, vBcFcp, vFcp) => { grupo.PFCPST = pFcp; grupo.VBCFCPST = vBcFcp; grupo.VFCPST = vFcp; });
    }

    private static void PreencherStComum(
        Action<double> setVbcSt, Action<double> setPicmsSt, Action<double> setVicmsSt,
        IcmsStDto st,
        Action<ModalidadeBaseCalculoICMSST> setModBcSt,
        Action<double?> setPmvaSt, Action<double?> setPRedBcSt,
        Action<double, double, double> setFcpSt)
    {
        setModBcSt(MapearModBcSt(st.ModBcSt));
        if (st.PercentualMva is not null) setPmvaSt((double)st.PercentualMva.Value);
        if (st.PercentualReducaoBcSt is not null) setPRedBcSt((double)st.PercentualReducaoBcSt.Value);
        setVbcSt((double)st.BaseCalculoSt!.Value);
        setPicmsSt((double)st.AliquotaSt!.Value);
        setVicmsSt((double)st.ValorSt!.Value);
        if (st.FcpPercentualSt is not null)
            setFcpSt((double)st.FcpPercentualSt.Value, (double)st.BaseCalculoSt.Value,
                (double)(st.ValorFcpSt ?? 0));
    }

    private static ModalidadeBaseCalculoICMSST MapearModBcSt(string? modBcSt)
    {
        if (string.IsNullOrWhiteSpace(modBcSt) || !int.TryParse(modBcSt.Trim(), out var valor) || valor is < 0 or > 6)
            throw new ErroNaoRecuperavelException(
                $"modBcSt '{modBcSt}' inválido — use 0 a 6 (modalidade da base de cálculo da ST).");
        return (ModalidadeBaseCalculoICMSST)valor;
    }

    private static void PreencherStRetido(ICMS60 grupo, IcmsStDto st)
    {
        if (st.BaseCalculoStRetido is not null) grupo.VBCSTRet = (double)st.BaseCalculoStRetido.Value;
        if (st.AliquotaStRetida is not null) grupo.PST = (double)st.AliquotaStRetida.Value;
        if (st.ValorIcmsSubstituto is not null) grupo.VICMSSubstituto = (double)st.ValorIcmsSubstituto.Value;
        if (st.ValorStRetido is not null) grupo.VICMSSTRet = (double)st.ValorStRetido.Value;
        if (st.FcpPercentualStRetido is not null)
        {
            grupo.PFCPSTRet = (double)st.FcpPercentualStRetido.Value;
            grupo.VBCFCPSTRet = (double)(st.BaseCalculoStRetido ?? 0);
        }
        if (st.ValorFcpStRetido is not null) grupo.VFCPSTRet = (double)st.ValorFcpStRetido.Value;
    }

    private static void PreencherStRetido(ICMSSN500 grupo, IcmsStDto st)
    {
        if (st.BaseCalculoStRetido is not null) grupo.VBCSTRet = (double)st.BaseCalculoStRetido.Value;
        if (st.AliquotaStRetida is not null) grupo.PST = (double)st.AliquotaStRetida.Value;
        if (st.ValorIcmsSubstituto is not null) grupo.VICMSSubstituto = (double)st.ValorIcmsSubstituto.Value;
        if (st.ValorStRetido is not null) grupo.VICMSSTRet = (double)st.ValorStRetido.Value;
        if (st.FcpPercentualStRetido is not null)
        {
            grupo.PFCPSTRet = (double)st.FcpPercentualStRetido.Value;
            grupo.VBCFCPSTRet = (double)(st.BaseCalculoStRetido ?? 0);
        }
        if (st.ValorFcpStRetido is not null) grupo.VFCPSTRet = (double)st.ValorFcpStRetido.Value;
    }

    private static void PreencherFcp(ICMS00 grupo, IcmsDto icms)
    {
        // ICMS00 na Unimake não expõe vBCFCP — só pFCP/vFCP.
        if (icms.FcpPercentual is not null) grupo.PFCP = (double)icms.FcpPercentual.Value;
        if (icms.ValorFcp is not null) grupo.VFCP = (double)icms.ValorFcp.Value;
    }

    private static void PreencherFcp(ICMS10 grupo, IcmsDto icms)
    {
        if (icms.FcpPercentual is not null)
        {
            grupo.PFCP = (double)icms.FcpPercentual.Value;
            grupo.VBCFCP = (double)(icms.BaseCalculo ?? 0);
        }
        if (icms.ValorFcp is not null) grupo.VFCP = (double)icms.ValorFcp.Value;
    }

    private static void PreencherFcp(ICMS20 grupo, IcmsDto icms)
    {
        if (icms.FcpPercentual is not null)
        {
            grupo.PFCP = (double)icms.FcpPercentual.Value;
            grupo.VBCFCP = (double)(icms.BaseCalculo ?? 0);
        }
        if (icms.ValorFcp is not null) grupo.VFCP = (double)icms.ValorFcp.Value;
    }

    private static void PreencherFcp(ICMS51 grupo, IcmsDto icms)
    {
        if (icms.FcpPercentual is not null)
        {
            grupo.PFCP = (double)icms.FcpPercentual.Value;
            grupo.VBCFCP = (double)(icms.BaseCalculo ?? 0);
        }
        if (icms.ValorFcp is not null) grupo.VFCP = (double)icms.ValorFcp.Value;
    }

    private static void PreencherFcp(ICMS70 grupo, IcmsDto icms)
    {
        if (icms.FcpPercentual is not null)
        {
            grupo.PFCP = (double)icms.FcpPercentual.Value;
            grupo.VBCFCP = (double)(icms.BaseCalculo ?? 0);
        }
        if (icms.ValorFcp is not null) grupo.VFCP = (double)icms.ValorFcp.Value;
    }

    private static void PreencherFcp(ICMS90 grupo, IcmsDto icms)
    {
        if (icms.FcpPercentual is not null)
        {
            grupo.PFCP = (double)icms.FcpPercentual.Value;
            grupo.VBCFCP = (double)(icms.BaseCalculo ?? 0);
        }
        if (icms.ValorFcp is not null) grupo.VFCP = (double)icms.ValorFcp.Value;
    }

    private static ICMSUFDest? MapearDifal(DifalDto? difal, int numeroItem, string codigoItem)
    {
        if (difal is null)
            return null;

        if (difal.AliquotaInterestadual is not (4 or 7 or 12))
            throw new ErroNaoRecuperavelException(
                $"Item {numeroItem} ('{codigoItem}'): DIFAL exige aliquotaInterestadual = 4, 7 ou 12 (pICMSInter).");
        Exigir(difal.BaseDestino is not null && difal.ValorIcmsDestino is not null && difal.ValorIcmsOrigem is not null,
            "DIFAL exige baseDestino, valorIcmsDestino e valorIcmsOrigem.", numeroItem, codigoItem);

        var destino = new ICMSUFDest
        {
            VBCUFDest = (double)difal.BaseDestino!.Value,
            PICMSInter = (double)difal.AliquotaInterestadual!.Value,
            PICMSInterPart = 100.00, // partilha 100% destino — Convênio 190/2017 (DT ICMS 88/2017 revogada)
            VICMSUFDest = (double)difal.ValorIcmsDestino!.Value,
            VICMSUFRemet = (double)difal.ValorIcmsOrigem!.Value,
        };
        if (difal.AliquotaDestino is not null) destino.PICMSUFDest = (double)difal.AliquotaDestino.Value;
        if (difal.FcpPercentualDestino is not null)
        {
            destino.PFCPUFDest = (double)difal.FcpPercentualDestino.Value;
            destino.VBCFCPUFDest = (double)difal.BaseDestino.Value;
        }
        if (difal.ValorFcpDestino is not null) destino.VFCPUFDest = (double)difal.ValorFcpDestino.Value;
        return destino;
    }

    private static Total MapearTotal(EmissaoRequest req)
    {
        decimal vBc = 0, vIcms = 0, vBcSt = 0, vSt = 0, vFcp = 0, vFcpSt = 0, vFcpStRet = 0,
                vFcpUfDest = 0, vIcmsUfDest = 0, vIcmsUfRemet = 0;
        var vDesc = req.Totais.ValorDesconto ?? 0;
        var vFrete = req.Totais.ValorFrete ?? 0;
        var vSeg = req.Totais.ValorSeguro ?? 0;
        var vOutro = req.Totais.OutrasDespesas ?? 0;
        decimal vIpi = 0, vPis = 0, vCofins = 0;
        decimal vBcIbsCbs = 0, vIbsUf = 0, vIbsMun = 0, vCbsTotal = 0, vIsTotal = 0;

        foreach (var item in req.Itens)
        {
            if (item.ValorDesconto is { } descontoItem)
                vDesc += descontoItem;
            if (item.ImpostosV2?.Ipi is { } ipi)
                vIpi += ipi.Valor ?? 0;
            if (item.ImpostosV2?.Pis is { } pis)
                vPis += pis.Valor ?? 0;
            if (item.ImpostosV2?.Cofins is { } cofins)
                vCofins += cofins.Valor ?? 0;
            if (item.ImpostosV2?.IbsCbs is { } ibsCbs)
            {
                vBcIbsCbs += ibsCbs.BaseCalculo ?? 0;
                vIbsUf += ibsCbs.ValorIbsEstadual ?? 0;
                vIbsMun += ibsCbs.ValorIbsMunicipal ?? 0;
                vCbsTotal += ibsCbs.ValorCbs ?? 0;
            }
            if (item.ImpostosV2?.Is is { } isItem)
                vIsTotal += isItem.Valor ?? 0;

            if (item.ImpostosV2?.Icms is { } icms)
            {
                vBc += icms.BaseCalculo ?? 0;
                vIcms += icms.Valor ?? 0;
                vFcp += icms.ValorFcp ?? 0;
                if (icms.St is { } st)
                {
                    vBcSt += st.BaseCalculoSt ?? 0;
                    vSt += st.ValorSt ?? 0;
                    vFcpSt += st.ValorFcpSt ?? 0;
                    vFcpStRet += st.ValorFcpStRetido ?? 0;
                }
                if (icms.Difal is { } difal)
                {
                    vFcpUfDest += difal.ValorFcpDestino ?? 0;
                    vIcmsUfDest += difal.ValorIcmsDestino ?? 0;
                    vIcmsUfRemet += difal.ValorIcmsOrigem ?? 0;
                }
            }
            else
            {
                foreach (var im in item.Impostos ?? [])
                {
                    vBc += im.BaseCalculo ?? 0;
                    vIcms += im.Valor ?? 0;
                }
            }
        }

        var tot = new ICMSTot
        {
            VBC = (double)vBc,
            VICMS = (double)vIcms,
            VProd = (double)req.Totais.ValorProdutos,
            VNF = (double)req.Totais.ValorNota,
        };
        if (vBcSt != 0) tot.VBCST = (double)vBcSt;
        if (vSt != 0) tot.VST = (double)vSt;
        if (vFcp != 0) tot.VFCP = (double)vFcp;
        if (vFcpSt != 0) tot.VFCPST = (double)vFcpSt;
        if (vFcpStRet != 0) tot.VFCPSTRet = (double)vFcpStRet;
        if (vFcpUfDest != 0) tot.VFCPUFDest = (double)vFcpUfDest;
        if (vIcmsUfDest != 0) tot.VICMSUFDest = (double)vIcmsUfDest;
        if (vIcmsUfRemet != 0) tot.VICMSUFRemet = (double)vIcmsUfRemet;
        if (vDesc != 0) tot.VDesc = (double)vDesc;
        if (vFrete != 0) tot.VFrete = (double)vFrete;
        if (vSeg != 0) tot.VSeg = (double)vSeg;
        if (vOutro != 0) tot.VOutro = (double)vOutro;
        if (vIpi != 0) tot.VIPI = (double)vIpi;
        if (vPis != 0) tot.VPIS = (double)vPis;
        if (vCofins != 0) tot.VCOFINS = (double)vCofins;

        var total = new Total { ICMSTot = tot };
        if (vBcIbsCbs != 0 || vIbsUf + vIbsMun != 0 || vCbsTotal != 0)
        {
            total.IBSCBSTot = new IBSCBSTot
            {
                VBCIBSCBS = (double)vBcIbsCbs,
                GIBS = new GIBSTot
                {
                    GIBSUF = new GIBSUFTot { VIBSUF = (double)vIbsUf },
                    GIBSMun = new GIBSMunTot { VIBSMun = (double)vIbsMun },
                    VIBS = (double)(vIbsUf + vIbsMun),
                },
                GCBS = new GCBSTot { VCBS = (double)vCbsTotal },
            };
        }
        if (vIsTotal != 0) total.ISTot = new ISTot { VIS = (double)vIsTotal };

        return total;
    }

    private static Pag MapearPag(EmissaoRequest req, bool nfce)
    {
        var formas = req.Pagamento ?? [];
        if (formas.Count == 0)
        {
            if (nfce)
                throw new ErroNaoRecuperavelException("NFC-e exige ao menos uma forma de pagamento.");
            // NF-e sem pagamento financeiro: tpPag 90 ("sem pagamento").
            formas = [new PagamentoDto("90", 0)];
        }

        return new Pag
        {
            DetPag = formas.Select(p =>
            {
                if (!int.TryParse(p.Forma, out var forma))
                    throw new ErroNaoRecuperavelException($"Forma de pagamento inválida (use o código numérico da SEFAZ): '{p.Forma}'.");
                return new DetPag { TPag = (MeioPagamento)forma, VPag = (double)p.Valor };
            }).ToList(),
        };
    }

    /// <summary>cNF (8 dígitos) determinístico a partir do id do documento — a
    /// unicidade real da chave vem de série+número, que são únicos por tenant.</summary>
    private static string CNFDe(Guid id)
    {
        var bytes = id.ToByteArray();
        var valor = BitConverter.ToUInt64(bytes, 0) % 99_999_999u + 1;
        return valor.ToString("D8");
    }

    private static string LoteDe(Guid id) => Math.Abs(id.GetHashCode()).ToString();
}
