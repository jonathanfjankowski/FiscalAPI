using Fiscal.Core.Entities;
using Fiscal.Core.Enums;
using Fiscal.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Persistence.Repositories;

public class RepositorioTenant : IRepositorioTenant
{
    private readonly FiscalDbContext _db;
    public RepositorioTenant(FiscalDbContext db) => _db = db;

    public Task<Tenant?> ObterPorIdAsync(Guid id, CancellationToken ct) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task AdicionarAsync(Tenant tenant, CancellationToken ct) =>
        await _db.Tenants.AddAsync(tenant, ct);
}

public class RepositorioApiKey : IRepositorioApiKey
{
    private readonly FiscalDbContext _db;
    public RepositorioApiKey(FiscalDbContext db) => _db = db;

    public Task<ApiKey?> ObterPorHashAsync(string keyHash, CancellationToken ct) =>
        _db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == keyHash, ct);

    public Task<ApiKey?> ObterPorIdAsync(Guid id, CancellationToken ct) =>
        _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);

    public async Task<IReadOnlyList<ApiKey>> ListarPorTenantAsync(Guid tenantId, CancellationToken ct) =>
        await _db.ApiKeys.Where(k => k.TenantId == tenantId).ToListAsync(ct);

    public async Task<IReadOnlyList<ApiKey>> ListarPorPrefixoAsync(string prefixo, CancellationToken ct) =>
        await _db.ApiKeys.Where(k => k.Prefixo == prefixo && k.Ativa && k.RevogadoEm == null).ToListAsync(ct);

    public async Task AdicionarAsync(ApiKey apiKey, CancellationToken ct) =>
        await _db.ApiKeys.AddAsync(apiKey, ct);

    public async Task RevogarAsync(Guid id, CancellationToken ct)
    {
        var k = await _db.ApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (k is null) return;
        k.Ativa = false;
        k.RevogadoEm = DateTimeOffset.UtcNow;
    }
}

public class RepositorioCertificado : IRepositorioCertificado
{
    private readonly FiscalDbContext _db;
    public RepositorioCertificado(FiscalDbContext db) => _db = db;

    public Task<Certificado?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        _db.Certificados.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

    public Task<Certificado?> ObterAtivoPorTenantAsync(Guid tenantId, CancellationToken ct) =>
        _db.Certificados.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Ativo, ct);

    public async Task<IReadOnlyList<Certificado>> ListarPorTenantAsync(Guid tenantId, CancellationToken ct) =>
        await _db.Certificados.Where(c => c.TenantId == tenantId).ToListAsync(ct);

    public async Task AdicionarAsync(Certificado certificado, CancellationToken ct) =>
        await _db.Certificados.AddAsync(certificado, ct);
}

public class RepositorioDocumentoFiscal : IRepositorioDocumentoFiscal
{
    private readonly FiscalDbContext _db;
    public RepositorioDocumentoFiscal(FiscalDbContext db) => _db = db;

    public Task<DocumentoFiscal?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        _db.DocumentosFiscais.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, ct);

    public Task<DocumentoFiscal?> ObterPorIdempotencyKeyAsync(Guid tenantId, TipoDocumento tipo, string key, CancellationToken ct) =>
        _db.DocumentosFiscais.FirstOrDefaultAsync(
            d => d.TenantId == tenantId && d.Tipo == tipo && d.IdempotencyKey == key, ct);

    public Task<DocumentoFiscal?> ObterParaLockAsync(Guid id, CancellationToken ct) =>
        _db.DocumentosFiscais.FromSqlInterpolated($"SELECT * FROM documentos_fiscais WHERE id = {id} FOR UPDATE")
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

    public async Task AdicionarAsync(DocumentoFiscal doc, CancellationToken ct) =>
        await _db.DocumentosFiscais.AddAsync(doc, ct);

    public Task AtualizarAsync(DocumentoFiscal doc, CancellationToken ct)
    {
        _db.DocumentosFiscais.Update(doc);
        return Task.CompletedTask;
    }

    public async Task<long> ReservarProximoNumeroAsync(
        Guid tenantId, short modelo, short serie, short ambiente, CancellationToken ct)
    {
        // Postgres tem UPDATE...RETURNING; SQLite não. Dois caminhos.
        if (_db.Database.IsNpgsql())
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE sequencias_numeracao
                   SET ultimo_numero = ultimo_numero + 1
                 WHERE tenant_id = @tenant_id
                   AND modelo = @modelo
                   AND serie = @serie
                   AND ambiente = @ambiente
             RETURNING ultimo_numero;";
            cmd.Parameters.Add(new Npgsql.NpgsqlParameter("tenant_id", tenantId));
            cmd.Parameters.Add(new Npgsql.NpgsqlParameter("modelo", modelo));
            cmd.Parameters.Add(new Npgsql.NpgsqlParameter("serie", serie));
            cmd.Parameters.Add(new Npgsql.NpgsqlParameter("ambiente", ambiente));

            var result = await cmd.ExecuteScalarAsync(ct);
            if (result is not null and not DBNull)
                return Convert.ToInt64(result);
        }
        else
        {
            // Caminho SQLite (in-memory em testes). Read-then-write sob transação
            // do próprio SaveChangesAsync — não é tão seguro quanto o lock do
            // Postgres, mas é o suficiente para testes de integração single-thread.
            var existente = await _db.SequenciasNumeracao.FirstOrDefaultAsync(
                s => s.TenantId == tenantId && s.Modelo == modelo && s.Serie == serie && s.Ambiente == ambiente, ct);
            if (existente is not null)
            {
                existente.UltimoNumero += 1;
                return existente.UltimoNumero;
            }
        }

        // Sem sequência ainda — cria com último 1 e retorna 1.
        var novo = new SequenciaNumeracao
        {
            TenantId = tenantId,
            Modelo = modelo,
            Serie = serie,
            Ambiente = ambiente,
            UltimoNumero = 1
        };
        await _db.SequenciasNumeracao.AddAsync(novo, ct);
        return 1;
    }

    public Task<bool> ExisteNumeroAsync(Guid tenantId, short modelo, short serie, long numero, CancellationToken ct) =>
        _db.DocumentosFiscais.AnyAsync(
            d => d.TenantId == tenantId && d.Modelo == modelo && d.Serie == serie && d.Numero == numero, ct);
}

