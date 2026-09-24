using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fiscal.Core.Entities;
using Fiscal.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Fiscal.Persistence.Criptografia;

/// <summary>
/// Envelope AES-256-GCM (DEK por certificado, KEK externa ao banco).
///
/// Material cifrado pela KEK (DEK, textos curtos) recebe prefixo de versão:
///   [0x02, kid] + nonce|cipher|tag — kid 0x01 = KEK atual, 0x00 = KEK anterior.
/// Envelopes sem prefixo (legado) são lidos com a KEK atual. Isso permite
/// rotação: configure ChaveMestraKEKAnterior + nova ChaveMestraKEK, o legado
/// continua legível e os dados novos já nascem com a KEK nova.
/// </summary>
public class EnvelopeEncryptionService : ICertificadoStore
{
    private const byte VersaoEnvelope = 0x02;
    private const byte KidKekAtual = 0x01;
    private const byte KidKekAnterior = 0x00;

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _kek;
    private readonly byte[]? _kekAnterior;

    public EnvelopeEncryptionService(IConfiguration configuration)
    {
        var raw = configuration["Certificados:ChaveMestraKEK"];
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Certificados:ChaveMestraKEK não configurada.");

        _kek = Convert.FromBase64String(raw);
        if (_kek.Length != 32)
            throw new InvalidOperationException("KEK deve ter 32 bytes (AES-256).");

        var rawAnterior = configuration["Certificados:ChaveMestraKEKAnterior"];
        if (!string.IsNullOrWhiteSpace(rawAnterior))
        {
            _kekAnterior = Convert.FromBase64String(rawAnterior);
            if (_kekAnterior.Length != 32)
                throw new InvalidOperationException("KEK anterior deve ter 32 bytes (AES-256).");
        }
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
        var envelope = Cifrar(System.Text.Encoding.UTF8.GetBytes(texto), _kek, nonce);
        return Task.FromResult(ComVersao(KidKekAtual, envelope));
    }

    public Task<string> DecifrarTextoAsync(byte[] dados, CancellationToken ct)
    {
        var (envelope, kek) = SemVersao(dados);
        var plain = Decifrar(envelope, kek);
        return Task.FromResult(System.Text.Encoding.UTF8.GetString(plain));
    }

    /// <summary>Prefixa versão+kid no material cifrado pela KEK.</summary>
    private static byte[] ComVersao(byte kid, byte[] envelopeKek)
    {
        var output = new byte[2 + envelopeKek.Length];
        output[0] = VersaoEnvelope;
        output[1] = kid;
        Buffer.BlockCopy(envelopeKek, 0, output, 2, envelopeKek.Length);
        return output;
    }

    /// <summary>Resolve qual KEK decifra (por prefixo de versão) e remove o prefixo.</summary>
    private (byte[] Envelope, byte[] Kek) SemVersao(byte[] dados)
    {
        if (dados.Length > 2 && dados[0] == VersaoEnvelope)
        {
            var kid = dados[1];
            var kek = kid == KidKekAnterior
                ? _kekAnterior
                    ?? throw new InvalidOperationException(
                        "Envelope usa a KEK anterior e Certificados:ChaveMestraKEKAnterior não está configurada.")
                : _kek;
            var envelope = new byte[dados.Length - 2];
            Buffer.BlockCopy(dados, 2, envelope, 0, envelope.Length);
            return (envelope, kek);
        }
        return (dados, _kek); // legado: sem prefixo, sempre a KEK atual
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

    private byte[] Decifrar(byte[] dados, byte[] dek)
    {
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[dados.Length - NonceSize - TagSize];
        Buffer.BlockCopy(dados, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(dados, NonceSize, cipher, 0, cipher.Length);
        Buffer.BlockCopy(dados, NonceSize + cipher.Length, tag, 0, TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(dek, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private byte[] CifrarDek(byte[] dek)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var envelope = Cifrar(dek, _kek, nonce);
        return ComVersao(KidKekAtual, envelope);
    }

    private byte[] DecifrarDek(byte[] envelopeComVersao)
    {
        var (envelope, kek) = SemVersao(envelopeComVersao);
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[envelope.Length - NonceSize - TagSize];
        Buffer.BlockCopy(envelope, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(envelope, NonceSize, cipher, 0, cipher.Length);
        Buffer.BlockCopy(envelope, NonceSize + cipher.Length, tag, 0, TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(kek, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
