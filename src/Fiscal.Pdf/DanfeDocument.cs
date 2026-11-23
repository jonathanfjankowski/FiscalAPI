using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Fiscal.Pdf;

/// <summary>DANFE A4 (NF-e modelo 55) via QuestPDF.</summary>
internal static class DanfeDocument
{
    public static byte[] Gerar(DocumentoFiscal doc, Tenant tenant)
    {
        var req = PdfFormatters.TryDeserialize(doc.PayloadEntrada);
        var homologacao = doc.Ambiente == (short)Ambiente.Homologacao;
        var entrada = string.Equals(req?.TipoOperacao, "entrada", StringComparison.OrdinalIgnoreCase);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(tenant.RazaoSocial).Bold().FontSize(11);
                            c.Item().Text($"CNPJ: {PdfFormatters.FormatarCnpj(tenant.Cnpj)}   IE: {tenant.InscricaoEstadual ?? "-"}");
                            c.Item().Text($"{tenant.Logradouro ?? "-"}, {tenant.Numero ?? "-"} — {tenant.Bairro ?? "-"}");
                            c.Item().Text($"{tenant.NomeMunicipio ?? "-"} / {tenant.Uf} — CEP {PdfFormatters.FormatarCep(tenant.Cep)}");
                        });

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignCenter().Text("DANFE").Bold().FontSize(16);
                            c.Item().AlignCenter().Text("Documento Auxiliar da Nota Fiscal Eletrônica").FontSize(7);
                            c.Item().PaddingTop(4).AlignCenter().Text($"{doc.Modelo}   SÉRIE {doc.Serie}   Nº {doc.Numero}").Bold();
                        });

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(text =>
                            {
                                var spanEntrada = text.Span("0 - ENTRADA").FontSize(8);
                                if (entrada) spanEntrada.Bold();
                                text.Span("    ");
                                var spanSaida = text.Span("1 - SAÍDA").FontSize(8);
                                if (!entrada) spanSaida.Bold();
                            });
                            c.Item().PaddingTop(2).Border(1).Padding(3).Column(k =>
                            {
                                k.Item().Text("CHAVE DE ACESSO").Bold().FontSize(7);
                                k.Item().Text(PdfFormatters.FormatarChave(doc.ChaveAcesso)).FontSize(8);
                                var barcode = PdfBarcodes.GerarCodigoBarrasChave(doc.ChaveAcesso);
                                if (barcode is not null)
                                    k.Item().PaddingTop(2).Height(46).Image(barcode).FitArea();
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

                    // ---- Canhoto (recebimento) ----
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(2);
                        });
                        t.Cell().Border(0.5f).Padding(4).Column(x =>
                        {
                            x.Item().Text($"RECEBEMOS DE {tenant.RazaoSocial} OS PRODUTOS/SERVIÇOS CONSTANTES DA NOTA FISCAL INDICADA AO LADO").FontSize(7);
                            x.Item().PaddingTop(3).Text("DATA DE RECEBIMENTO").FontSize(6);
                            x.Item().BorderBottom(0.5f).Text(" ").FontSize(10);
                            x.Item().Text("IDENTIFICAÇÃO E ASSINATURA DO RECEBEDOR").FontSize(6);
                        });
                        t.Cell().Border(0.5f).Padding(4).Column(x =>
                        {
                            x.Item().Row(r =>
                            {
                                r.RelativeItem().Column(k =>
                                {
                                    k.Item().Text("NF-e").FontSize(6);
                                    k.Item().Text($"{doc.Modelo}").Bold().FontSize(10);
                                });
                                r.RelativeItem().Column(k =>
                                {
                                    k.Item().Text("SÉRIE").FontSize(6);
                                    k.Item().Text($"{doc.Serie}").Bold().FontSize(10);
                                });
                                r.RelativeItem().Column(k =>
                                {
                                    k.Item().Text("NÚMERO").FontSize(6);
                                    k.Item().Text($"{doc.Numero}").Bold().FontSize(10);
                                });
                            });
                        });
                    });
                    col.Item().PaddingTop(4);

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

                    // ---- CÁLCULO DO IMPOSTO ----
                    if (req is not null)
                    {
                        var t2 = req.Totais;
                        var campos = new List<(string Titulo, string Valor)>
                        {
                            ("BASE DE CÁLC. DO ICMS", PdfFormatters.SomaBaseIcms(req).ToString("N2")),
                            ("VALOR DO ICMS", PdfFormatters.SomaIcms(req).ToString("N2")),
                            ("V. ICMS DESONERADO", PdfFormatters.SomaDesonerado(req).ToString("N2")),
                            ("BASE CÁLC. ICMS ST", PdfFormatters.SomaBcSt(req).ToString("N2")),
                            ("VALOR ICMS ST", PdfFormatters.SomaSt(req).ToString("N2")),
                            ("VALOR DO FCP", PdfFormatters.SomaFcp(req).ToString("N2")),
                            ("V. TOTAL PRODUTOS", t2.ValorProdutos.ToString("N2")),
                            ("VALOR DO FRETE", (t2.ValorFrete ?? 0).ToString("N2")),
                            ("VALOR DO SEGURO", (t2.ValorSeguro ?? 0).ToString("N2")),
                            ("DESCONTO", (t2.ValorDesconto ?? 0).ToString("N2")),
                            ("VALOR DO II", PdfFormatters.SomaIi(req).ToString("N2")),
                            ("VALOR DO IPI", PdfFormatters.SomaIpi(req).ToString("N2")),
                            ("VALOR PIS / COFINS", $"{PdfFormatters.SomaPis(req):N2} / {PdfFormatters.SomaCofins(req):N2}"),
                            ("OUTRAS DESPESAS", (t2.OutrasDespesas ?? 0).ToString("N2")),
                            ("VALOR TOTAL DA NOTA", t2.ValorNota.ToString("N2")),
                        };

                        if (t2.ValorIbs is > 0 || t2.ValorCbs is > 0 || t2.ValorIs is > 0)
                        {
                            campos.Add(("IBS / CBS / IS", $"{(t2.ValorIbs ?? 0):N2} / {(t2.ValorCbs ?? 0):N2} / {(t2.ValorIs ?? 0):N2}"));
                        }

                        col.Item().PaddingTop(6).Text("CÁLCULO DO IMPOSTO").Bold().FontSize(8);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                for (var i = 0; i < 4; i++) c.RelativeColumn();
                            });

                            foreach (var (titulo, valor) in campos)
                            {
                                t.Cell().Border(0.5f).Padding(2).Column(x =>
                                {
                                    x.Item().Text(titulo).Bold().FontSize(6);
                                    x.Item().Text(valor).FontSize(8);
                                });
                            }
                        });
                    }

                    if (req?.Destinatario is { } dest)
                    {
                        col.Item().PaddingTop(6).Text("DESTINATÁRIO / REMETENTE").Bold().FontSize(8);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("NOME / RAZÃO SOCIAL").Bold().FontSize(6);
                                x.Item().Text(dest.Nome);
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("CNPJ / CPF").Bold().FontSize(6);
                                x.Item().Text(PdfFormatters.FormatarCnpjCpf(dest.CnpjCpf));
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("INSCRIÇÃO ESTADUAL").Bold().FontSize(6);
                                x.Item().Text(dest.InscricaoEstadual ?? "-");
                            });
                            t.Cell().ColumnSpan(2).Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("ENDEREÇO").Bold().FontSize(6);
                                x.Item().Text(dest.Endereco is null ? "-"
                                    : $"{dest.Endereco.Logradouro ?? "-"}, {dest.Endereco.Numero ?? "-"}" +
                                      (string.IsNullOrEmpty(dest.Endereco.Complemento) ? "" : $" — {dest.Endereco.Complemento}"));
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("BAIRRO").Bold().FontSize(6);
                                x.Item().Text(dest.Endereco?.Bairro ?? "-");
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("MUNICÍPIO").Bold().FontSize(6);
                                x.Item().Text(dest.Endereco?.NomeMunicipio ?? "-");
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("UF").Bold().FontSize(6);
                                x.Item().Text(dest.Endereco?.Uf ?? "-");
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("CEP").Bold().FontSize(6);
                                x.Item().Text(PdfFormatters.FormatarCep(dest.Endereco?.Cep));
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("CÓD. MUNICÍPIO (IBGE)").Bold().FontSize(6);
                                x.Item().Text(dest.Endereco?.CodigoMunicipioIbge ?? "-");
                            });
                        });
                    }

                    // ---- FATURA / PAGAMENTOS ----
                    if (req?.Pagamento is { Count: > 0 } pags)
                    {
                        col.Item().PaddingTop(6).Text("FATURA / PAGAMENTOS").Bold().FontSize(8);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn();
                                c.RelativeColumn(2);
                            });
                            foreach (var pag in pags)
                            {
                                t.Cell().Border(0.5f).Padding(2).Column(x =>
                                {
                                    x.Item().Text("FORMA").Bold().FontSize(6);
                                    x.Item().Text($"{pag.Forma} — {PdfFormatters.NomeFormaPagamento(pag.Forma)}").FontSize(8);
                                });
                                t.Cell().Border(0.5f).Padding(2).Column(x =>
                                {
                                    x.Item().Text("VALOR (R$)").Bold().FontSize(6);
                                    x.Item().Text(pag.Valor.ToString("N2")).FontSize(8);
                                });
                                t.Cell().Border(0.5f).Padding(2).Column(x =>
                                {
                                    x.Item().Text("CARTÃO").Bold().FontSize(6);
                                    x.Item().Text(pag.Bandeira is null ? "-"
                                        : $"bandeira {pag.Bandeira} — aut. {pag.Autorizacao ?? "-"}").FontSize(7);
                                });
                            }
                        });
                    }

                    // ---- TRANSPORTADOR / VOLUMES ----
                    var tra = req?.Transporte;
                    col.Item().PaddingTop(6).Text("TRANSPORTADOR / VOLUMES TRANSPORTADOS").Bold().FontSize(8);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });
                        t.Cell().Border(0.5f).Padding(2).Column(x =>
                        {
                            x.Item().Text("MODALIDADE DO FRETE").Bold().FontSize(6);
                            x.Item().Text(tra?.ModalidadeFrete is not null
                                ? $"código {tra.ModalidadeFrete}"
                                : (req?.Totais.ValorFrete is > 0 ? "0 — CIF (conta do remetente)" : "9 — sem ocorrência")).FontSize(8);
                        });
                        t.Cell().Border(0.5f).Padding(2).Column(x =>
                        {
                            x.Item().Text("TRANSPORTADORA").Bold().FontSize(6);
                            x.Item().Text(tra?.Transportadora?.Nome ?? "-").FontSize(8);
                        });
                        t.Cell().Border(0.5f).Padding(2).Column(x =>
                        {
                            x.Item().Text("CNPJ / CPF").Bold().FontSize(6);
                            x.Item().Text(tra?.Transportadora?.CnpjCpf is { } cnpj ? PdfFormatters.FormatarCnpjCpf(cnpj) : "-").FontSize(8);
                        });
                        t.Cell().Border(0.5f).Padding(2).Column(x =>
                        {
                            x.Item().Text("INSCRIÇÃO ESTADUAL").Bold().FontSize(6);
                            x.Item().Text(tra?.Transportadora?.InscricaoEstadual ?? "-").FontSize(8);
                        });
                        t.Cell().Border(0.5f).Padding(2).Column(x =>
                        {
                            x.Item().Text("ENDEREÇO").Bold().FontSize(6);
                            x.Item().Text(tra?.Transportadora?.EnderecoLogradouro ?? "-").FontSize(8);
                        });
                        t.Cell().Border(0.5f).Padding(2).Column(x =>
                        {
                            x.Item().Text("MUNICÍPIO / UF").Bold().FontSize(6);
                            x.Item().Text(tra?.Transportadora is null ? "-"
                                : $"{tra.Transportadora.EnderecoMunicipio ?? "-"}/{tra.Transportadora.EnderecoUf ?? "-"}").FontSize(8);
                        });
                        foreach (var vol in tra?.Volumes ?? [])
                        {
                            t.Cell().ColumnSpan(2).Border(0.5f).Padding(2).Column(x =>
                            {
                                x.Item().Text("QTD / ESPÉCIE / MARCA / NUMERAÇÃO").Bold().FontSize(6);
                                x.Item().Text($"{vol.Quantidade?.ToString() ?? "-"} / {vol.Especie ?? "-"} / {vol.Marca ?? "-"} / {vol.Numeracao ?? "-"}").FontSize(8);
                            });
                            t.Cell().Border(0.5f).Padding(2).Column(x =>
                            {
                                x.Item().Text("PESO LÍQ. / BRUTO (kg)").Bold().FontSize(6);
                                x.Item().Text($"{(vol.PesoLiquido?.ToString("N3") ?? "-")} / {(vol.PesoBruto?.ToString("N3") ?? "-")}").FontSize(8);
                            });
                            t.Cell().ColumnSpan(3).Border(0.5f).Padding(2).Column(x =>
                            {
                                x.Item().Text("LACRES").Bold().FontSize(6);
                                x.Item().Text(vol.Lacres is { Count: > 0 }
                                    ? string.Join(", ", vol.Lacres.Select(l => l.Numero)) : "-").FontSize(8);
                            });
                        }
                    });

                    // ---- DADOS DOS PRODUTOS / SERVIÇOS ----
                    col.Item().PaddingTop(8).Text("DADOS DOS PRODUTOS / SERVIÇOS").Bold().FontSize(8);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(12);
                            c.ConstantColumn(38);
                            c.RelativeColumn();
                            c.ConstantColumn(40);
                            c.ConstantColumn(20);
                            c.ConstantColumn(26);
                            c.ConstantColumn(13);
                            c.ConstantColumn(34);
                            c.ConstantColumn(34);
                            c.ConstantColumn(36);
                            c.ConstantColumn(34);
                            c.ConstantColumn(32);
                        });

                        foreach (var titulo in PdfFormatters.CabecalhoItens())
                            t.Cell().Border(0.5f).Background("#EFEFEF").Padding(2).Text(titulo).Bold().FontSize(6);

                        var itens = req?.Itens ?? [];
                        for (var i = 0; i < itens.Count; i++)
                        {
                            var item = itens[i];
                            foreach (var valor in PdfFormatters.ValoresItem(i + 1, item))
                                t.Cell().Border(0.5f).Padding(2).Text(valor).FontSize(6);
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
                                x.Item().Text(PdfFormatters.SomaIcms(req).ToString("N2"));
                            });
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text("TOTAL DA NOTA").Bold().FontSize(8);
                                x.Item().Text((req?.Totais.ValorNota ?? 0).ToString("N2")).Bold();
                            });
                        });
                    });

                    // ---- DADOS ADICIONAIS ----
                    col.Item().PaddingTop(8).Border(0.5f).Padding(4).Column(x =>
                    {
                        x.Item().Text("DADOS ADICIONAIS").Bold().FontSize(7);
                        if (!string.IsNullOrWhiteSpace(req?.InformacoesComplementares))
                            x.Item().Text(req.InformacoesComplementares).FontSize(7);

                        if (req?.NfesReferenciadas is { Count: > 0 } refs)
                        {
                            var chaves = string.Join("; ", refs.Select(r => PdfFormatters.FormatarChave(r.ChaveAcesso)));
                            x.Item().PaddingTop(2).Text($"NF-e referenciadas: {chaves}").FontSize(7);
                        }

                        var obs = "DANFE gerado pelo FiscalAPI a partir do XML autorizado.";
                        if (!string.IsNullOrEmpty(doc.MotivoStatus))
                            obs += $"  {doc.MotivoStatus}";
                        if (doc.ModoContingencia is not null)
                            obs += $"  [Contingência: {doc.ModoContingencia}" +
                                   (doc.EpecProtocolo is not null ? $" — EPEC {doc.EpecProtocolo}" : "") + "]";
                        x.Item().PaddingTop(2).Text(obs).FontSize(7);
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span($"FiscalAPI — DANFE gerado a partir do XML autorizado em {DateTimeOffset.UtcNow:dd/MM/yyyy HH:mm}");
                });
            });
        });

        return document.GeneratePdf();
    }
}
