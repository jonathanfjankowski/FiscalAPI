using Fiscal.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fiscal.Persistence;

public class FiscalDbContext : DbContext
{
    public FiscalDbContext(DbContextOptions<FiscalDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Certificado> Certificados => Set<Certificado>();
    public DbSet<DocumentoFiscal> DocumentosFiscais => Set<DocumentoFiscal>();
    public DbSet<SequenciaNumeracao> SequenciasNumeracao => Set<SequenciaNumeracao>();
    public DbSet<EventoFiscal> EventosFiscais => Set<EventoFiscal>();
    public DbSet<WebhookEntrega> WebhooksEntrega => Set<WebhookEntrega>();
    public DbSet<NotaRecebida> NotasRecebidas => Set<NotaRecebida>();
    public DbSet<ManifestacaoDestinatario> Manifestacoes => Set<ManifestacaoDestinatario>();
    public DbSet<NsuDistribuicao> NsuDistribuicao => Set<NsuDistribuicao>();
    public DbSet<Auditoria> Auditoria => Set<Auditoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Cnpj).HasColumnName("cnpj").HasMaxLength(14).IsRequired();
            b.Property(x => x.RazaoSocial).HasColumnName("razao_social").HasMaxLength(200).IsRequired();
            b.Property(x => x.Uf).HasColumnName("uf").HasMaxLength(2).IsRequired();
            b.Property(x => x.CodigoMunicipioIbge).HasColumnName("codigo_municipio_ibge").HasMaxLength(7);
            b.Property(x => x.RegimeTributario).HasColumnName("regime_tributario");
            b.Property(x => x.AmbientePadrao).HasColumnName("ambiente_padrao");
            b.Property(x => x.InscricaoEstadual).HasColumnName("inscricao_estadual").HasMaxLength(20);
            b.Property(x => x.InscricaoMunicipal).HasColumnName("inscricao_municipal").HasMaxLength(20);
            b.Property(x => x.Logradouro).HasColumnName("logradouro").HasMaxLength(100);
            b.Property(x => x.Numero).HasColumnName("numero").HasMaxLength(10);
            b.Property(x => x.Complemento).HasColumnName("complemento").HasMaxLength(100);
            b.Property(x => x.Bairro).HasColumnName("bairro").HasMaxLength(100);
            b.Property(x => x.Cep).HasColumnName("cep").HasMaxLength(8);
            b.Property(x => x.NomeMunicipio).HasColumnName("nome_municipio").HasMaxLength(100);
            b.Property(x => x.CscId).HasColumnName("csc_id").HasMaxLength(10);
            b.Property(x => x.CscCriptografado).HasColumnName("csc_criptografado");
            b.Property(x => x.WebhookUrl).HasColumnName("webhook_url");
            b.Property(x => x.WebhookSecret).HasColumnName("webhook_secret");
            b.Property(x => x.WebhookSecretCriptografado).HasColumnName("webhook_secret_criptografado");
            b.Property(x => x.Ativo).HasColumnName("ativo");
            b.Property(x => x.CriadoEm).HasColumnName("criado_em");
            b.HasIndex(x => x.Cnpj).IsUnique();
        });

        modelBuilder.Entity<ApiKey>(b =>
        {
            b.ToTable("api_keys");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.Prefixo).HasColumnName("prefixo").HasMaxLength(16).IsRequired();
            b.Property(x => x.KeyHash).HasColumnName("key_hash").HasMaxLength(120).IsRequired();
            b.Property(x => x.Descricao).HasColumnName("descricao");
            b.Property(x => x.Ambiente).HasColumnName("ambiente");
            b.Property(x => x.Ativa).HasColumnName("ativa");
            b.Property(x => x.CriadoEm).HasColumnName("criado_em");
            b.Property(x => x.RevogadoEm).HasColumnName("revogado_em");
            b.HasOne(x => x.Tenant).WithMany(t => t.ApiKeys).HasForeignKey(x => x.TenantId);
            b.HasIndex(x => x.KeyHash).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Ativa });
        });

        modelBuilder.Entity<Certificado>(b =>
        {
            b.ToTable("certificados");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.PfxCriptografado).HasColumnName("pfx_criptografado").IsRequired();
            b.Property(x => x.SenhaCriptografada).HasColumnName("senha_criptografada").IsRequired();
            b.Property(x => x.ChaveDekCriptografada).HasColumnName("chave_dek_criptografada").IsRequired();
            b.Property(x => x.Thumbprint).HasColumnName("thumbprint").HasMaxLength(64).IsRequired();
            b.Property(x => x.ValidoAte).HasColumnName("valido_ate");
            b.Property(x => x.Ativo).HasColumnName("ativo");
            b.Property(x => x.CriadoEm).HasColumnName("criado_em");
            b.HasOne(x => x.Tenant).WithMany(t => t.Certificados).HasForeignKey(x => x.TenantId);
            b.HasIndex(x => new { x.TenantId, x.Thumbprint }).IsUnique();
            b.HasIndex(x => x.ValidoAte).HasFilter("ativo = true");
        });

        modelBuilder.Entity<DocumentoFiscal>(b =>
        {
            b.ToTable("documentos_fiscais");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
            b.Property(x => x.Tipo).HasColumnName("tipo").HasConversion<string>().HasMaxLength(10).IsRequired();
            b.Property(x => x.Ambiente).HasColumnName("ambiente");
            b.Property(x => x.Modelo).HasColumnName("modelo");
            b.Property(x => x.Serie).HasColumnName("serie");
            b.Property(x => x.Numero).HasColumnName("numero");
            b.Property(x => x.ChaveAcesso).HasColumnName("chave_acesso").HasMaxLength(44);
            b.Property(x => x.PayloadEntrada).HasColumnName("payload_entrada").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.XmlGerado).HasColumnName("xml_gerado");
            b.Property(x => x.XmlAssinado).HasColumnName("xml_assinado");
            b.Property(x => x.XmlRetornoSefaz).HasColumnName("xml_retorno_sefaz");
            b.Property(x => x.ProtocoloAutorizacao).HasColumnName("protocolo_autorizacao").HasMaxLength(20);
            b.Property(x => x.ReciboLote).HasColumnName("recibo_lote").HasMaxLength(20);
            b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(x => x.MotivoStatus).HasColumnName("motivo_status");
            b.Property(x => x.Tentativas).HasColumnName("tentativas");
            b.Property(x => x.ModoContingencia).HasColumnName("modo_contingencia").HasMaxLength(20);
            b.Property(x => x.ProximaTentativaEm).HasColumnName("proxima_tentativa_em");
            b.Property(x => x.CriadoEm).HasColumnName("criado_em");
            b.Property(x => x.AtualizadoEm).HasColumnName("atualizado_em");
            b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);

            // errata §3.4: inclui `tipo` na idempotência
            b.HasIndex(x => new { x.TenantId, x.Tipo, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Modelo, x.Serie, x.Numero }).IsUnique();
            b.HasIndex(x => new { x.Status, x.ProximaTentativaEm })
                .HasFilter("status IN ('PENDENTE','CONTINGENCIA')");
            b.HasIndex(x => new { x.TenantId, x.CriadoEm });
        });

        modelBuilder.Entity<SequenciaNumeracao>(b =>
        {
            b.ToTable("sequencias_numeracao");
            b.HasKey(x => new { x.TenantId, x.Modelo, x.Serie, x.Ambiente });
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.Modelo).HasColumnName("modelo");
            b.Property(x => x.Serie).HasColumnName("serie");
            b.Property(x => x.Ambiente).HasColumnName("ambiente");
            b.Property(x => x.UltimoNumero).HasColumnName("ultimo_numero");
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        });

        modelBuilder.Entity<EventoFiscal>(b =>
        {
            b.ToTable("eventos_fiscais");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.DocumentoId).HasColumnName("documento_id");
            b.Property(x => x.TipoEvento).HasColumnName("tipo_evento").HasMaxLength(30).IsRequired();
            b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
            b.Property(x => x.Justificativa).HasColumnName("justificativa");
            b.Property(x => x.DadosEvento).HasColumnName("dados_evento").HasColumnType("jsonb");
            b.Property(x => x.XmlRetorno).HasColumnName("xml_retorno");
            b.Property(x => x.Protocolo).HasColumnName("protocolo").HasMaxLength(20);
            b.Property(x => x.MotivoStatus).HasColumnName("motivo_status");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            b.Property(x => x.Tentativas).HasColumnName("tentativas");
            b.Property(x => x.ProximaTentativaEm).HasColumnName("proxima_tentativa_em");
            b.Property(x => x.CriadoEm).HasColumnName("criado_em");
            // DocumentoId é nullable: inutilização não amarra a documento (FK opcional).
            b.HasOne(x => x.Documento).WithMany().HasForeignKey(x => x.DocumentoId);
            b.HasIndex(x => new { x.DocumentoId, x.TipoEvento, x.IdempotencyKey }).IsUnique();
            // Idempotência da inutilização — o composite acima não cobre linhas com
            // documento_id NULL (Postgres trata NULL como distinto).
            b.HasIndex(x => new { x.TenantId, x.TipoEvento, x.IdempotencyKey })
                .IsUnique()
                .HasFilter("documento_id IS NULL");
            // VarrerEventosJob: eventos PENDENTES cuja próxima tentativa venceu.
            b.HasIndex(x => new { x.Status, x.ProximaTentativaEm })
                .HasFilter("status = 'PENDENTE'");
        });

        modelBuilder.Entity<AdminUser>(b =>
        {
            b.ToTable("admin_users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Email).HasColumnName("email").HasMaxLength(200).IsRequired();
            b.Property(x => x.SenhaHash).HasColumnName("senha_hash").HasMaxLength(500).IsRequired();
            b.Property(x => x.Ativo).HasColumnName("ativo");
            b.Property(x => x.CriadoEm).HasColumnName("criado_em");
            b.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Auditoria>(b =>
        {
            b.ToTable("auditoria");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.ApiKeyId).HasColumnName("api_key_id");
            b.Property(x => x.Acao).HasColumnName("acao").HasMaxLength(50).IsRequired();
            b.Property(x => x.RecursoId).HasColumnName("recurso_id");
            b.Property(x => x.IpOrigem).HasColumnName("ip_origem").HasColumnType("inet");
            b.Property(x => x.Detalhe).HasColumnName("detalhe").HasColumnType("jsonb");
            b.Property(x => x.CriadoEm).HasColumnName("criado_em");
            b.HasIndex(x => new { x.TenantId, x.CriadoEm });
        });

        modelBuilder.Entity<WebhookEntrega>(b =>
        {
            b.ToTable("outbox_webhooks");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.DocumentoId).HasColumnName("documento_id");
            b.Property(x => x.TipoEvento).HasColumnName("tipo_evento").HasMaxLength(40).IsRequired();
            b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            b.Property(x => x.Tentativas).HasColumnName("tentativas");
            b.Property(x => x.ProximaTentativaEm).HasColumnName("proxima_tentativa_em");
            b.Property(x => x.UltimoStatusCode).HasColumnName("ultimo_status_code");
            b.Property(x => x.UltimoErro).HasColumnName("ultimo_erro");
            b.Property(x => x.CriadoEm).HasColumnName("criado_em");
            b.Property(x => x.EntregueEm).HasColumnName("entregue_em");
            b.HasOne(x => x.Documento).WithMany().HasForeignKey(x => x.DocumentoId);
            // VarrerWebhooksJob: entregas PENDENTES cuja próxima tentativa venceu.
            b.HasIndex(x => new { x.Status, x.ProximaTentativaEm })
                .HasFilter("status = 'PENDENTE'");
            b.HasIndex(x => new { x.TenantId, x.CriadoEm });
        });

        modelBuilder.Entity<NotaRecebida>(b =>
        {
            b.ToTable("notas_recebidas");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.Ambiente).HasColumnName("ambiente");
            b.Property(x => x.Chave).HasColumnName("chave").HasMaxLength(44).IsRequired();
            b.Property(x => x.Nsu).HasColumnName("nsu").HasMaxLength(15).IsRequired();
            b.Property(x => x.TipoSchema).HasColumnName("tipo_schema").HasMaxLength(20).IsRequired();
            b.Property(x => x.XmlResumo).HasColumnName("xml_resumo").IsRequired();
            b.Property(x => x.XmlCompleto).HasColumnName("xml_completo");
            b.Property(x => x.CnpjEmitente).HasColumnName("cnpj_emitente").HasMaxLength(14);
            b.Property(x => x.NomeEmitente).HasColumnName("nome_emitente").HasMaxLength(200);
            b.Property(x => x.Valor).HasColumnName("valor").HasColumnType("numeric(15,2)");
            b.Property(x => x.EmitidaEm).HasColumnName("emitida_em");
            b.Property(x => x.ManifestacaoAtual).HasColumnName("manifestacao_atual").HasMaxLength(30);
            b.Property(x => x.RecebidaEm).HasColumnName("recebida_em");
            b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
            b.HasIndex(x => new { x.TenantId, x.Chave }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.RecebidaEm });
        });

        modelBuilder.Entity<ManifestacaoDestinatario>(b =>
        {
            b.ToTable("manifestacoes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.NotaRecebidaId).HasColumnName("nota_recebida_id");
            b.Property(x => x.Tipo).HasColumnName("tipo").HasMaxLength(6).IsRequired();
            b.Property(x => x.Justificativa).HasColumnName("justificativa");
            b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            b.Property(x => x.Protocolo).HasColumnName("protocolo").HasMaxLength(20);
            b.Property(x => x.MotivoStatus).HasColumnName("motivo_status");
            b.Property(x => x.Tentativas).HasColumnName("tentativas");
            b.Property(x => x.CriadoEm).HasColumnName("criado_em");
            b.HasOne(x => x.NotaRecebida).WithMany().HasForeignKey(x => x.NotaRecebidaId);
            b.HasIndex(x => new { x.TenantId, x.NotaRecebidaId, x.IdempotencyKey }).IsUnique();
        });

        modelBuilder.Entity<NsuDistribuicao>(b =>
        {
            b.ToTable("ultimo_nsu");
            b.HasKey(x => new { x.TenantId, x.Ambiente });
            b.Property(x => x.TenantId).HasColumnName("tenant_id");
            b.Property(x => x.Ambiente).HasColumnName("ambiente");
            b.Property(x => x.UltimoNsu).HasColumnName("ultimo_nsu").HasMaxLength(15).IsRequired();
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        });
    }
}
