using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Correção de bug de produção: key_hash é PBKDF2 (~91 chars no formato
    /// $pbkdf2-sha256$iter$salt$hash) mas a coluna era varchar(64) — criar
    /// API key via API falhava no Postgres (SQLite não valida tamanho, por
    /// isso os testes nunca pegaram). Largura 120 dá folga p/ mais iterações.
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260903021000_ApiKeyHashMaxLength")]
    public partial class ApiKeyHashMaxLength : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "key_hash",
                table: "api_keys",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "key_hash",
                table: "api_keys",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);
        }
    }
}
