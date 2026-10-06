using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Sandbox por tenant (tenants.sandbox, default true — novos tenants herdam
    /// de Fiscal:ModoSandbox no momento da criação) + chave bootstrap persistida
    /// (chave_bootstrap) para o provisionamento do ERP com rotação pelo painel.
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20261006000000_TenantSandboxChaveBootstrap")]
    public partial class TenantSandboxChaveBootstrap : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "sandbox",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "chave_bootstrap",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prefixo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chave_bootstrap", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chave_bootstrap");

            migrationBuilder.DropColumn(
                name: "sandbox",
                table: "tenants");
        }
    }
}
