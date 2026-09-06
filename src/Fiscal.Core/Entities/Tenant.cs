using Fiscal.Core.Enums;

namespace Fiscal.Core.Entities;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Cnpj { get; set; } = string.Empty;
    public string RazaoSocial { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public string? CodigoMunicipioIbge { get; set; }
    public short RegimeTributario { get; set; }
    public short AmbientePadrao { get; set; } = (short)Ambiente.Homologacao;

    // Dados do emitente exigidos pelo layout da NFe (grupo emit/enderEmit).
    public string? InscricaoEstadual { get; set; }
    public string? Logradouro { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cep { get; set; }
    public string? NomeMunicipio { get; set; }

    // CSC/IdCSC da NFC-e (emitido pela SEFAZ por tenant). O CSC é sensível —
    // fica cifrado com a KEK (EnvelopeEncryptionService).
    public string? CscId { get; set; }
    public byte[]? CscCriptografado { get; set; }

    public string? WebhookUrl { get; set; }

    /// <summary>Legado (texto plano). Novos segredos vão para WebhookSecretCriptografado;
    /// o job de entrega migra em voo e limpa esta coluna.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Segredo HMAC dos webhooks, cifrado com a KEK (EnvelopeEncryptionService).</summary>
    public byte[]? WebhookSecretCriptografado { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
    public ICollection<Certificado> Certificados { get; set; } = new List<Certificado>();
}
