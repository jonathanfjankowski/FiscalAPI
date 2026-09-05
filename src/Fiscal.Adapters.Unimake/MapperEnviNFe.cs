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
/// operação), unidade = "UN", transp sem ocorrência de transporte, CSOSN e
/// IPI/PIS/COFINS não mapeados nesta sprint.
/// </summary>
public static class MapperEnviNFe
{
    private const string VerProc = "FiscalAPI 0.4.0";

    public static EnviNFe Criar(DocumentoFiscal doc, Tenant tenant, EmissaoRequest req, Ambiente ambiente)
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
                Det = MapearDets(req),
                Total = MapearTotal(req),
                Transp = new Transp { ModFrete = ModalidadeFrete.SemOcorrenciaTransporte },
                Pag = MapearPag(req, nfce),
            }
        };

        if (req.Destinatario is not null)
            nfe.InfNFeField.Dest = MapearDest(req.Destinatario, ufEmit);

        return new EnviNFe
        {
            Versao = "4.00",
            IdLote = LoteDe(doc.Id),
            IndSinc = nfce ? SimNao.Sim : SimNao.Nao,
            NFe = new List<NFe> { nfe },
        };
    }

    private static Ide MapearIde(DocumentoFiscal doc, Tenant tenant, EmissaoRequest req, Ambiente ambiente, bool nfce, UFBrasil ufEmit)
    {
        var indPres = IndicadorPresenca.OperacaoPresencial;
        if (!nfce && req.Destinatario?.Endereco?.Uf is { Length: 2 } ufDest
            && !string.Equals(ufDest, tenant.Uf, StringComparison.OrdinalIgnoreCase))
            indPres = IndicadorPresenca.OperacaoInternet;

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
            TpNF = TipoOperacao.Saida,
            IdDest = DestinoOperacao.OperacaoInterna,
            CMunFG = int.Parse(tenant.CodigoMunicipioIbge!),
            TpImp = nfce ? FormatoImpressaoDANFE.NFCe : FormatoImpressaoDANFE.NormalRetrato,
            TpEmis = doc.ModoContingencia switch
            {
                "SVCAN" => TipoEmissao.ContingenciaSVCAN,
                "SVCRS" => TipoEmissao.ContingenciaSVCRS,
                _ => TipoEmissao.Normal,
            },
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            FinNFe = FinalidadeNFe.Normal,
            IndFinal = SimNao.Sim,
            IndPres = indPres,
            ProcEmi = ProcessoEmissao.AplicativoContribuinte,
            VerProc = VerProc,
        };
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

    private static List<Det> MapearDets(EmissaoRequest req)
    {
        var dets = new List<Det>(req.Itens.Count);
        for (var i = 0; i < req.Itens.Count; i++)
        {
            var item = req.Itens[i];
            var imposto = (item.Impostos ?? []).FirstOrDefault();

            dets.Add(new Det
            {
                NItem = i + 1,
                Prod = new Prod
                {
                    CProd = item.Codigo,
                    CEAN = "SEM GTIN",
                    XProd = item.Descricao,
                    NCM = string.IsNullOrWhiteSpace(item.Ncm)
                        ? throw new ErroNaoRecuperavelException($"Item {i + 1} ('{item.Codigo}') sem NCM — obrigatório na NF-e.")
                        : item.Ncm,
                    CFOP = string.IsNullOrWhiteSpace(item.Cfop)
                        ? throw new ErroNaoRecuperavelException($"Item {i + 1} ('{item.Codigo}') sem CFOP — obrigatório na NF-e.")
                        : item.Cfop,
                    UCom = "UN",
                    QCom = item.Quantidade,
                    VUnCom = item.ValorUnitario,
                    VProd = (double)item.ValorTotal,
                    UTrib = "UN",
                    QTrib = item.Quantidade,
                    VUnTrib = item.ValorUnitario,
                    IndTot = SimNao.Sim,
                },
                Imposto = new Imposto
                {
                    ICMS = MapearICMS(imposto, i + 1, item.Codigo),
                },
            });
        }
        return dets;
    }

    private static ICMS MapearICMS(ImpostoDto? imposto, int numeroItem, string codigoItem)
    {
        if (imposto is null)
            throw new ErroNaoRecuperavelException(
                $"Item {numeroItem} ('{codigoItem}') sem grupo de impostos — informe ao menos o CST de ICMS.");

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
            $"Item {numeroItem} ('{codigoItem}'): CST '{cst}' não suportado pelo mapper atual " +
            "(suporta 00, 40, 41 e 50; CST 60, CSOSN do Simples Nacional e grupos 10/20/51/70/90 entram em sprint futura).");
    }

    private static Total MapearTotal(EmissaoRequest req)
    {
        var vBC = (req.Itens.SelectMany(i => i.Impostos ?? [])
            .Where(im => im.BaseCalculo is not null).Sum(im => im.BaseCalculo!.Value));
        var vICMS = (req.Itens.SelectMany(i => i.Impostos ?? [])
            .Where(im => im.Valor is not null).Sum(im => im.Valor!.Value));

        return new Total
        {
            ICMSTot = new ICMSTot
            {
                VBC = (double)vBC,
                VICMS = (double)vICMS,
                VProd = (double)req.Totais.ValorProdutos,
                VNF = (double)req.Totais.ValorNota,
            },
        };
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
