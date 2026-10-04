using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Transporters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlacementsClaimsCostsCapacityAndAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alerts",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    alert_type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    severity = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    shipment_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    entity_key = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(14)", maxLength: 14, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    resolution = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerts", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "capacity_days",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    vehicles_committed = table.Column<int>(type: "int", nullable: false),
                    vehicles_available = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capacity_days", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "claims",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    shipment_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    claim_type = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    claim_date = table.Column<DateOnly>(type: "date", nullable: false),
                    claim_value = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_key = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_claims", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "load_costs",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    agreed_amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    invoiced_amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_load_costs", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "placements",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    vehicle_type_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    mode = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origin_state = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origin_city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_state = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    vehicle_registration = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    required_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    placed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    loading_started_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    replacement_count = table.Column<int>(type: "int", nullable: false),
                    exception_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_placements", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "placement_events",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    placement_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_type = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    event_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_placement_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_placement_events_placements_placement_id",
                        column: x => x.placement_id,
                        principalSchema: "transporters",
                        principalTable: "placements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_tenant_id_alert_type_entity_key",
                schema: "transporters",
                table: "alerts",
                columns: new[] { "tenant_id", "alert_type", "entity_key" });

            migrationBuilder.CreateIndex(
                name: "ix_alerts_tenant_id_status_severity",
                schema: "transporters",
                table: "alerts",
                columns: new[] { "tenant_id", "status", "severity" });

            migrationBuilder.CreateIndex(
                name: "ix_capacity_days_tenant_id_transporter_id_date",
                schema: "transporters",
                table: "capacity_days",
                columns: new[] { "tenant_id", "transporter_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_claims_tenant_id_source_key",
                schema: "transporters",
                table: "claims",
                columns: new[] { "tenant_id", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_claims_tenant_id_transporter_id_claim_date",
                schema: "transporters",
                table: "claims",
                columns: new[] { "tenant_id", "transporter_id", "claim_date" });

            migrationBuilder.CreateIndex(
                name: "ix_load_costs_tenant_id_shipment_id",
                schema: "transporters",
                table: "load_costs",
                columns: new[] { "tenant_id", "shipment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_load_costs_tenant_id_transporter_id_service_date",
                schema: "transporters",
                table: "load_costs",
                columns: new[] { "tenant_id", "transporter_id", "service_date" });

            migrationBuilder.CreateIndex(
                name: "ix_placement_events_placement_id",
                schema: "transporters",
                table: "placement_events",
                column: "placement_id");

            migrationBuilder.CreateIndex(
                name: "ix_placements_tenant_id_shipment_id",
                schema: "transporters",
                table: "placements",
                columns: new[] { "tenant_id", "shipment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_placements_tenant_id_status",
                schema: "transporters",
                table: "placements",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_placements_tenant_id_transporter_id_required_at",
                schema: "transporters",
                table: "placements",
                columns: new[] { "tenant_id", "transporter_id", "required_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "capacity_days",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "claims",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "load_costs",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "placement_events",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "placements",
                schema: "transporters");
        }
    }
}
