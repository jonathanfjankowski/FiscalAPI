using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Tabela de sobrescrita dos prazos de cancelamento (NF-e 55 e NFC-e 65)
    /// por UF — permite editar no painel admin quando alguma SEFAZ alterar o
    /// prazo legal sem precisar de deploy.
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20261127000000_PrazosCancelamentoUf")]
    public partial class PrazosCancelamentoUf : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prazos_cancelamento",
                columns: table => new
                {
                    uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    modelo = table.Column<short>(type: "smallint", nullable: false),
                    prazo_minutos = table.Column<int>(type: "integer", nullable: false),
                    observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prazos_cancelamento", x => new { x.uf, x.modelo });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prazos_cancelamento");
        }
    }
}