public class RepositorioAuditoria : IRepositorioAuditoria
{
    private readonly FiscalDbContext _db;
    public RepositorioAuditoria(FiscalDbContext db) => _db = db;

    public async Task RegistrarAsync(Auditoria auditoria, CancellationToken ct) =>
        await _db.Auditoria.AddAsync(auditoria, ct);
}

public class RepositorioEventoFiscal : IRepositorioEventoFiscal
{
    private readonly FiscalDbContext _db;
    public RepositorioEventoFiscal(FiscalDbContext db) => _db = db;

    public Task<EventoFiscal?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        _db.EventosFiscais.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId, ct);

    // documentoId null (inutilização) casa com linhas documento_id IS NULL —
    // o EF Core reescreve a comparação quando o parâmetro chega nulo.
    public Task<EventoFiscal?> ObterPorIdempotencyAsync(
        Guid tenantId, Guid? documentoId, string tipoEvento, string idempotencyKey, CancellationToken ct) =>
        _db.EventosFiscais.FirstOrDefaultAsync(
            e => e.TenantId == tenantId
                 && e.DocumentoId == documentoId
                 && e.TipoEvento == tipoEvento
                 && e.IdempotencyKey == idempotencyKey, ct);

    public async Task AdicionarAsync(EventoFiscal evento, CancellationToken ct) =>
        await _db.EventosFiscais.AddAsync(evento, ct);

    public Task AtualizarAsync(EventoFiscal evento, CancellationToken ct)
    {
        _db.EventosFiscais.Update(evento);
        return Task.CompletedTask;
    }
}

public class RepositorioNotaRecebida : IRepositorioNotaRecebida
{
    private readonly FiscalDbContext _db;
    public RepositorioNotaRecebida(FiscalDbContext db) => _db = db;

    public Task<NotaRecebida?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        _db.NotasRecebidas.FirstOrDefaultAsync(n => n.Id == id && n.TenantId == tenantId, ct);

    public Task<NotaRecebida?> ObterPorChaveAsync(Guid tenantId, string chave, CancellationToken ct) =>
        _db.NotasRecebidas.FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Chave == chave, ct);

    public async Task<IReadOnlyList<NotaRecebida>> ListarPorTenantAsync(Guid tenantId, int limite, CancellationToken ct)
    {
        // ORDER BY DateTimeOffset não é suportado no SQLite (testes) — ordena em memória.
        var recentes = await _db.NotasRecebidas
            .Where(n => n.TenantId == tenantId)
            .Take(1000)
            .ToListAsync(ct);
        return recentes.OrderByDescending(n => n.RecebidaEm).Take(limite).ToList();
    }

    public async Task AdicionarAsync(NotaRecebida nota, CancellationToken ct) =>
        await _db.NotasRecebidas.AddAsync(nota, ct);

    public Task AtualizarAsync(NotaRecebida nota, CancellationToken ct)
    {
        _db.NotasRecebidas.Update(nota);
        return Task.CompletedTask;
    }
}

public class RepositorioManifestacao : IRepositorioManifestacao
{
    private readonly FiscalDbContext _db;
    public RepositorioManifestacao(FiscalDbContext db) => _db = db;

    public Task<ManifestacaoDestinatario?> ObterPorIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        _db.Manifestacoes.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);

    public Task<ManifestacaoDestinatario?> ObterPorIdempotencyAsync(
        Guid tenantId, Guid notaId, string idempotencyKey, CancellationToken ct) =>
        _db.Manifestacoes.FirstOrDefaultAsync(
            m => m.TenantId == tenantId && m.NotaRecebidaId == notaId && m.IdempotencyKey == idempotencyKey, ct);

    public async Task<IReadOnlyList<ManifestacaoDestinatario>> ListarVencidasAsync(DateTimeOffset agora, int limite, CancellationToken ct) =>
        await _db.Manifestacoes
            .Where(m => m.Status == "PENDENTE")
            .OrderBy(m => m.CriadoEm)
            .Take(limite)
            .ToListAsync(ct);

    public async Task AdicionarAsync(ManifestacaoDestinatario manifestacao, CancellationToken ct) =>
        await _db.Manifestacoes.AddAsync(manifestacao, ct);

    public Task AtualizarAsync(ManifestacaoDestinatario manifestacao, CancellationToken ct)
    {
        _db.Manifestacoes.Update(manifestacao);
        return Task.CompletedTask;
    }
}

public class RepositorioNsu : IRepositorioNsu
{
    private readonly FiscalDbContext _db;
    public RepositorioNsu(FiscalDbContext db) => _db = db;

    public Task<NsuDistribuicao?> ObterAsync(Guid tenantId, short ambiente, CancellationToken ct) =>
        _db.NsuDistribuicao.FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Ambiente == ambiente, ct);

    public async Task SalvarAsync(NsuDistribuicao nsu, CancellationToken ct)
    {
        var existente = await _db.NsuDistribuicao.FindAsync(new object[] { nsu.TenantId, nsu.Ambiente }, ct);
        if (existente is null)
            await _db.NsuDistribuicao.AddAsync(nsu, ct);
        else
            existente.UltimoNsu = nsu.UltimoNsu;
    }
}
