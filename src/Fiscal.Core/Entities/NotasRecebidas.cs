namespace Fiscal.Core.Entities;

/// <summary>
/// Nota recebida via Distribuição DFe (resumo resNFe ou procNFe completo).
/// </summary>
public class NotaRecebida
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public short Ambiente { get; set; }

    public string Chave { get; set; } = string.Empty;
    public string Nsu { get; set; } = string.Empty;
    public string TipoSchema { get; set; } = "resNFe"; // resNFe | procNFe
    public string XmlResumo { get; set; } = string.Empty;
    public string? XmlCompleto { get; set; }

    public string? CnpjEmitente { get; set; }
    public string? NomeEmitente { get; set; }
    public decimal? Valor { get; set; }
    public DateTimeOffset? EmitidaEm { get; set; }

    // ciencia | confirmacao | desconhecimento | nao_realizacao (última efetiva)
    public string? ManifestacaoAtual { get; set; }

    public DateTimeOffset RecebidaEm { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Manifestação do Destinatário sobre uma nota recebida.</summary>
public class ManifestacaoDestinatario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid NotaRecebidaId { get; set; }
    public NotaRecebida? NotaRecebida { get; set; }

    // 210200 (confirmação) | 210210 (ciência) | 210220 (desconhecimento) | 210240 (não realização)
    public string Tipo { get; set; } = string.Empty;
    public string? Justificativa { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    public string Status { get; set; } = "PENDENTE"; // PENDENTE | PROCESSANDO | PROCESSADO | REJEITADO
    public string? Protocolo { get; set; }
    public string? MotivoStatus { get; set; }
    public int Tentativas { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Último NSU processado por tenant + ambiente (PK composta).</summary>
public class NsuDistribuicao
{
    public Guid TenantId { get; set; }
    public short Ambiente { get; set; }
    public string UltimoNsu { get; set; } = "000000000000000";
}
