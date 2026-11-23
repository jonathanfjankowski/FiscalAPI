using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Fiscal.Pdf;

/// <summary>
/// DANFCe em bobina térmica de 80mm (altura contínua): emitente, chave +
/// QR de consulta, consumidor, itens compactos, totais, pagamentos e
/// observações. Homologação imprime "SEM VALOR FISCAL".
/// </summary>
internal static class DanfceDocument
{
    public static byte[] Gerar(DocumentoFiscal doc, Tenant tenant)
    {
        var req = PdfFormatters.TryDeserialize(doc.PayloadEntrada);
        var homologacao = doc.Ambiente == (short)Ambiente.Homologacao;
        var qr = PdfBarcodes.ExtrairQrCode(doc.XmlAssinado);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ContinuousSize(80, Unit.Millimetre);
                page.Margin(4, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(7));

                page.Content().Column(col =>
                {
                    // ---- Emitente ----
                    col.Item().AlignCenter().Text(tenant.RazaoSocial).Bold().FontSize(9);
                    col.Item().AlignCenter().Text($"CNPJ: {PdfFormatters.FormatarCnpj(tenant.Cnpj)}   IE: {tenant.InscricaoEstadual ?? "-"}");
                    col.Item().AlignCenter().Text($"{tenant.Logradouro ?? "-"}, {tenant.Numero ?? "-"} — {tenant.Bairro ?? "-"}");
                    col.Item().AlignCenter().Text($"{tenant.NomeMunicipio ?? "-"}/{tenant.Uf} — CEP {PdfFormatters.FormatarCep(tenant.Cep)}");

                    col.Item().PaddingTop(3).LineHorizontal(0.5f);

                    // ---- Título ----
                    col.Item().PaddingTop(3).AlignCenter().Text("DANFCe").Bold().FontSize(11);
                    col.Item().AlignCenter().Text("Documento Auxiliar da Nota Fiscal de Consumidor Eletrônica").FontSize(6);
                    col.Item().PaddingTop(2).AlignCenter().Text($"Nº {doc.Numero:D9}   SÉRIE {doc.Serie}   MODELO {doc.Modelo}").Bold();

                    if (homologacao)
                    {
                        col.Item().PaddingTop(2).AlignCenter().Text("EMISSÃO EM HOMOLOGAÇÃO — SEM VALOR FISCAL").Bold().FontColor(Colors.Red.Medium).FontSize(7);
                    }

                    // ---- Chave de acesso + protocolo + QR ----
                    col.Item().PaddingTop(3).AlignCenter().Text(PdfFormatters.FormatarChave(doc.ChaveAcesso)).FontSize(7);
                    col.Item().AlignCenter().Text($"Protocolo de autorização: {doc.ProtocoloAutorizacao ?? "-"}").FontSize(6);
                    if (qr is not null)
                    {
                        col.Item().PaddingTop(3).AlignCenter().Width(140).Image(PdfBarcodes.GerarQrCode(qr)).FitWidth();
                        col.Item().AlignCenter().Text("Consulta via leitor de QR Code").FontSize(6);
                    }

                    col.Item().PaddingTop(3).LineHorizontal(0.5f);

                    // ---- Consumidor ----
                    if (req?.Destinatario is { } dest)
                    {
                        col.Item().PaddingTop(2).Text("CONSUMIDOR").Bold();
                        col.Item().Text(dest.Nome);
                        col.Item().Text($"CPF/CNPJ: {PdfFormatters.FormatarCnpjCpf(dest.CnpjCpf)}");
                        if (dest.Endereco is { } end)
                        {
                            col.Item().Text($"{end.Logradouro ?? "-"}, {end.Numero ?? "-"} — {end.Bairro ?? "-"}");
                            col.Item().Text($"{end.NomeMunicipio ?? "-"}/{end.Uf ?? "-"}");
                        }
                    }
                    else
                    {
                        col.Item().PaddingTop(2).AlignCenter().Text("CONSUMIDOR NÃO IDENTIFICADO").FontSize(7);
                    }

                    col.Item().PaddingTop(3).LineHorizontal(0.5f);

                    // ---- Itens ----
                    if (req?.Itens is { Count: > 0 } itens)
                    {
                        var numero = 1;
                        foreach (var item in itens)
                        {
                            col.Item().PaddingTop(2).Text($"#{numero} {item.Descricao}").Bold();
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"{item.Quantidade:N3} {item.Unidade ?? "UN"} X {item.ValorUnitario:N2}");
                                row.ConstantItem(60).AlignRight().Text(item.ValorTotal.ToString("N2"));
                            });
                            numero++;
                        }
                    }

                    col.Item().PaddingTop(3).LineHorizontal(0.5f);

                    // ---- Totais ----
                    var totais = req?.Totais;
                    col.Item().Text($"Qtd. total de itens: {req?.Itens?.Count ?? 0}");
                    if (totais?.ValorDesconto is > 0)
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Desconto");
                            row.ConstantItem(60).AlignRight().Text($"− {totais.ValorDesconto.Value:N2}");
                        });
                    }
                    if (totais?.ValorFrete is > 0)
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Frete");
                            row.ConstantItem(60).AlignRight().Text($"+ {totais.ValorFrete.Value:N2}");
                        });
                    }
                    if (totais?.ValorSeguro is > 0)
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Seguro");
                            row.ConstantItem(60).AlignRight().Text($"+ {totais.ValorSeguro.Value:N2}");
                        });
                    }
                    if (totais?.OutrasDespesas is > 0)
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text("Outras despesas");
                            row.ConstantItem(60).AlignRight().Text($"+ {totais.OutrasDespesas.Value:N2}");
                        });
                    }
                    col.Item().PaddingTop(2).Row(row =>
                    {
                        row.RelativeItem().Text("VALOR TOTAL").Bold().FontSize(9);
                        row.ConstantItem(60).AlignRight().Text($"R$ {(totais?.ValorNota ?? 0):N2}").Bold().FontSize(9);
                    });

                    col.Item().PaddingTop(3).LineHorizontal(0.5f);

                    // ---- Pagamentos ----
                    if (req?.Pagamento is { Count: > 0 } pags)
                    {
                        foreach (var pag in pags)
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text(PdfFormatters.NomeFormaPagamento(pag.Forma));
                                row.ConstantItem(60).AlignRight().Text(pag.Valor.ToString("N2"));
                            });
                        }

                        var pago = pags.Sum(p => p.Valor);
                        var troco = pago - (totais?.ValorNota ?? 0);
                        if (troco > 0)
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Troco").Bold();
                                row.ConstantItem(60).AlignRight().Text($"R$ {troco:N2}").Bold();
                            });
                        }
                    }

                    col.Item().PaddingTop(3).LineHorizontal(0.5f);

                    // ---- Observações ----
                    if (!string.IsNullOrWhiteSpace(req?.InformacoesComplementares))
                    {
                        col.Item().PaddingTop(2).Text(req.InformacoesComplementares).FontSize(6);
                    }

                    if (homologacao)
                    {
                        col.Item().PaddingTop(4).AlignCenter().Text("SEM VALOR FISCAL").Bold().FontColor(Colors.Red.Medium);
                    }
                });
            });
        });

        return document.GeneratePdf();
    }
}
