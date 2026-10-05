using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// manifestacoes.proxima_tentativa_em: backoff das retentativas de
    /// transmissão (VarrerManifestacoesJob deixava de reenfileirar a cada 30 s)
    /// e lease da execução em PROCESSANDO (resgate de órfãos após crash).
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260924000000_ManifestacaoProximaTentativa")]
    public partial class ManifestacaoProximaTentativa : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "proxima_tentativa_em",
                table: "manifestacoes",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "proxima_tentativa_em",
                table: "manifestacoes");
        }
    }
}
