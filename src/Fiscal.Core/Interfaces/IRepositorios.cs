using Fiscal.Core.Entities;
using Fiscal.Core.Enums;

namespace Fiscal.Core.Interfaces;

public interface IRepositorioTenant
{
    Task<Tenant?> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(Tenant tenant, CancellationToken ct);
}

public interface IRepositorioApiKey
{
    Task<ApiKey?> ObterPorHashAsync(string keyHash, CancellationToken ct);
    Task<ApiKey?> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ApiKey>> ListarPorTenantAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<ApiKey>> ListarPorPrefixoAsync(string prefixo, CancellationToken ct);
    Task AdicionarAsync(ApiKey apiKey, CancellationToken ct);
    Task RevogarAsync(Guid id, CancellationToken ct);
}

public interface IRepositorioCertificado
{
    Task<Certificado?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<Certificado?> ObterAtivoPorTenantAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<Certificado>> ListarPorTenantAsync(Guid tenantId, CancellationToken ct);
    Task AdicionarAsync(Certificado certificado, CancellationToken ct);
}

public interface IRepositorioDocumentoFiscal
{
    Task<DocumentoFiscal?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<DocumentoFiscal?> ObterPorIdempotencyKeyAsync(Guid tenantId, TipoDocumento tipo, string key, CancellationToken ct);
    Task<DocumentoFiscal?> ObterParaLockAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(DocumentoFiscal doc, CancellationToken ct);
    Task AtualizarAsync(DocumentoFiscal doc, CancellationToken ct);
    Task<long> ReservarProximoNumeroAsync(Guid tenantId, short modelo, short serie, short ambiente, CancellationToken ct);
    Task<bool> ExisteNumeroAsync(Guid tenantId, short modelo, short serie, long numero, CancellationToken ct);
}

public interface IRepositorioAuditoria
{
    Task RegistrarAsync(Auditoria auditoria, CancellationToken ct);
}

public interface IRepositorioEventoFiscal
{
    Task<EventoFiscal?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<EventoFiscal?> ObterPorIdempotencyAsync(
        Guid tenantId, Guid? documentoId, string tipoEvento, string idempotencyKey, CancellationToken ct);
    Task AdicionarAsync(EventoFiscal evento, CancellationToken ct);
    Task AtualizarAsync(EventoFiscal evento, CancellationToken ct);
}

public interface IRepositorioNotaRecebida
{
    Task<NotaRecebida?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<NotaRecebida?> ObterPorChaveAsync(Guid tenantId, string chave, CancellationToken ct);
    Task<IReadOnlyList<NotaRecebida>> ListarPorTenantAsync(Guid tenantId, int limite, CancellationToken ct);
    Task AdicionarAsync(NotaRecebida nota, CancellationToken ct);
    Task AtualizarAsync(NotaRecebida nota, CancellationToken ct);
}

public interface IRepositorioManifestacao
{
    Task<ManifestacaoDestinatario?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<ManifestacaoDestinatario?> ObterPorIdempotencyAsync(Guid tenantId, Guid notaId, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<ManifestacaoDestinatario>> ListarVencidasAsync(DateTimeOffset agora, int limite, CancellationToken ct);
    Task AdicionarAsync(ManifestacaoDestinatario manifestacao, CancellationToken ct);
    Task AtualizarAsync(ManifestacaoDestinatario manifestacao, CancellationToken ct);
}

public interface IRepositorioNsu
{
    Task<NsuDistribuicao?> ObterAsync(Guid tenantId, short ambiente, CancellationToken ct);
    Task SalvarAsync(NsuDistribuicao nsu, CancellationToken ct);
}
