using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auditoria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    recurso_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip_origem = table.Column<IPAddress>(type: "inet", nullable: true),
                    detalhe = table.Column<string>(type: "jsonb", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auditoria", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    razao_social = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    codigo_municipio_ibge = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    regime_tributario = table.Column<short>(type: "smallint", nullable: false),
                    ambiente_padrao = table.Column<short>(type: "smallint", nullable: false),
                    webhook_url = table.Column<string>(type: "text", nullable: true),
                    webhook_secret = table.Column<string>(type: "text", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prefixo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: true),
                    ambiente = table.Column<short>(type: "smallint", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revogado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "FK_api_keys_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "certificados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfx_criptografado = table.Column<byte[]>(type: "bytea", nullable: false),
                    senha_criptografada = table.Column<byte[]>(type: "bytea", nullable: false),
                    chave_dek_criptografada = table.Column<byte[]>(type: "bytea", nullable: false),
                    thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    valido_ate = table.Column<DateOnly>(type: "date", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificados", x => x.id);
                    table.ForeignKey(
                        name: "FK_certificados_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documentos_fiscais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ambiente = table.Column<short>(type: "smallint", nullable: false),
                    modelo = table.Column<short>(type: "smallint", nullable: true),
                    serie = table.Column<short>(type: "smallint", nullable: true),
                    numero = table.Column<long>(type: "bigint", nullable: true),
                    chave_acesso = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: true),
                    payload_entrada = table.Column<string>(type: "jsonb", nullable: false),
                    xml_gerado = table.Column<string>(type: "text", nullable: true),
                    xml_assinado = table.Column<string>(type: "text", nullable: true),
                    xml_retorno_sefaz = table.Column<string>(type: "text", nullable: true),
                    protocolo_autorizacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo_status = table.Column<string>(type: "text", nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    modo_contingencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documentos_fiscais", x => x.id);
                    table.ForeignKey(
                        name: "FK_documentos_fiscais_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sequencias_numeracao",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modelo = table.Column<short>(type: "smallint", nullable: false),
                    serie = table.Column<short>(type: "smallint", nullable: false),
                    ambiente = table.Column<short>(type: "smallint", nullable: false),
                    ultimo_numero = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequencias_numeracao", x => new { x.tenant_id, x.modelo, x.serie, x.ambiente });
                    table.ForeignKey(
                        name: "FK_sequencias_numeracao_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "eventos_fiscais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    justificativa = table.Column<string>(type: "text", nullable: true),
                    xml_retorno = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventos_fiscais", x => x.id);
                    table.ForeignKey(
                        name: "FK_eventos_fiscais_documentos_fiscais_documento_id",
                        column: x => x.documento_id,
                        principalTable: "documentos_fiscais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_key_hash",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_tenant_id_ativa",
                table: "api_keys",
                columns: new[] { "tenant_id", "ativa" });

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_tenant_id_criado_em",
                table: "auditoria",
                columns: new[] { "tenant_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "IX_certificados_tenant_id_thumbprint",
                table: "certificados",
                columns: new[] { "tenant_id", "thumbprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_certificados_valido_ate",
                table: "certificados",
                column: "valido_ate",
                filter: "ativo = true");

            migrationBuilder.CreateIndex(
                name: "IX_documentos_fiscais_status_proxima_tentativa_em",
                table: "documentos_fiscais",
                columns: new[] { "status", "proxima_tentativa_em" },
                filter: "status IN ('PENDENTE','CONTINGENCIA')");

            migrationBuilder.CreateIndex(
                name: "IX_documentos_fiscais_tenant_id_criado_em",
                table: "documentos_fiscais",
                columns: new[] { "tenant_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "IX_documentos_fiscais_tenant_id_modelo_serie_numero",
                table: "documentos_fiscais",
                columns: new[] { "tenant_id", "modelo", "serie", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documentos_fiscais_tenant_id_tipo_idempotency_key",
                table: "documentos_fiscais",
                columns: new[] { "tenant_id", "tipo", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_eventos_fiscais_documento_id_tipo_evento_idempotency_key",
                table: "eventos_fiscais",
                columns: new[] { "documento_id", "tipo_evento", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_cnpj",
                table: "tenants",
                column: "cnpj",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "auditoria");

            migrationBuilder.DropTable(
                name: "certificados");

            migrationBuilder.DropTable(
                name: "eventos_fiscais");

            migrationBuilder.DropTable(
                name: "sequencias_numeracao");

            migrationBuilder.DropTable(
                name: "documentos_fiscais");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
