using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Sprint 1.3 (integração real Unimake): dados do emitente por tenant
    /// (exigidos pelo grupo emit/enderEmit da NFe), CSC/IdCSC da NFC-e
    /// (csc_criptografado via KEK) e recibo_lote em documentos_fiscais
    /// (fluxo assíncrono NFeAutorizacao → NFeRetAutorizacao). Também ajusta
    /// eventos_fiscais: tenant_id (isolamento) e documento_id nullable
    /// (inutilização não amarra a documento).
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260903000000_PerfilFiscalEmitenteCscReciboLote")]
    public partial class PerfilFiscalEmitenteCscReciboLote : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "inscricao_estadual",
                table: "tenants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logradouro",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero",
                table: "tenants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "complemento",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bairro",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cep",
                table: "tenants",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nome_municipio",
                table: "tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "csc_id",
                table: "tenants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "csc_criptografado",
                table: "tenants",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recibo_lote",
                table: "documentos_fiscais",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // eventos_fiscais: tenant para isolamento/consulta; documento_id vira
            // nullable porque a inutilização não amarra a um documento (antes usava
            // Guid.Empty, que violava a FK para documentos_fiscais em Postgres).
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "eventos_fiscais",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "documento_id",
                table: "eventos_fiscais",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_eventos_fiscais_tenant_id_tipo_evento_idempotency_key",
                table: "eventos_fiscais",
                columns: new[] { "tenant_id", "tipo_evento", "idempotency_key" },
                unique: true,
                filter: "documento_id IS NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_eventos_fiscais_tenant_id_tipo_evento_idempotency_key",
                table: "eventos_fiscais");

            migrationBuilder.DropColumn(name: "inscricao_estadual", table: "tenants");
            migrationBuilder.DropColumn(name: "logradouro", table: "tenants");
            migrationBuilder.DropColumn(name: "numero", table: "tenants");
            migrationBuilder.DropColumn(name: "complemento", table: "tenants");
            migrationBuilder.DropColumn(name: "bairro", table: "tenants");
            migrationBuilder.DropColumn(name: "cep", table: "tenants");
            migrationBuilder.DropColumn(name: "nome_municipio", table: "tenants");
            migrationBuilder.DropColumn(name: "csc_id", table: "tenants");
            migrationBuilder.DropColumn(name: "csc_criptografado", table: "tenants");
            migrationBuilder.DropColumn(name: "recibo_lote", table: "documentos_fiscais");
            migrationBuilder.DropColumn(name: "tenant_id", table: "eventos_fiscais");

            migrationBuilder.AlterColumn<Guid>(
                name: "documento_id",
                table: "eventos_fiscais",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
