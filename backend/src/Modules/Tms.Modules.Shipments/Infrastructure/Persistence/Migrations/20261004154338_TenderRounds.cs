using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenderRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tender_rounds",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    response_minutes = table.Column<int>(type: "int", nullable: false),
                    notes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    awarded_transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    closed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    close_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_rounds", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tender_events",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tender_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    invitee_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    comments = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_tender_events_tender_rounds_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "shipments",
                        principalTable: "tender_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tender_invitees",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tender_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_reference = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quoted_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sent_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    deadline = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    counter_rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    counter_comment = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    counter_status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    agreed_rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    bid_vehicle_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    bid_driver_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    bid_vehicle_registration = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bid_driver_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_invitees", x => x.id);
                    table.ForeignKey(
                        name: "fk_tender_invitees_tender_rounds_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "shipments",
                        principalTable: "tender_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_tender_events_tenant_id_tender_id_at",
                schema: "shipments",
                table: "tender_events",
                columns: new[] { "tenant_id", "tender_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_tender_events_tender_id",
                schema: "shipments",
                table: "tender_events",
                column: "tender_id");

            migrationBuilder.CreateIndex(
                name: "ix_tender_invitees_tenant_id_shipment_id",
                schema: "shipments",
                table: "tender_invitees",
                columns: new[] { "tenant_id", "shipment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tender_invitees_tenant_id_status_deadline",
                schema: "shipments",
                table: "tender_invitees",
                columns: new[] { "tenant_id", "status", "deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_tender_invitees_tenant_id_tender_id_transporter_id",
                schema: "shipments",
                table: "tender_invitees",
                columns: new[] { "tenant_id", "tender_id", "transporter_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tender_invitees_tenant_id_transporter_id_status",
                schema: "shipments",
                table: "tender_invitees",
                columns: new[] { "tenant_id", "transporter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tender_invitees_tender_id",
                schema: "shipments",
                table: "tender_invitees",
                column: "tender_id");

            migrationBuilder.CreateIndex(
                name: "ix_tender_rounds_tenant_id_number",
                schema: "shipments",
                table: "tender_rounds",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tender_rounds_tenant_id_shipment_id_status",
                schema: "shipments",
                table: "tender_rounds",
                columns: new[] { "tenant_id", "shipment_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tender_events",
                schema: "shipments");

            migrationBuilder.DropTable(
                name: "tender_invitees",
                schema: "shipments");

            migrationBuilder.DropTable(
                name: "tender_rounds",
                schema: "shipments");
        }
    }
}
