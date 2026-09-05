using Fiscal.Core.Entities;

namespace Fiscal.Core.Interfaces;

public interface IGeradorPdf
{
    Task<byte[]> GerarDanfeAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct);
    Task<byte[]> GerarDanfceAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct);
    Task<byte[]> GerarDanfseAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct);
}
