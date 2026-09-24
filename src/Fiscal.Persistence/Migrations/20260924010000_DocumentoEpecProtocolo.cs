using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// documentos_fiscais.epec_protocolo: protocolo do evento EPEC (110140)
    /// autorizado pela SVRS na contingência EPEC — a NF-e completa é transmitida
    /// depois (janela de 168h). Non-null marca o EPEC como já enviado.
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260924010000_DocumentoEpecProtocolo")]
    public partial class DocumentoEpecProtocolo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "epec_protocolo",
                table: "documentos_fiscais",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "epec_protocolo",
                table: "documentos_fiscais");
        }
    }
}
