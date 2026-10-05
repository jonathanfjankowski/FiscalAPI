using Fiscal.Core.Contracts;

namespace Fiscal.Core.Services;

/// <summary>
/// Validação declarativa do NfseDpsRequest (padrão Nacional). A crítica fiscal
/// fina é da SEFAZ; aqui garantimos os campos mínimos para montar um DPS
/// coerente — falhas viram 422 com campo exato.
/// </summary>
public class ValidadorNfseDps
{
    public IReadOnlyList<InconsistenciaFiscal> Validar(NfseDpsRequest req)
    {
        var erros = new List<InconsistenciaFiscal>();

        if (req.Ambiente is not ("producao" or "homologacao"))
            erros.Add(new("ambiente", "Use 'producao' ou 'homologacao'."));

        if (req.Serie is < 1)
            erros.Add(new("serie", "Série deve ser maior que zero."));

        if (req.TipoEmissor is not (null or 1 or 2 or 3))
            erros.Add(new("tipoEmissor", "Use 1 (prestador), 2 (tomador) ou 3 (intermediário)."));

        ValidarTomador(req.Tomador, erros);
        ValidarServico(req, erros);
        ValidarValores(req.Valores, erros);
        ValidarIbsCbs(req.IbsCbs, erros);

        if (!string.IsNullOrWhiteSpace(req.DataCompetencia) &&
            !DateTimeOffset.TryParseExact(req.DataCompetencia, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
        {
            erros.Add(new("dataCompetencia", "Use o formato yyyy-MM-dd."));
        }

        return erros;
    }

    private static void ValidarTomador(NfseTomaDto? toma, List<InconsistenciaFiscal> erros)
    {
        if (toma is null)
        {
            erros.Add(new("tomador", "NFS-e exige 'tomador' no payload."));
            return;
        }

        var digitos = new string((toma.CnpjCpf ?? "").Where(char.IsDigit).ToArray());
        if (digitos.Length is not (11 or 14))
            erros.Add(new("tomador.cnpjCpf", $"CnpjCpf do tomador inválido (esperado 11 ou 14 dígitos): '{toma.CnpjCpf}'."));

        if (toma.Endereco is { } end)
        {
            if (string.IsNullOrWhiteSpace(end.Logradouro) || string.IsNullOrWhiteSpace(end.Numero) ||
                string.IsNullOrWhiteSpace(end.Bairro) || string.IsNullOrWhiteSpace(end.CodigoMunicipioIbge))
                erros.Add(new("tomador.endereco",
                    "Endereço do tomador incompleto: logradouro, numero, bairro e codigoMunicipioIbge são obrigatórios quando informado."));
            else if (end.CodigoMunicipioIbge.Length != 7 || !int.TryParse(end.CodigoMunicipioIbge, out _))
                erros.Add(new("tomador.endereco.codigoMunicipioIbge", "Código IBGE do município deve ter 7 dígitos."));
        }
    }

    private static void ValidarServico(NfseDpsRequest req, List<InconsistenciaFiscal> erros)
    {
        var serv = req.Servico;
        if (string.IsNullOrWhiteSpace(serv.CodigoTributarioNacional))
            erros.Add(new("servico.codigoTributarioNacional", "cTribNac é obrigatório."));
        if (string.IsNullOrWhiteSpace(serv.DescricaoServico))
            erros.Add(new("servico.descricaoServico", "xDescServ é obrigatório."));
        if (serv.CodigoNbs is { Length: > 0 and not 9 })
            erros.Add(new("servico.codigoNbs", "cNBS deve ter 9 dígitos."));
    }

    private static void ValidarValores(NfseValoresDto v, List<InconsistenciaFiscal> erros)
    {
        if (v.ValorServicos < 0)
            erros.Add(new("valores.valorServicos", "valorServicos não pode ser negativo."));

        // R-NFS014 — exportação de serviços: resultado da prestação no exterior
        // (cPaisResult, tabela ISO 3166-1 numérica) e ISS não devido.
        if (v.TributacaoIssqn == 3)
        {
            if (string.IsNullOrWhiteSpace(v.CodigoPaisResultado) ||
                !System.Text.RegularExpressions.Regex.IsMatch(v.CodigoPaisResultado, @"^\d{3}$"))
                erros.Add(new("valores.codigoPaisResultado",
                    "Exportação (tribISSQN = 3) exige codigoPaisResultado com 3 dígitos (ISO 3166-1 numérico — ex.: 840 = EUA)."));

            if (v.AliquotaIssqn is > 0)
                erros.Add(new("valores.aliquotaIssqn",
                    "Exportação (tribISSQN = 3) não é tributável pelo ISS — aliquotaIssqn deve ser nula ou zero."));
        }

        if (v.TributacaoFederal is { } fed)
        {
            if (fed.CstPisCofins is not null &&
                ((fed.ValorPis is null) != (fed.AliquotaPis is null) ||
                 (fed.ValorCofins is null) != (fed.AliquotaCofins is null)))
            {
                erros.Add(new("valores.tributacaoFederal",
                    "Com cstPisCofins informado, envie aliquota+valor de PIS e de COFINS (ou nenhum)."));
            }
        }
    }

    private static void ValidarIbsCbs(NfseIbsCbsDto? ibs, List<InconsistenciaFiscal> erros)
    {
        if (ibs is null)
            return;

        if (ibs.TipoOperacaoGov is not null && ibs.TipoEnteGovernamental is null)
            erros.Add(new("ibscbs.tipoEnteGovernamental",
                "tpOper informado exige tpEnteGov (1 União, 2 Estado, 3 DF, 4 Município)."));

        if (string.IsNullOrWhiteSpace(ibs.CodigoIndicadorOperacao) || ibs.CodigoIndicadorOperacao.Length != 6)
            erros.Add(new("ibscbs.codigoIndicadorOperacao", "cIndOp deve ter 6 dígitos (tabela SEPEC)."));

        var g = ibs.GibbsCbs;
        if (g is null)
        {
            erros.Add(new("ibscbs.gibsCbs", "Bloco gIBSCBS é obrigatório quando ibscbs é informado."));
            return;
        }
        if (string.IsNullOrWhiteSpace(g.Cst) || g.Cst.Length != 3)
            erros.Add(new("ibscbs.gibsCbs.cst", "CST do IBS/CBS deve ter 3 dígitos (tabela SEPEC)."));
        if (string.IsNullOrWhiteSpace(g.CClassTrib) || g.CClassTrib.Length != 6)
            erros.Add(new("ibscbs.gibsCbs.cClassTrib", "cClassTrib deve ter 6 dígitos (tabela SEPEC)."));
    }
}
