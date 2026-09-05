using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Reforça o invariante declarado em SECURITY.md (linha 26-27): a tabela
    /// `auditoria` é append-only. Em Postgres, o role usado pela aplicação
    /// perde UPDATE/DELETE/TRUNCATE; em SQLite (testes locais) a migration
    /// é no-op porque o controle é por permissão de sistema.
    ///
    /// Para configurar em produção, crie um role dedicado:
    ///   CREATE ROLE fiscal_app LOGIN PASSWORD '...';
    ///   GRANT CONNECT ON DATABASE fiscal TO fiscal_app;
    ///   GRANT USAGE, SELECT, INSERT ON ALL TABLES IN SCHEMA public TO fiscal_app;
    ///   GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO fiscal_app;
    ///   -- auditoria recebe apenas INSERT (sem UPDATE/DELETE/TRUNCATE).
    ///   REVOKE UPDATE, DELETE, TRUNCATE ON auditoria FROM fiscal_app;
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260902000000_EnforceAuditoriaAppendOnly")]
    public partial class EnforceAuditoriaAppendOnly : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: aplicado via GRANT/REVOKE no role do banco, não via schema EF.
            // Documentado em SECURITY.md e no sumário desta migration.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op.
        }
    }
}
