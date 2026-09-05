using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Transmissão de eventos à SEFAZ (Sprint 1.4): protocolo/motivo/tentativas
    /// para o ciclo PENDENTE → PROCESSANDO → PROCESSADO/REJEITADO/ERRO e
    /// dados_evento (jsonb) para carregar os parâmetros da inutilização
    /// (modelo/serie/faixa/ambiente), que não amarram a um documento.
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260904000000_EventosTransmissao")]
    public partial class EventosTransmissao : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dados_evento",
                table: "eventos_fiscais",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "protocolo",
                table: "eventos_fiscais",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_status",
                table: "eventos_fiscais",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tentativas",
                table: "eventos_fiscais",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "proxima_tentativa_em",
                table: "eventos_fiscais",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_eventos_fiscais_status_proxima_tentativa_em",
                table: "eventos_fiscais",
                columns: new[] { "status", "proxima_tentativa_em" },
                filter: "status = 'PENDENTE'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_eventos_fiscais_status_proxima_tentativa_em",
                table: "eventos_fiscais");

            migrationBuilder.DropColumn(name: "proxima_tentativa_em", table: "eventos_fiscais");
            migrationBuilder.DropColumn(name: "tentativas", table: "eventos_fiscais");
            migrationBuilder.DropColumn(name: "motivo_status", table: "eventos_fiscais");
            migrationBuilder.DropColumn(name: "protocolo", table: "eventos_fiscais");
            migrationBuilder.DropColumn(name: "dados_evento", table: "eventos_fiscais");
        }
    }
}
