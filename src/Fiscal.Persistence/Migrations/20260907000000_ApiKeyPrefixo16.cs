using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Segurança/assinatura: prefixo de API key passa de 8 para 16 chars.
    /// Com 8 chars o prefixo era constante ("fk_live_"/"fk_test_"), e o lookup
    /// por prefixo devolvia TODAS as chaves ativas do ambiente — cada request
    /// acabava verificando PBKDF2 de todas. Com 16 chars o prefixo identifica
    /// a chave (ou um conjunto mínimo).
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260907000000_ApiKeyPrefixo16")]
    public partial class ApiKeyPrefixo16 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "prefixo",
                table: "api_keys",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "prefixo",
                table: "api_keys",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);
        }
    }
}
