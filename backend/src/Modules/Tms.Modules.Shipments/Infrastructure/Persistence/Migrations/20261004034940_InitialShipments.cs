using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shipments");

            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    direction = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pickup = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    drop = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pickup_state = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pickup_city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    drop_state = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    drop_city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    weight_kg = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    volume_cbm = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    packages = table.Column<int>(type: "int", nullable: true),
                    description = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ready_date = table.Column<DateOnly>(type: "date", nullable: false),
                    deliver_by_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    cancel_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mode = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_type_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    planned_pickup_date = table.Column<DateOnly>(type: "date", nullable: false),
                    distance_km = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    origin_state = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origin_city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    total_weight_kg = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    total_volume_cbm = table.Column<decimal>(type: "decimal(10,3)", precision: 10, scale: 3, nullable: true),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    contract_reference = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    freight_estimate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    estimate_lines = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    override_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tendered_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    rejection_count = table.Column<int>(type: "int", nullable: false),
                    last_rejection_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    vehicle_payload_kg = table.Column<int>(type: "int", nullable: true),
                    vehicle_registration = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    driver_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_phone = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    dispatched_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    cancel_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "shipment_orders",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    order_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    drop_sequence = table.Column<int>(type: "int", nullable: false),
                    lr_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_orders_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "shipments",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_number",
                schema: "shipments",
                table: "orders",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_shipment_id",
                schema: "shipments",
                table: "orders",
                columns: new[] { "tenant_id", "shipment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_status_pickup_state_pickup_city",
                schema: "shipments",
                table: "orders",
                columns: new[] { "tenant_id", "status", "pickup_state", "pickup_city" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_orders_shipment_id_order_id",
                schema: "shipments",
                table: "shipment_orders",
                columns: new[] { "shipment_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_orders_tenant_id_order_id",
                schema: "shipments",
                table: "shipment_orders",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_number",
                schema: "shipments",
                table: "shipments",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_status",
                schema: "shipments",
                table: "shipments",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_transporter_id_status",
                schema: "shipments",
                table: "shipments",
                columns: new[] { "tenant_id", "transporter_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orders",
                schema: "shipments");

            migrationBuilder.DropTable(
                name: "shipment_orders",
                schema: "shipments");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "shipments");
        }
    }
}
