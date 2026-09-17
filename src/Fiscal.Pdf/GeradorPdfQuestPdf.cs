using SkiaSharp;
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
/// DANFE/DANFCe/DANFSe via QuestPDF (licença Community). Layouts
/// simplificados — não são os leiautes oficiais (DANFE de 20 campos do
/// convênio), mas carregam todos os dados essenciais (identificação,
/// emitente/destinatário, itens/serviço, totais, pagamento, chave e
/// protocolo). Em homologação imprime marca d'água.
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
        Task.FromResult(GerarDanfse(documento, tenant));

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
                                // F6: barcode CODE-128 da chave (padrão visual do DANFE).
                                var barcode = GerarCodigoBarrasChave(doc.ChaveAcesso);
                                if (barcode is not null)
                                    k.Item().PaddingTop(2).Height(46).Image(barcode).FitArea();
                            });
                            c.Item().PaddingTop(2).Text($"PROTOCOLO: {doc.ProtocoloAutorizacao ?? "-"}").FontSize(8);
                        });
                        // F6: QR Code do DANFCe (extraído do infNFeSupl/qrCode do XML autorizado).
                        var qr = ExtrairQrCode(doc.XmlAssinado);
                        if (nfce && qr is not null)
                        {
                            row.ConstantItem(110).Column(c =>
                            {
                                c.Item().Text("Consulta via leitor de QR Code").FontSize(6);
                                c.Item().PaddingTop(2).Image(GerarQrCode(qr)).FitWidth();
                            });
                        }
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

    /// <summary>
    /// DANFSe simplificado da NFS-e Nacional: prestador, tomador,
    /// detalhamento do serviço, valores (incl. retenções federais e total de
    /// tributos), identificador da NFS-e e protocolo. O payload é o
    /// NfseDpsRequest (direto ou embrulhado em substituição) — mesma regra de
    /// leitura do MapperDps. Payload legado (EmissaoRequest) ainda gera o PDF,
    /// só sem as seções de serviço/tomador.
    /// </summary>
    private static byte[] GerarDanfse(DocumentoFiscal doc, Tenant tenant)
    {
        var req = TryDeserializeDps(doc.PayloadEntrada);
        var servico = req?.Servico;
        var valores = req?.Valores;
        var tomador = req?.Tomador;
        var homologacao = doc.Ambiente == (short)Ambiente.Homologacao;
        var competencia = DateOnly.TryParse(req?.DataCompetencia, out var data)
            ? data
            : new DateOnly(doc.CriadoEm.Year, doc.CriadoEm.Month, doc.CriadoEm.Day);

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
                            c.Item().Text($"CNPJ: {FormatarCnpjCpf(tenant.Cnpj)}   IM: {tenant.InscricaoMunicipal ?? "-"}");
                            c.Item().Text($"{tenant.Logradouro ?? "-"}, {tenant.Numero ?? "-"} — {tenant.Bairro ?? "-"}");
                            c.Item().Text($"{tenant.NomeMunicipio ?? "-"} / {tenant.Uf} — CEP {FormatarCep(tenant.Cep)}");
                        });

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignCenter().Text("DANFSe").Bold().FontSize(16);
                            c.Item().AlignCenter().Text("Documento Auxiliar da Nota Fiscal de Serviços Eletrônica").FontSize(7);
                            c.Item().PaddingTop(4).AlignCenter().Text($"NFS-e Nº {doc.Numero}   SÉRIE {doc.Serie}").Bold();
                            c.Item().AlignCenter().Text($"COMPETÊNCIA: {competencia:MM/yyyy}").FontSize(8);
                        });

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(homologacao ? "AMBIENTE: HOMOLOGAÇÃO" : "AMBIENTE: PRODUÇÃO").FontSize(8);
                            c.Item().PaddingTop(2).Border(1).Padding(3).Column(k =>
                            {
                                k.Item().Text("IDENTIFICADOR NFS-e").Bold().FontSize(7);
                                k.Item().Text(string.IsNullOrEmpty(doc.ChaveAcesso)
                                    ? "-"
                                    : string.Join(" ", Chunk(doc.ChaveAcesso, 4))).FontSize(8);
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
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        t.Cell().Border(0.5f).Padding(3).Column(x =>
                        {
                            x.Item().Text("PRESTADOR").Bold().FontSize(7);
                            x.Item().Text($"{tenant.RazaoSocial} — CNPJ {FormatarCnpjCpf(tenant.Cnpj)}");
                            x.Item().Text($"IM: {tenant.InscricaoMunicipal ?? "-"}   Município: {tenant.NomeMunicipio ?? "-"} / {tenant.Uf}");
                        });
                        t.Cell().Border(0.5f).Padding(3).Column(x =>
                        {
                            x.Item().Text("TOMADOR").Bold().FontSize(7);
                            if (tomador is { } toma)
                            {
                                x.Item().Text($"{toma.Nome ?? "-"} — {FormatarCnpjCpf(toma.CnpjCpf)}");
                                x.Item().Text(toma.Endereco is { } end
                                    ? $"{end.Logradouro}, {end.Numero} — {end.Bairro} — CEP {FormatarCep(end.Cep)} — Mun. IBGE {end.CodigoMunicipioIbge}"
                                    : "-");
                            }
                            else
                            {
                                x.Item().Text("-");
                            }
                        });
                    });

                    col.Item().PaddingTop(6).Text("DETALHAMENTO DO SERVIÇO").Bold().FontSize(8);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(120);
                            c.RelativeColumn();
                        });

                        t.Cell().Border(0.5f).Padding(3).Text("DESCRIÇÃO").Bold().FontSize(7);
                        t.Cell().Border(0.5f).Padding(3).Text(servico?.DescricaoServico ?? "-");
                        t.Cell().Border(0.5f).Padding(3).Text("CÓD. TRIBUTÁRIO NACIONAL / MUNICIPAL").Bold().FontSize(7);
                        t.Cell().Border(0.5f).Padding(3).Text($"{servico?.CodigoTributarioNacional ?? "-"} / {servico?.CodigoTributarioMunicipal ?? "-"}");
                        t.Cell().Border(0.5f).Padding(3).Text("NBS / MUNICÍPIO DA PRESTAÇÃO (IBGE)").Bold().FontSize(7);
                        t.Cell().Border(0.5f).Padding(3).Text(
                            $"{servico?.CodigoNbs ?? "-"} / {servico?.CodigoMunicipioPrestacao?.ToString() ?? tenant.CodigoMunicipioIbge ?? "-"}");
                    });

                    col.Item().PaddingTop(6).Text("VALORES").Bold().FontSize(8);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        foreach (var (titulo, valor) in ValoresNfse(valores))
                        {
                            t.Cell().Border(0.5f).Padding(3).Column(x =>
                            {
                                x.Item().Text(titulo).Bold().FontSize(7);
                                x.Item().Text(valor);
                            });
                        }
                    });

                    if (!string.IsNullOrEmpty(req?.InformacoesComplementares))
                    {
                        col.Item().PaddingTop(8).Text(t =>
                        {
                            t.Span("INFORMAÇÕES COMPLEMENTARES: ").Bold().FontSize(8);
                            t.Span(req.InformacoesComplementares).FontSize(8);
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
                    t.Span($"FiscalAPI — DANFSe gerado a partir do XML autorizado em {DateTimeOffset.UtcNow:dd/MM/yyyy HH:mm}");
                });
            });
        });

        return document.GeneratePdf();
    }

    private static IEnumerable<(string Titulo, string Valor)> ValoresNfse(NfseValoresDto? valores)
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

    /// <summary>Lê o NfseDpsRequest do payload — direto ou embrulhado em
    /// NfseDpsSubstituicaoRequest (detecta cMotivo), mesma regra do
    /// MapperDps.LerRequest. Retorna null se não for um DPS (ex.: payload
    /// legado da rota sandbox).</summary>
    private static NfseDpsRequest? TryDeserializeDps(string payload)
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

    private static IEnumerable<string> Chunk(string valor, int tamanho)
    {
        for (var i = 0; i < valor.Length; i += tamanho)
            yield return valor.Substring(i, Math.Min(tamanho, valor.Length - i));
    }

    private static string FormatarCnpjCpf(string valor) => valor.Length switch
    {
        14 => $"{valor[..2]}.{valor.Substring(2, 3)}.{valor.Substring(5, 3)}/{valor.Substring(8, 4)}-{valor[12..]}",
        11 => $"{valor[..3]}.{valor.Substring(3, 3)}.{valor.Substring(6, 3)}-{valor[9..]}",
        _ => valor,
    };

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
    /// <summary>Código de barras CODE-128 da chave de acesso (44 dígitos) em PNG.</summary>
    private static byte[]? GerarCodigoBarrasChave(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44 || chave.Any(c => !char.IsDigit(c)))
            return null;

        var writer = new ZXing.SkiaSharp.BarcodeWriter
        {
            Format = ZXing.BarcodeFormat.CODE_128,
            Options = new ZXing.Common.EncodingOptions
            {
                Width = 420,
                Height = 90,
                Margin = 0,
                PureBarcode = true,
            },
        };
        using var bitmap = writer.Write(chave);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Extrai a URL do QR Code do DANFCe do XML autorizado (infNFeSupl/qrCode).</summary>
    private static string? ExtrairQrCode(string? xmlAssinado)
    {
        if (string.IsNullOrWhiteSpace(xmlAssinado))
            return null;

        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xmlAssinado);
            var qr = doc.SelectSingleNode("//*[local-name()='qrCode']")?.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(qr))
                return null;
            // o XML traz o texto com CDATA/escape — remove envoltório <![CDATA[ ]]>
            return qr.Replace("<![CDATA[", "").Replace("]]>", "").Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>QR Code em PNG a partir da URL de consulta do DANFCe.</summary>
    private static byte[] GerarQrCode(string conteudo)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(conteudo, QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(pixelsPerModule: 4);
        return png;
    }
}
