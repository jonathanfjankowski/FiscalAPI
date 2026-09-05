using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Fiscal.Persistence.Criptografia;

public class EnvelopeEncryptionService : ICertificadoStore
{
    private readonly byte[] _kek;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public EnvelopeEncryptionService(IConfiguration configuration)
    {
        var raw = configuration["Certificados:ChaveMestraKEK"];
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Certificados:ChaveMestraKEK não configurada.");

        _kek = Convert.FromBase64String(raw);
        if (_kek.Length != 32)
            throw new InvalidOperationException("KEK deve ter 32 bytes (AES-256).");
    }

    public async Task<CertificadoEnvelope> CriarEnvelopeAsync(byte[] pfx, string senha, CancellationToken ct)
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        var noncePfx = RandomNumberGenerator.GetBytes(NonceSize);
        var nonceSenha = RandomNumberGenerator.GetBytes(NonceSize);

        var pfxCifrado = Cifrar(pfx, dek, noncePfx);
        var senhaCifrada = Cifrar(System.Text.Encoding.UTF8.GetBytes(senha), dek, nonceSenha);
        var dekCifrada = CifrarDek(dek);

        // Carrega só pra extrair thumbprint + validade; depois descarta.
        using var temp = X509CertificateLoader.LoadPkcs12(pfx, senha);
        var thumbprint = temp.Thumbprint;
        var validoAte = DateOnly.FromDateTime(temp.NotAfter);

        return new CertificadoEnvelope(pfxCifrado, senhaCifrada, dekCifrada, thumbprint, validoAte);
    }

    public async Task<X509Certificate2> CarregarAsync(Certificado certificado, CancellationToken ct)
    {
        var dek = DecifrarDek(certificado.ChaveDekCriptografada);
        var pfx = Decifrar(certificado.PfxCriptografado, dek);
        var senha = System.Text.Encoding.UTF8.GetString(Decifrar(certificado.SenhaCriptografada, dek));
        return X509CertificateLoader.LoadPkcs12(pfx, senha);
    }

    public async Task SalvarAsync(Guid tenantId, CertificadoEnvelope envelope, CancellationToken ct)
    {
        // Implementado pelo controller/handler que já tem acesso ao DbContext;
        // esta interface fica como contrato. Aqui só guardamos envelope em memória se quiser.
        // (Persistência concreta está em RepositorioCertificado + controller.)
        await Task.CompletedTask;
    }

    public Task<byte[]> CifrarTextoAsync(string texto, CancellationToken ct)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cifrado = Cifrar(System.Text.Encoding.UTF8.GetBytes(texto), _kek, nonce);
        return Task.FromResult(cifrado);
    }

    public Task<string> DecifrarTextoAsync(byte[] dados, CancellationToken ct)
    {
        var plain = Decifrar(dados, _kek);
        return Task.FromResult(System.Text.Encoding.UTF8.GetString(plain));
    }

    private byte[] Cifrar(byte[] plain, byte[] dek, byte[] nonce)
    {
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(dek, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        var output = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, output, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, output, nonce.Length + cipher.Length, tag.Length);
        return output;
    }

    private byte[] Decifrar(byte[] envelope, byte[] dek)
    {
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[envelope.Length - NonceSize - TagSize];
        Buffer.BlockCopy(envelope, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(envelope, NonceSize, cipher, 0, cipher.Length);
        Buffer.BlockCopy(envelope, NonceSize + cipher.Length, tag, 0, TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(dek, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private byte[] CifrarDek(byte[] dek)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[dek.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_kek, TagSize);
        aes.Encrypt(nonce, dek, cipher, tag);
        var output = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, output, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, output, nonce.Length + cipher.Length, tag.Length);
        return output;
    }

    private byte[] DecifrarDek(byte[] envelope)
    {
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[envelope.Length - NonceSize - TagSize];
        Buffer.BlockCopy(envelope, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(envelope, NonceSize, cipher, 0, cipher.Length);
        Buffer.BlockCopy(envelope, NonceSize + cipher.Length, tag, 0, TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_kek, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
