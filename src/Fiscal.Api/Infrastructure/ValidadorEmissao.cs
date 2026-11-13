using Fiscal.Core.Contracts;
using Fiscal.Core.Services;

namespace Fiscal.Api.Infrastructure;

/// <summary>
/// Validação do payload de emissão/reenvio, compartilhada entre o endpoint de
/// tenant (<c>POST /v1/documentos-fiscais/{id}/reenviar</c>) e o endpoint do
/// painel (<c>POST /v1/admin/documentos-fiscais/{id}/reenviar</c>).
///
/// Existe para que as duas rotas apliquem exatamente a mesma regra: ambiente
/// válido, devolução exigindo chave referenciada, item sem os dois grupos de
/// imposto, aritmética dos itens e do total. Duplicar isso no controller admin
/// faria o painel aceitar payload que o integrador teria recusado.
/// </summary>
public class ValidadorEmissao
{
    private readonly ValidadorConsistenciaFiscal _validador;
    private readonly ValidadorImpostosV2 _validadorImpostosV2;

    public ValidadorEmissao(
        ValidadorConsistenciaFiscal validador,
        ValidadorImpostosV2 validadorImpostosV2)
    {
        _validador = validador;
        _validadorImpostosV2 = validadorImpostosV2;
    }

    /// <summary>Resultado da validação: ou ok, ou o problema a devolver.</summary>
    public record Resultado(int Status, string Titulo, string? Detalhe = null, string? Campo = null)
    {
        public bool Ok => Status == 0;
        public static readonly Resultado Valido = new(0, "");
    }

    /// <summary>
    /// Valida o payload de emissão. <paramref name="nfce"/> ajusta regras que
    /// diferem por modelo (ex.: grupos v2 permitidos na NFC-e).
    /// </summary>
    public Resultado Validar(EmissaoRequest req, bool nfce)
    {
        if (string.IsNullOrWhiteSpace(req.Ambiente) ||
            (req.Ambiente != "producao" && req.Ambiente != "homologacao"))
        {
            return new Resultado(422, "Ambiente inválido", "Use 'producao' ou 'homologacao'.");
        }

        // v2 F4: devolução exige NF-e referenciada; chaves com 44 dígitos.
        if (req.Finalidade?.Trim().ToLowerInvariant() == "devolucao" &&
            req.NfesReferenciadas is not { Count: > 0 })
        {
            return new Resultado(422, "Devolução exige NF-e referenciada",
                "Informe 'nfesReferenciadas' com a(s) chave(s) de 44 dígitos da(s) NF-e devolvida(s).");
        }

        for (var i = 0; i < (req.NfesReferenciadas?.Count ?? 0); i++)
        {
            var chave = new string((req.NfesReferenciadas![i].ChaveAcesso ?? "").Where(char.IsDigit).ToArray());
            if (chave.Length != 44)
            {
                return new Resultado(422, "Chave de NF-e referenciada inválida",
                    $"nfesReferenciadas[{i}]: esperado 44 dígitos, recebido {chave.Length}.");
            }
        }

        // Grupos de imposto ambíguos: item declara a lista plana legada OU o
        // grupo tipado v2 — nunca os dois (docs/plano-evolucao-contrato-v2.md §8).
        for (var i = 0; i < req.Itens.Count; i++)
        {
            if (req.Itens[i].Impostos is not null && req.Itens[i].ImpostosV2 is not null)
            {
                return new Resultado(400,
                    $"Item {i + 1}: informe apenas 'impostos' (legado) OU 'impostosV2' — nunca os dois.");
            }
        }

        // Aritmética dos itens. Com a fórmula v2 do total
        // (docs/plano-evolucao-contrato-v2.md §5.2), a soma legada
        // (itens = valorNota) não se aplica — confere-se contra os brutos.
        var docValidar = new DocumentoParaValidar(
            ValorTotal: ValidadorImpostosV2.FormulaV2Ativa(req.Totais, req.Itens)
                ? req.Itens.Sum(i => i.ValorTotal)
                : req.Totais.ValorNota,
            Itens: req.Itens.Select(i => new ItemFiscal(
                i.Codigo, i.Quantidade, i.ValorUnitario, i.ValorTotal)).ToList(),
            Impostos: req.Itens
                .SelectMany(i => i.Impostos ?? new List<ImpostoDto>())
                .Select(im => new ImpostoFiscal(im.Cst, im.BaseCalculo, im.Aliquota, im.Valor))
                .ToList());

        var inconsistencias = _validador.Validar(docValidar);
        if (inconsistencias.Count > 0)
        {
            return new Resultado(422, "Inconsistência nos valores do documento",
                inconsistencias[0].Mensagem, inconsistencias[0].Campo);
        }

        var inconsistenciasV2 = _validadorImpostosV2.Validar(req.Itens, nfce: nfce);
        if (inconsistenciasV2.Count > 0)
        {
            return new Resultado(422, "Inconsistência nos grupos de imposto v2",
                inconsistenciasV2[0].Mensagem, inconsistenciasV2[0].Campo);
        }

        var inconsistenciasTotais = _validadorImpostosV2.ValidarTotais(req.Totais, req.Itens);
        if (inconsistenciasTotais.Count > 0)
        {
            return new Resultado(422, "Inconsistência no total da nota",
                inconsistenciasTotais[0].Mensagem, "valorNota");
        }

        return Resultado.Valido;
    }
}