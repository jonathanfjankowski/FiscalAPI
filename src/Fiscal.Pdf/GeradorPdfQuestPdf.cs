using System.Text.Json;
using Fiscal.Core.Contracts;
using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Exceptions;
using Fiscal.Core.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Fiscal.Pdf;

/// <summary>
/// DANFE/DANFCe via QuestPDF (licença Community). Layout simplificado — não é
/// o leiaute oficial de 20 campos do convênio, mas carrega todos os dados
/// essenciais (identificação, emitente/destinatário, itens, totais, pagamento,
/// chave de acesso e protocolo). Em homologação imprime marca d'água.
/// NFS-e entra na Fase 4 (GerarDanfseAsync falha alto até lá).
/// </summary>
public class GeradorPdfQuestPdf : IGeradorPdf
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    static GeradorPdfQuestPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> GerarDanfeAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct) =>
        Task.FromResult(Gerar(documento, tenant, nfce: false));

    public Task<byte[]> GerarDanfceAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct) =>
        Task.FromResult(Gerar(documento, tenant, nfce: true));

    public Task<byte[]> GerarDanfseAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct) =>
        Task.FromResult(Gerar(documento, tenant, nfce: false));

    private static byte[] Gerar(DocumentoFiscal doc, Tenant tenant, bool nfce)
    {
        var req = TryDeserialize(doc.PayloadEntrada);
        var homologacao = doc.Ambiente == (short)Ambiente.Homologacao;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(nfce ? PageSizes.A5 : PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(tenant.RazaoSocial).Bold().FontSize(11);
                            c.Item().Text($"CNPJ: {FormatarCnpj(tenant.Cnpj)}   IE: {tenant.InscricaoEstadual ?? "-"}");
                            c.Item().Text($"{tenant.Logradouro ?? "-"}, {tenant.Numero ?? "-"} — {tenant.Bairro ?? "-"}");
                            c.Item().Text($"{tenant.NomeMunicipio ?? "-"} / {tenant.Uf} — CEP {FormatarCep(tenant.Cep)}");
                        });

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignCenter().Text("DANFE").Bold().FontSize(16);
                            c.Item().AlignCenter().Text(nfce
                                ? "Documento Auxiliar da Nota Fiscal de Consumidor Eletrônica"
                                : "Documento Auxiliar da Nota Fiscal Eletrônica").FontSize(7);
                            c.Item().PaddingTop(4).AlignCenter().Text($"{doc.Modelo}   SÉRIE {doc.Serie}   Nº {doc.Numero}").Bold();
                        });

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(text =>
                            {
                                text.Span("0 - ENTRADA").FontSize(8);
                                text.Span("    ");
                                text.Span("1 - SAÍDA").Bold().FontSize(8);
                            });
                            c.Item().PaddingTop(2).Border(1).Padding(3).Column(k =>
                            {
                                k.Item().Text("CHAVE DE ACESSO").Bold().FontSize(7);
                                k.Item().Text(FormatarChave(doc.ChaveAcesso)).FontSize(8);
                            });
                            c.Item().PaddingTop(2).Text($"PROTOCOLO: {doc.ProtocoloAutorizacao ?? "-"}").FontSize(8);
                        });
                    });

                    col.Item().PaddingTop(6).LineHorizontal(0.5f);
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    if (homologacao)
                    {
                        col.Item().PaddingBottom(6).AlignCenter()
                            .Text("EMISSÃO EM HOMOLOGAÇÃO — SEM VALOR FISCAL")
                            .Bold().FontSize(11).FontColor(Colors.Red.Darken2);
                    }

                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(120);
                            c.RelativeColumn();
                        });

                        t.Cell().Border(0.5f).Padding(3).Text("NATUREZA DA OPERAÇÃO").Bold().FontSize(7);
                        t.Cell().Border(0.5f).Padding(3).Text(req?.NaturezaOperacao ?? "VENDA");
                        t.Cell().Border(0.5f).Padding(3).Text("AMBIENTE / EMISSÃO").Bold().FontSize(7);
                        t.Cell().Border(0.5f).Padding(3).Text(
                            $"{(homologacao ? "HOMOLOGAÇÃO" : "PRODUÇÃO")}   {doc.CriadoEm:dd/MM/yyyy HH:mm}   (FiscalAPI)");
                    });

                    if (req?.Destinatario is { } dest)
                    {
                        col.Item().PaddingTop(6).Text("DESTINATÁRIO / REMETENTE").Bold().FontSize(8);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(150);
                                c.ConstantColumn(140);
                                c.RelativeColumn();
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("NOME / RAZÃO SOCIAL").Bold().FontSize(7);
                                x.Item().Text(dest.Nome);
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("CNPJ / CPF").Bold().FontSize(7);
                                x.Item().Text(dest.CnpjCpf);
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("ENDEREÇO").Bold().FontSize(7);
                                x.Item().Text(dest.Endereco is null ? "-"
                                    : $"{dest.Endereco.Logradouro ?? "-"}, {dest.Endereco.Numero ?? "-"} — " +
                                      $"{dest.Endereco.Bairro ?? "-"} — {dest.Endereco.NomeMunicipio ?? "-"}/{dest.Endereco.Uf ?? "-"}");
                            });
                        });
                    }

                    col.Item().PaddingTop(8).Text("DADOS DOS PRODUTOS / SERVIÇOS").Bold().FontSize(8);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(18);
                            c.ConstantColumn(52);
                            c.RelativeColumn();
                            c.ConstantColumn(58);
                            c.ConstantColumn(30);
                            c.ConstantColumn(42);
                            c.ConstantColumn(20);
                            c.ConstantColumn(52);
                            c.ConstantColumn(52);
                        });

                        foreach (var titulo in CabecalhoItens())
                            t.Cell().Border(0.5f).Background("#EFEFEF").Padding(2).Text(titulo).Bold().FontSize(7);

                        var itens = req?.Itens ?? [];
                        for (var i = 0; i < itens.Count; i++)
                        {
                            var item = itens[i];
                            foreach (var valor in ValoresItem(i + 1, item))
                                t.Cell().Border(0.5f).Padding(2).Text(valor).FontSize(7);
                        }
                    });

                    col.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn();
                                c.RelativeColumn();
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("TOTAL PRODUTOS").Bold().FontSize(7);
                                x.Item().Text((req?.Totais.ValorProdutos ?? 0).ToString("N2"));
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("TOTAL ICMS").Bold().FontSize(7);
                                x.Item().Text(SomaIcms(req).ToString("N2"));
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("TOTAL DA NOTA").Bold().FontSize(8);
                                x.Item().Text((req?.Totais.ValorNota ?? 0).ToString("N2")).Bold();
                            });
                        });
                    });

                    if (req?.Pagamento is { Count: > 0 } pags)
                    {
                        col.Item().PaddingTop(6).Text("PAGAMENTO").Bold().FontSize(8);
                        col.Item().Row(row =>
                        {
                            foreach (var pag in pags)
                                row.RelativeItem().Text($"{pag.Forma}: R$ {pag.Valor:N2}").FontSize(8);
                        });
                    }

                    if (!string.IsNullOrEmpty(doc.MotivoStatus))
                    {
                        col.Item().PaddingTop(8).Text(t =>
                        {
                            t.Span("OBSERVAÇÃO: ").Bold().FontSize(8);
                            t.Span(doc.MotivoStatus).FontSize(8);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span($"FiscalAPI — DANFE gerado a partir do XML autorizado em {DateTimeOffset.UtcNow:dd/MM/yyyy HH:mm}");
                });
            });
        });

        return document.GeneratePdf();
    }

    private static IEnumerable<string> CabecalhoItens()
    {
        yield return "#";
        yield return "CÓDIGO";
        yield return "DESCRIÇÃO";
        yield return "NCM";
        yield return "CFOP";
        yield return "QTD";
        yield return "UN";
        yield return "V. UNIT";
        yield return "V. TOTAL";
    }

    private static IEnumerable<string> ValoresItem(int numero, ItemDto item)
    {
        yield return numero.ToString();
        yield return item.Codigo;
        yield return item.Descricao;
        yield return item.Ncm ?? "-";
        yield return item.Cfop ?? "-";
        yield return item.Quantidade.ToString("N3");
        yield return "UN";
        yield return item.ValorUnitario.ToString("N2");
        yield return item.ValorTotal.ToString("N2");
    }

    private static decimal SomaIcms(EmissaoRequest? req) =>
        req?.Itens.SelectMany(i => i.Impostos ?? [])
            .Where(im => im.Valor is not null).Sum(im => im.Valor!.Value) ?? 0;

    private static EmissaoRequest? TryDeserialize(string payload)
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

    private static string FormatarChave(string? chave) =>
        string.IsNullOrEmpty(chave) ? "-" : string.Join(" ", Enumerable.Range(0, 11).Select(i => chave.Substring(i * 4, 4)));

    private static string FormatarCnpj(string cnpj) =>
        cnpj.Length == 14 ? $"{cnpj[..2]}.{cnpj.Substring(2, 3)}.{cnpj.Substring(5, 3)}/{cnpj.Substring(8, 4)}-{cnpj[12..]}" : cnpj;

    private static string FormatarCep(string? cep) =>
        string.IsNullOrEmpty(cep) ? "-" : cep.Length == 8 ? $"{cep[..5]}-{cep[5..]}" : cep;
}
