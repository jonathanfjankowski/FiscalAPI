using System.Text.Json;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Unimake.Business.DFe.Servicos;
using NACIONAL = Unimake.Business.DFe.Xml.NFSe.NACIONAL;

namespace Fiscal.Adapters.Unimake;

/// <summary>
/// Monta o DPS (padrão Nacional, layout 1.01) a partir do DocumentoFiscal +
/// do NfseDpsRequest serializado em doc.PayloadEntrada + do perfil do tenant
/// (prestador). Chave do DPS (Id): "DPS" + cLocEmi(7) + tpInsc(1) + CNPJ(14)
/// + série(5) + nDPS(15) = 45 caracteres — a NFS-e (chave de 50, prefixo
/// "NFS") é devolvida pela SEFAZ na autorização síncrona.
///
/// Substituição: quando o payload vem de
/// POST /v1/documentos-fiscais/{id}/substituicao, o grupo &lt;subst&gt; aponta
/// a NFS-e substituída — a SEFAZ desativa a original quando autoriza a
/// substituta (não há evento separado a registrar).
/// </summary>
public static class MapperDps
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public sealed record InfoSubstituicao(int CMotivo, string? XMotivo, string ChaveSubstituida);

    public static NfseDpsRequest LerRequest(string payloadEntrada)
    {
        var json = JsonDocument.Parse(payloadEntrada).RootElement.Clone();
        if (json.TryGetProperty("cMotivo", out _))
        {
            var subst = json.Deserialize<NfseDpsSubstituicaoRequest>(JsonOpts)
                ?? throw new ErroNaoRecuperavelException("PayloadEntrada não pôde ser lido como NfseDpsSubstituicaoRequest.");
            return subst.Dps;
        }
        return json.Deserialize<NfseDpsRequest>(JsonOpts)
            ?? throw new ErroNaoRecuperavelException(
                "PayloadEntrada não pôde ser lido como NfseDpsRequest. Emissões pela rota legada " +
                "(/nfse com EmissaoRequest) só funcionam em sandbox — use POST /v1/documentos-fiscais/nfse/dps.");
    }

    public static InfoSubstituicao? LerSubstituicao(string payloadEntrada)
    {
        var json = JsonDocument.Parse(payloadEntrada).RootElement.Clone();
        if (!json.TryGetProperty("cMotivo", out var cMotivo))
            return null;

        var chave = json.TryGetProperty("chaveSubstituida", out var chaveEl)
            ? chaveEl.GetString()
            : null;
        return new InfoSubstituicao(
            cMotivo.GetInt32(),
            json.TryGetProperty("xMotivo", out var xMotivo) ? xMotivo.GetString() : null,
            chave ?? throw new ErroNaoRecuperavelException("Substituição sem chave da NFS-e substituída."));
    }

    public static NACIONAL.DPS Criar(
        DocumentoFiscal doc,
        Tenant tenant,
        NfseDpsRequest req,
        Ambiente ambiente,
        InfoSubstituicao? substituicao = null)
    {
        if (doc.Serie is null || doc.Numero is null)
            throw new ErroNaoRecuperavelException("Documento sem série/número reservados.");

        if (string.IsNullOrWhiteSpace(tenant.Cnpj) || tenant.Cnpj.Length != 14)
            throw new ErroNaoRecuperavelException("CNPJ do tenant ausente ou inválido (esperado: 14 dígitos).");

        int cLocEmi;
        if (req.CodigoMunicipioEmissor is { } emissor)
        {
            cLocEmi = emissor;
        }
        else if (int.TryParse(tenant.CodigoMunicipioIbge, out var munTenant) && munTenant > 0)
        {
            cLocEmi = munTenant;
        }
        else
        {
            throw new ErroNaoRecuperavelException(
                "cLocEmi ausente: informe codigoMunicipioEmissor ou cadastre codigo_municipio_ibge do tenant.");
        }

        if (req.Tomador is null)
            throw new ErroNaoRecuperavelException("NFS-e exige 'tomador' no payload.");

        var nDps = doc.Numero!.Value;
        if (nDps > 999_999_999_999_999)
            throw new ErroNaoRecuperavelException($"nDPS fora do layout (15 dígitos): {nDps}.");

        var infDps = new NACIONAL.InfDPS
        {
            // tpInsc "1" = CNPJ (layout 1.01: "DPS" + cLocEmi(7) + tpInsc(1) +
            // inscricao(14) + serie(5) + nDPS(15) = 45 posições).
            Id = $"DPS{cLocEmi:D7}1{tenant.Cnpj}{doc.Serie!.Value:D5}{nDps:D15}",
            TpAmb = ambiente == Ambiente.Producao ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            // DhEmi = aceitação da request: mantém o Id determinístico entre
            // tentativas (retry reenvia o MESMO DPS).
            DhEmi = doc.CriadoEm,
            VerAplic = $"FiscalAPI {typeof(MapperDps).Assembly.GetName().Version?.ToString(3) ?? "1.4.0"}",
            Serie = doc.Serie.Value.ToString("D5"),
            // TSNumDPS: padrão [1-9][0-9]{0,14} — sem zeros à esquerda.
            NDPS = nDps.ToString(),
            DCompet = ParseDataCompetencia(req.DataCompetencia, doc),
            TpEmit = (TipoEmitenteNFSe)(req.TipoEmissor ?? 1),
            CLocEmi = cLocEmi,
            Prest = MapearPrest(tenant),
            Toma = MapearToma(req.Tomador),
            Serv = MapearServ(req, cLocEmi),
            Valores = MapearValores(req),
        };

        if (req.IbsCbs is not null)
            infDps.IBSCBS = MapearIbsCbs(req.IbsCbs);

        if (substituicao is not null)
        {
            if (substituicao.CMotivo is not (1 or 2 or 3 or 4 or 5 or 99))
                throw new ErroNaoRecuperavelException(
                    $"cMotivo {substituicao.CMotivo} inválido — use 1, 2, 3, 4, 5 ou 99 (tabela da SEFAZ Nacional).");
            if (substituicao.CMotivo is 99 && string.IsNullOrWhiteSpace(substituicao.XMotivo))
                throw new ErroNaoRecuperavelException("Substituição com cMotivo 99 (outros) exige xMotivo.");
            infDps.Subst = new NACIONAL.Subst
            {
                chSubstda = substituicao.ChaveSubstituida,
                CMotivo = (CMotivoSubs)substituicao.CMotivo,
                XMotivo = substituicao.XMotivo,
            };
        }

        return new NACIONAL.DPS
        {
            Versao = "1.01",
            InfDPS = infDps,
        };
    }

    private static DateTimeOffset ParseDataCompetencia(string? dataCompetencia, DocumentoFiscal doc) =>
        DateTimeOffset.TryParseExact(
            string.IsNullOrWhiteSpace(dataCompetencia) ? null : dataCompetencia,
            "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var compet)
                ? compet
                : new DateTimeOffset(doc.CriadoEm.Year, doc.CriadoEm.Month, doc.CriadoEm.Day, 0, 0, 0, TimeSpan.Zero);

    private static NACIONAL.Prest MapearPrest(Tenant tenant) => new()
    {
        CNPJ = tenant.Cnpj,
        XNome = tenant.RazaoSocial,
        IM = tenant.InscricaoMunicipal,
        End = MapearEndereco(
            logradouro: tenant.Logradouro,
            numero: tenant.Numero,
            complemento: tenant.Complemento,
            bairro: tenant.Bairro,
            codigoMunicipioIbge: tenant.CodigoMunicipioIbge,
            cep: tenant.Cep),
        RegTrib = new NACIONAL.RegTrib
        {
            OpSimpNac = tenant.RegimeTributario switch
            {
                1 or 2 => OptSimplesNacional.ME_EPP,
                _ => OptSimplesNacional.NaoOptante,
            },
        },
    };

    private static NACIONAL.Toma MapearToma(NfseTomaDto toma)
    {
        var documento = new string(toma.CnpjCpf.Where(char.IsDigit).ToArray());
        var result = new NACIONAL.Toma
        {
            XNome = toma.Nome,
            IM = toma.InscricaoMunicipal,
            Fone = toma.Telefone,
            Email = toma.Email,
        };
        if (documento.Length == 11) result.CPF = documento;
        else if (documento.Length == 14) result.CNPJ = documento;
        else throw new ErroNaoRecuperavelException(
            $"CnpjCpf do tomador inválido (esperado 11 ou 14 dígitos): '{toma.CnpjCpf}'.");

        if (toma.Endereco is not null)
        {
            result.End = MapearEndereco(
                logradouro: toma.Endereco.Logradouro,
                numero: toma.Endereco.Numero,
                complemento: toma.Endereco.Complemento,
                bairro: toma.Endereco.Bairro,
                codigoMunicipioIbge: toma.Endereco.CodigoMunicipioIbge,
                cep: toma.Endereco.Cep);
        }

        return result;
    }

    private static NACIONAL.End MapearEndereco(
        string? logradouro, string? numero, string? complemento, string? bairro,
        string? codigoMunicipioIbge, string? cep)
    {
        if (string.IsNullOrWhiteSpace(logradouro) || string.IsNullOrWhiteSpace(numero) ||
            string.IsNullOrWhiteSpace(bairro) || string.IsNullOrWhiteSpace(codigoMunicipioIbge) ||
            !int.TryParse(codigoMunicipioIbge, out var cMun))
        {
            throw new ErroNaoRecuperavelException(
                "Endereço incompleto: logradouro, numero, bairro e codigoMunicipioIbge são obrigatórios quando informado.");
        }

        return new NACIONAL.End
        {
            EndNac = new NACIONAL.EndNac { CMun = cMun, CEP = cep },
            XLgr = logradouro,
            Nro = numero,
            XCpl = complemento,
            XBairro = bairro,
        };
    }

    private static NACIONAL.Serv MapearServ(NfseDpsRequest req, int cLocEmi)
    {
        var serv = req.Servico;
        if (string.IsNullOrWhiteSpace(serv.CodigoTributarioNacional))
            throw new ErroNaoRecuperavelException("servico.codigoTributarioNacional (cTribNac) é obrigatório.");
        if (string.IsNullOrWhiteSpace(serv.DescricaoServico))
            throw new ErroNaoRecuperavelException("servico.descricaoServico (xDescServ) é obrigatório.");

        return new NACIONAL.Serv
        {
            LocPrest = new NACIONAL.LocPrest
            {
                CLocPrestacao = serv.CodigoMunicipioPrestacao ?? cLocEmi,
            },
            CServ = new NACIONAL.CServ
            {
                CTribNac = serv.CodigoTributarioNacional,
                CTribMun = serv.CodigoTributarioMunicipal,
                XDescServ = serv.DescricaoServico,
                CNBS = serv.CodigoNbs,
            },
            InfoCompl = string.IsNullOrWhiteSpace(req.InformacoesComplementares)
                ? null
                : new NACIONAL.InfoCompl { XInfComp = req.InformacoesComplementares },
        };
    }

    private static NACIONAL.Valores MapearValores(NfseDpsRequest req)
    {
        var v = req.Valores;
        var tribMun = new NACIONAL.TribMun
        {
            TribISSQN = (TribISSQN)v.TributacaoIssqn,
            TpRetISSQN = (TipoRetencaoISSQN)v.RetencaoIssqn,
        };
        if (v.AliquotaIssqn is { } aliquota) tribMun.PAliq = (double)aliquota;

        var valores = new NACIONAL.Valores
        {
            VServPrest = new NACIONAL.VServPrest
            {
                VServ = (double)v.ValorServicos,
            },
            Trib = new NACIONAL.Trib
            {
                TribMun = tribMun,
                // totTrib é obrigatório no layout 1.01; indTotTrib 0 = não
                // totaliza (o contrato ainda não expõe totais de tributos).
                TotTrib = new NACIONAL.TotTrib
                {
                    IndTotTrib = 0,
                },
            },
        };
        if (v.ValorRecebido is { } recebido) valores.VServPrest.VReceb = (double)recebido;

        if (v.DescontoIncondicionado is { } desconto)
            valores.vDescCondIncond = new NACIONAL.VDescCondIncond { VDescIncond = (double)desconto };

        if (v.TributacaoFederal is { } fed)
        {
            var tribFed = new NACIONAL.TribFed();
            if (fed.CstPisCofins is not null)
            {
                var pisCofins = new NACIONAL.PISCOFINS { CST = fed.CstPisCofins };
                if (fed.BaseCalculoPisCofins is { } bc) pisCofins.VBCPisCofins = (double)bc;
                if (fed.AliquotaPis is { } pPis) pisCofins.PAliqPis = (double)pPis;
                if (fed.AliquotaCofins is { } pCofins) pisCofins.PAliqCofins = (double)pCofins;
                if (fed.ValorPis is { } vPis) pisCofins.VPis = (double)vPis;
                if (fed.ValorCofins is { } vCofins) pisCofins.VCofins = (double)vCofins;
                if (fed.TipoRetencaoPisCofins is { } tp) pisCofins.TpRetPisCofins = (TipoRetPisCofins)tp;
                tribFed.PISCOFINS = pisCofins;
            }
            if (fed.ValorRetidoCpp is { } retCp) tribFed.VRetCP = (double)retCp;
            if (fed.ValorRetidoIrrf is { } retIrrf) tribFed.VRetIRRF = (double)retIrrf;
            if (fed.ValorRetidoCsll is { } retCsll) tribFed.VRetCSLL = (double)retCsll;
            valores.Trib.TribFed = tribFed;
        }

        if (v.TotalTributos is { } tot && (tot.Federal is not null || tot.Estadual is not null || tot.Municipal is not null))
        {
            valores.Trib.TotTrib = new NACIONAL.TotTrib
            {
                VTotTrib = new NACIONAL.VTotTrib
                {
                    VTotTribFed = (double)(tot.Federal ?? 0),
                    VTotTribEst = (double)(tot.Estadual ?? 0),
                    VTotTribMun = (double)(tot.Municipal ?? 0),
                },
            };
        }

        return valores;
    }

    private static NACIONAL.IBSCBS MapearIbsCbs(NfseIbsCbsDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.CodigoIndicadorOperacao))
            throw new ErroNaoRecuperavelException("ibscbs.codigoIndicadorOperacao (cIndOp) é obrigatório.");

        return new NACIONAL.IBSCBS
        {
            FinNFSe = (FinalidadeNFSeRTC)dto.Finalidade,
            IndFinal = dto.IndicadorFinal is { } indFinal ? (IndicadorFinalNFSeRTC)indFinal : IndicadorFinalNFSeRTC.Sim,
            CIndOp = dto.CodigoIndicadorOperacao,
            TpOper = dto.TipoOperacaoGov is { } tpOper ? (TpOperacaoGov)tpOper : null,
            TpEnteGov = dto.TipoEnteGovernamental is { } ente ? (TipoEnteGovernamentalNFSeRTC)ente : null,
            IndDest = dto.IndicadorDestinatario is { } indDest
                ? (IndicadorDestinatarioNFSeRTC)indDest
                : IndicadorDestinatarioNFSeRTC.ProprioTomadorAdquirente,
            Valores = new NACIONAL.IBSCBSValores
            {
                Trib = new NACIONAL.IBSCBSValoresTrib
                {
                    GIBSCBS = new NACIONAL.GIBSCBS
                    {
                        CST = dto.GibbsCbs.Cst,
                        CClassTrib = dto.GibbsCbs.CClassTrib,
                        CCredPres = dto.GibbsCbs.CodigoCreditoPresumido,
                    },
                },
            },
        };
    }
}
