using System.Text.Json;
using Fiscal.Core.Contracts;

namespace Fiscal.Pdf;

/// <summary>
/// Helpers de deserialização, agregação de tributos e formatação compartilhados
/// pelos documentos DANFE, DANFCe e DANFSe.
/// </summary>
public static class PdfFormatters
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    internal static EmissaoRequest? TryDeserialize(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<EmissaoRequest>(payload, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lê o NfseDpsRequest do payload — direto ou embrulhado em
    /// NfseDpsSubstituicaoRequest (detecta cMotivo), mesma regra do
    /// MapperDps.LerRequest. Retorna null se não for um DPS.</summary>
    internal static NfseDpsRequest? TryDeserializeDps(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement.Clone();
            if (root.TryGetProperty("cMotivo", out _))
                return root.Deserialize<NfseDpsSubstituicaoRequest>(JsonOpts)?.Dps;
            return root.Deserialize<NfseDpsRequest>(JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Formata chave de acesso em grupos de 4 dígitos; seguro para chaves ≠ 44 chars.</summary>
    public static string FormatarChave(string? chave) =>
        string.IsNullOrWhiteSpace(chave) ? "-" : string.Join(" ", Chunk(chave.Trim(), 4));

    public static string FormatarCnpj(string cnpj) =>
        cnpj.Length == 14 ? $"{cnpj[..2]}.{cnpj.Substring(2, 3)}.{cnpj.Substring(5, 3)}/{cnpj.Substring(8, 4)}-{cnpj[12..]}" : cnpj;

    public static string FormatarCnpjCpf(string valor) => valor.Length switch
    {
        14 => $"{valor[..2]}.{valor.Substring(2, 3)}.{valor.Substring(5, 3)}/{valor.Substring(8, 4)}-{valor[12..]}",
        11 => $"{valor[..3]}.{valor.Substring(3, 3)}.{valor.Substring(6, 3)}-{valor[9..]}",
        _ => valor,
    };

    public static string FormatarCep(string? cep) =>
        string.IsNullOrEmpty(cep) ? "-" : cep.Length == 8 ? $"{cep[..5]}-{cep[5..]}" : cep;

    internal static IEnumerable<string> Chunk(string valor, int tamanho)
    {
        for (var i = 0; i < valor.Length; i += tamanho)
            yield return valor.Substring(i, Math.Min(tamanho, valor.Length - i));
    }

    public static decimal SomaBaseIcms(EmissaoRequest? req)
    {
        if (req is null)
            return 0;

        decimal total = 0;
        foreach (var item in req.Itens)
        {
            if (item.ImpostosV2?.Icms is { } icms)
                total += icms.BaseCalculo ?? 0;
            else
                total += item.Impostos?.Sum(im => im.BaseCalculo ?? 0) ?? 0;
        }
        return total;
    }

    public static decimal SomaIcms(EmissaoRequest? req)
    {
        if (req is null)
            return 0;

        decimal total = 0;
        foreach (var item in req.Itens)
        {
            if (item.ImpostosV2?.Icms is { } icms)
                total += icms.Valor ?? 0;
            else
                total += item.Impostos?.Sum(im => im.Valor ?? 0) ?? 0;
        }
        return total;
    }

    public static decimal SomaBcSt(EmissaoRequest? req) =>
        req?.Itens.Sum(i => i.ImpostosV2?.Icms?.St?.BaseCalculoSt ?? 0) ?? 0;

    public static decimal SomaSt(EmissaoRequest? req) =>
        req?.Itens.Sum(i => i.ImpostosV2?.Icms?.St?.ValorSt ?? 0) ?? 0;

    public static decimal SomaIpi(EmissaoRequest? req) =>
        req?.Itens.Sum(i => i.ImpostosV2?.Ipi?.Valor ?? 0) ?? 0;

    public static decimal SomaIi(EmissaoRequest? req) =>
        req?.Itens.Sum(i => i.ImpostosV2?.Ii?.ValorIi ?? 0) ?? 0;

    public static decimal SomaPis(EmissaoRequest? req) =>
        req?.Itens.Sum(i => i.ImpostosV2?.Pis?.Valor ?? 0) ?? 0;

    public static decimal SomaCofins(EmissaoRequest? req) =>
        req?.Itens.Sum(i => i.ImpostosV2?.Cofins?.Valor ?? 0) ?? 0;

    public static decimal SomaFcp(EmissaoRequest? req) =>
        req?.Itens.Sum(i => (i.ImpostosV2?.Icms?.ValorFcp ?? 0) + (i.ImpostosV2?.Icms?.St?.ValorFcpSt ?? 0)) ?? 0;

    public static decimal SomaDesonerado(EmissaoRequest? req) =>
        req?.Itens.Sum(i => i.ImpostosV2?.Icms?.ValorDesonerado ?? 0) ?? 0;

    internal static IEnumerable<string> CabecalhoItens()
    {
        yield return "#";
        yield return "CÓDIGO";
        yield return "DESCRIÇÃO";
        yield return "NCM";
        yield return "CST";
        yield return "CFOP";
        yield return "UN";
        yield return "QTD";
        yield return "V. UNIT";
        yield return "V. TOTAL";
        yield return "BC ICMS";
        yield return "V. ICMS";
    }

    internal static IEnumerable<string> ValoresItem(int numero, ItemDto item)
    {
        var icms = item.ImpostosV2?.Icms;
        var cst = icms?.Cst ?? icms?.Csosn ?? item.Impostos?.FirstOrDefault()?.Cst ?? "-";
        yield return numero.ToString();
        yield return item.Codigo;
        yield return item.Descricao;
        yield return item.Ncm ?? "-";
        yield return cst;
        yield return item.Cfop ?? "-";
        yield return item.Unidade ?? "UN";
        yield return item.Quantidade.ToString("N3");
        yield return item.ValorUnitario.ToString("N2");
        yield return item.ValorTotal.ToString("N2");
        yield return (icms?.BaseCalculo ?? item.Impostos?.FirstOrDefault()?.BaseCalculo ?? 0).ToString("N2");
        yield return (icms?.Valor ?? item.Impostos?.FirstOrDefault()?.Valor ?? 0).ToString("N2");
    }

    /// <summary>tPag (tabela SEFAZ) → nome amigável no cupom e DANFE.</summary>
    internal static string NomeFormaPagamento(string codigo) => codigo switch
    {
        "01" => "Dinheiro",
        "02" => "Cheque",
        "03" => "Cartão de crédito",
        "04" => "Cartão de débito",
        "05" => "Crédito loja",
        "10" => "Vale alimentação",
        "11" => "Vale refeição",
        "12" => "Vale presente",
        "13" => "Vale combustível",
        "14" => "Duplicata mercantil",
        "15" => "Boleto bancário",
        "16" => "Depósito bancário",
        "17" => "PIX",
        "18" => "Transferência / carteira digital",
        "19" => "Fidelidade / Cashback",
        "20" => "Pagamento instantâneo (outro)",
        "21" => "Pagamento posterior",
        "22" => "Crédito em loja",
        "90" => "Sem pagamento",
        _ => $"Forma {codigo}",
    };

    internal static IEnumerable<(string Titulo, string Valor)> ValoresNfse(NfseValoresDto? valores)
    {
        yield return ("VALOR DOS SERVIÇOS", (valores?.ValorServicos ?? 0).ToString("N2"));
        yield return ("DESCONTO INCONDICIONADO", (valores?.DescontoIncondicionado ?? 0).ToString("N2"));
        yield return ("VALOR RECEBIDO", valores?.ValorRecebido?.ToString("N2") ?? "-");
        yield return ("ALÍQUOTA ISSQN", valores?.AliquotaIssqn is { } aliq ? $"{aliq:N2}%" : "-");
        yield return ("TRIBUTAÇÃO ISSQN", RotuloTributacaoIssqn(valores?.TributacaoIssqn));
        yield return ("RETENÇÃO ISSQN", RotuloRetencaoIssqn(valores?.RetencaoIssqn));
        var fed = valores?.TributacaoFederal;
        yield return ("PIS / COFINS", fed is null ? "-" : $"{fed.ValorPis ?? 0:N2} / {fed.ValorCofins ?? 0:N2}");
        yield return ("RETENÇÕES FEDERAIS (IRRF / CSLL / CPP)",
            fed is null ? "-" : $"{fed.ValorRetidoIrrf ?? 0:N2} / {fed.ValorRetidoCsll ?? 0:N2} / {fed.ValorRetidoCpp ?? 0:N2}");
        var tot = valores?.TotalTributos;
        yield return ("TOTAL TRIBUTOS (FED / EST / MUN)",
            tot is null ? "-" : $"{tot.Federal ?? 0:N2} / {tot.Estadual ?? 0:N2} / {tot.Municipal ?? 0:N2}");
    }

    private static string RotuloTributacaoIssqn(int? tipo) => tipo switch
    {
        1 => "Tributável no município",
        2 => "Imunidade",
        3 => "Exportação",
        4 => "Não incidência",
        _ => tipo?.ToString() ?? "-",
    };

    private static string RotuloRetencaoIssqn(int? tipo) => tipo switch
    {
        1 => "Não retido",
        2 => "Retido pelo tomador",
        3 => "Retido pelo intermediário",
        _ => tipo?.ToString() ?? "-",
    };
}
