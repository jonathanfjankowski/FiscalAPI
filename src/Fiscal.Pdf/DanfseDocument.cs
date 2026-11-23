using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Fiscal.Pdf;

/// <summary>
/// DANFSe simplificado da NFS-e Nacional (extraído sem alteração de layout).
/// </summary>
internal static class DanfseDocument
{
    public static byte[] Gerar(DocumentoFiscal doc, Tenant tenant)
    {
        var req = PdfFormatters.TryDeserializeDps(doc.PayloadEntrada);
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
                            c.Item().Text($"CNPJ: {PdfFormatters.FormatarCnpjCpf(tenant.Cnpj)}   IM: {tenant.InscricaoMunicipal ?? "-"}");
                            c.Item().Text($"{tenant.Logradouro ?? "-"}, {tenant.Numero ?? "-"} — {tenant.Bairro ?? "-"}");
                            c.Item().Text($"{tenant.NomeMunicipio ?? "-"} / {tenant.Uf} — CEP {PdfFormatters.FormatarCep(tenant.Cep)}");
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
                                    : string.Join(" ", PdfFormatters.Chunk(doc.ChaveAcesso, 4))).FontSize(8);
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
                            x.Item().Text($"{tenant.RazaoSocial} — CNPJ {PdfFormatters.FormatarCnpjCpf(tenant.Cnpj)}");
                            x.Item().Text($"IM: {tenant.InscricaoMunicipal ?? "-"}   Município: {tenant.NomeMunicipio ?? "-"} / {tenant.Uf}");
                        });
                        t.Cell().Border(0.5f).Padding(3).Column(x =>
                        {
                            x.Item().Text("TOMADOR").Bold().FontSize(7);
                            if (tomador is { } toma)
                            {
                                x.Item().Text($"{toma.Nome ?? "-"} — {PdfFormatters.FormatarCnpjCpf(toma.CnpjCpf)}");
                                x.Item().Text(toma.Endereco is { } end
                                    ? $"{end.Logradouro}, {end.Numero} — {end.Bairro} — CEP {PdfFormatters.FormatarCep(end.Cep)} — Mun. IBGE {end.CodigoMunicipioIbge}"
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

                        foreach (var (titulo, valor) in PdfFormatters.ValoresNfse(valores))
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
}
