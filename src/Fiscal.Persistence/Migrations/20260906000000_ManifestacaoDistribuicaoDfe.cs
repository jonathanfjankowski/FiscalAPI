using Fiscal.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <summary>
    /// Fase 3 (Sprint 1.7): Distribuição DFe + Manifestação do Destinatário.
    /// notas_recebidas (resumo/proc por chave), manifestacoes (ciclo de
    /// transmissão com idempotência) e ultimo_nsu (marca de posição por
    /// tenant + ambiente).
    /// </summary>
    [DbContext(typeof(FiscalDbContext))]
    [Migration("20260906000000_ManifestacaoDistribuicaoDfe")]
    public partial class ManifestacaoDistribuicaoDfe : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notas_recebidas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambiente = table.Column<short>(type: "smallint", nullable: false),
                    chave = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    nsu = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    tipo_schema = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xml_resumo = table.Column<string>(type: "text", nullable: false),
                    xml_completo = table.Column<string>(type: "text", nullable: true),
                    cnpj_emitente = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    nome_emitente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    valor = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    emitida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    manifestacao_atual = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    recebida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notas_recebidas", x => x.id);
                    table.ForeignKey(
                        name: "FK_notas_recebidas_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manifestacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_recebida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    justificativa = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    protocolo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    motivo_status = table.Column<string>(type: "text", nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manifestacoes", x => x.id);
                    table.ForeignKey(
                        name: "FK_manifestacoes_notas_recebidas_nota_recebida_id",
                        column: x => x.nota_recebida_id,
                        principalTable: "notas_recebidas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ultimo_nsu",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambiente = table.Column<short>(type: "smallint", nullable: false),
                    ultimo_nsu = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ultimo_nsu", x => new { x.tenant_id, x.ambiente });
                    table.ForeignKey(
                        name: "FK_ultimo_nsu_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notas_recebidas_tenant_id_chave",
                table: "notas_recebidas",
                columns: new[] { "tenant_id", "chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notas_recebidas_tenant_id_recebida_em",
                table: "notas_recebidas",
                columns: new[] { "tenant_id", "recebida_em" });

            migrationBuilder.CreateIndex(
                name: "IX_manifestacoes_tenant_id_nota_recebida_id_idempotency_key",
                table: "manifestacoes",
                columns: new[] { "tenant_id", "nota_recebida_id", "idempotency_key" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "manifestacoes");
            migrationBuilder.DropTable(name: "notas_recebidas");
            migrationBuilder.DropTable(name: "ultimo_nsu");
        }
    }
}
