namespace Fiscal.Core.Services;

public record ItemFiscal(
    string Codigo,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal);

public record ImpostoFiscal(
    string Cst,
    decimal? BaseCalculo,
    decimal? Aliquota,
    decimal? Valor);

public record DocumentoParaValidar(
    decimal ValorTotal,
    IReadOnlyList<ItemFiscal> Itens,
    IReadOnlyList<ImpostoFiscal> Impostos);

public record InconsistenciaFiscal(string Campo, string Mensagem);

public class ValidadorConsistenciaFiscal
{
    private const decimal Tolerancia = 0.01m;

    public IReadOnlyList<InconsistenciaFiscal> Validar(DocumentoParaValidar doc)
    {
        var erros = new List<InconsistenciaFiscal>();

        // 1) Soma dos itens = valor total.
        if (doc.Itens.Count > 0)
        {
            var somaItens = doc.Itens.Sum(i => i.ValorTotal);
            if (Math.Abs(somaItens - doc.ValorTotal) > Tolerancia)
            {
                erros.Add(new InconsistenciaFiscal(
                    "valorTotal",
                    $"Soma dos itens ({somaItens:N2}) difere do valor total ({doc.ValorTotal:N2})."));
            }
        }

        // 2) Por item: quantidade * valorUnitario ≈ valorTotal.
        for (int i = 0; i < doc.Itens.Count; i++)
        {
            var item = doc.Itens[i];
            var esperado = item.Quantidade * item.ValorUnitario;
            if (Math.Abs(esperado - item.ValorTotal) > Tolerancia)
            {
                erros.Add(new InconsistenciaFiscal(
                    $"itens[{i}].valorTotal",
                    $"Item {item.Codigo}: {item.Quantidade} × {item.ValorUnitario:N2} = {esperado:N2}, recebido {item.ValorTotal:N2}."));
            }
        }

        // 3) Por imposto: base * aliquota/100 ≈ valor (se ambos informados).
        //    E coerência CST vs presença de valor.
        for (int i = 0; i < doc.Impostos.Count; i++)
        {
            var imp = doc.Impostos[i];
            var prefixo = $"impostos[{i}]";

            if (imp.BaseCalculo is { } b && imp.Aliquota is { } a && imp.Valor is { } v)
            {
                var esperado = b * a / 100m;
                if (Math.Abs(esperado - v) > Tolerancia)
                {
                    erros.Add(new InconsistenciaFiscal(
                        $"{prefixo}.valor",
                        $"Base {b:N2} × alíquota {a:N4}% = {esperado:N2}, recebido {v:N2}."));
                }
            }

            // CST isento/não tributado (40 isenta, 41 não tributada, 50 suspensão,
            // 60 já cobrada por ST) — sem valor próprio esperado.
            // Se CST indica isenção e veio valor > 0, é inconsistência.
            if (EhCstIsento(imp.Cst) && imp.Valor is { } valor && valor > 0)
            {
                erros.Add(new InconsistenciaFiscal(
                    $"{prefixo}.valor",
                    $"CST {imp.Cst} indica isenção mas foi informado valor {valor:N2}."));
            }
        }

        return erros;
    }

    private static bool EhCstIsento(string cst) => cst is "40" or "41" or "50" or "60";
}
