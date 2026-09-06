using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Segurança (docs/revisao-seguranca.md §Pendências 2): o webhook_secret
    /// do tenant passa a ser cifrado em repouso com o mesmo envelope AES-GCM
    /// (DEK por registro + KEK) usado pelo CSC e pelos certificados. Valores
    /// legados em texto plano são migrados em voo pelo ProcessarWebhookJob e
    /// a coluna antiga é esvaziada.
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260908000000_WebhookSecretCriptografado")]
    public partial class WebhookSecretCriptografado : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "webhook_secret_criptografado",
                table: "tenants",
                type: "bytea",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "webhook_secret_criptografado",
                table: "tenants");
        }
    }
}
