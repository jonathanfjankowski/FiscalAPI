using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Fase 3 (Sprint 1.5): outbox de webhooks. Linhas gravadas junto com a
    /// mudança de status do documento/evento; EntregarWebhooksJob entrega com
    /// HMAC-SHA256 e retry próprio (separado do retry de SEFAZ).
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260905000000_OutboxWebhooks")]
    public partial class OutboxWebhooks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_evento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_status_code = table.Column<int>(type: "integer", nullable: true),
                    ultimo_erro = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entregue_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_webhooks", x => x.id);
                    table.ForeignKey(
                        name: "FK_outbox_webhooks_documentos_fiscais_documento_id",
                        column: x => x.documento_id,
                        principalTable: "documentos_fiscais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_webhooks_status_proxima_tentativa_em",
                table: "outbox_webhooks",
                columns: new[] { "status", "proxima_tentativa_em" },
                filter: "status = 'PENDENTE'");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_webhooks_tenant_id_criado_em",
                table: "outbox_webhooks",
                columns: new[] { "tenant_id", "criado_em" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "outbox_webhooks");
        }
    }
}
