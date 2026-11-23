using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using QuestPDF.Infrastructure;

namespace Fiscal.Pdf;

/// <summary>
/// Fachada de geração de DANFE/DANFCe/DANFSe via QuestPDF (licença Community).
/// Delega cada modelo para seu documento dedicado (<see cref="DanfeDocument"/>,
/// <see cref="DanfceDocument"/> e <see cref="DanfseDocument"/>).
/// </summary>
public class GeradorPdfQuestPdf : IGeradorPdf
{
    static GeradorPdfQuestPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> GerarDanfeAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct) =>
        Task.FromResult(DanfeDocument.Gerar(documento, tenant));

    public Task<byte[]> GerarDanfceAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct) =>
        Task.FromResult(DanfceDocument.Gerar(documento, tenant));

    public Task<byte[]> GerarDanfseAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct) =>
        Task.FromResult(DanfseDocument.Gerar(documento, tenant));
}
