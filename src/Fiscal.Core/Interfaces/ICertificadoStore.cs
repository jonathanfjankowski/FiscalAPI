using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;

namespace Fiscal.Core.Interfaces;

public record CertificadoEnvelope(
    byte[] PfxCifrado,
    byte[] SenhaCifrada,
    byte[] DekCifrada,
    string Thumbprint,
    DateOnly ValidoAte);

public interface ICertificadoStore
{
    Task<CertificadoEnvelope> CriarEnvelopeAsync(byte[] pfx, string senha, CancellationToken ct);
    Task<X509Certificate2> CarregarAsync(Certificado certificado, CancellationToken ct);
    Task SalvarAsync(Guid tenantId, CertificadoEnvelope envelope, CancellationToken ct);

    /// <summary>
    /// Cifra texto sensível de tenant (hoje: CSC da NFC-e) com a KEK — AES-GCM,
    /// formato nonce||cipher||tag. Para segredos menores/mais simples que o PFX.
    /// </summary>
    Task<byte[]> CifrarTextoAsync(string texto, CancellationToken ct);

    /// <summary>
    /// Decifra o que <see cref="CifrarTextoAsync"/> produziu.
    /// </summary>
    Task<string> DecifrarTextoAsync(byte[] dados, CancellationToken ct);
}
