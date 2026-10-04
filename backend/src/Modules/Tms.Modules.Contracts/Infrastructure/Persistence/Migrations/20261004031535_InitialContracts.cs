using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Contracts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contracts");

            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contracts",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revision = table.Column<int>(type: "int", nullable: false),
                    revision_of_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: false),
                    payment_terms_days = table.Column<int>(type: "int", nullable: false),
                    estimated_annual_spend = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    terms = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fuel = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    approval_request_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    termination_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rates_revision = table.Column<int>(type: "int", nullable: false),
                    rate_count = table.Column<int>(type: "int", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "diesel_prices",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    region = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    price_per_litre = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diesel_prices", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_key = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "expiry_alerts",
                schema: "contracts",
                columns: table => new
                {
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    days_before = table.Column<int>(type: "int", nullable: false),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sent_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expiry_alerts", x => new { x.contract_id, x.days_before });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sequences",
                schema: "contracts",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sequences", x => new { x.tenant_id, x.name });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "zones",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    members = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zones", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "rate_cards",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    origin_kind = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origin_state = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origin_city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origin_zone = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_kind = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_state = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_zone = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    both_ways = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    vehicle_type_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    min_distance_km = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    max_distance_km = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    pricing = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_rate_cards_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "contracts",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_tenant_id_number_revision",
                schema: "contracts",
                table: "contracts",
                columns: new[] { "tenant_id", "number", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_tenant_id_status_effective_to",
                schema: "contracts",
                table: "contracts",
                columns: new[] { "tenant_id", "status", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_tenant_id_transporter_id_status",
                schema: "contracts",
                table: "contracts",
                columns: new[] { "tenant_id", "transporter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_diesel_prices_tenant_id_region_effective_from",
                schema: "contracts",
                table: "diesel_prices",
                columns: new[] { "tenant_id", "region", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_tenant_id_contract_id",
                schema: "contracts",
                table: "documents",
                columns: new[] { "tenant_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_contract_id",
                schema: "contracts",
                table: "rate_cards",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_tenant_id_origin_state_destination_state",
                schema: "contracts",
                table: "rate_cards",
                columns: new[] { "tenant_id", "origin_state", "destination_state" });

            migrationBuilder.CreateIndex(
                name: "ix_zones_tenant_id_code",
                schema: "contracts",
                table: "zones",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diesel_prices",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "expiry_alerts",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "rate_cards",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "sequences",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "zones",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "contracts",
                schema: "contracts");
        }
    }
}
