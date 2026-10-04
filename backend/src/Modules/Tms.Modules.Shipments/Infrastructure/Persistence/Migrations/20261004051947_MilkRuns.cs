using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MilkRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "milk_run_templates",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    code = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    depot_location_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    vehicle_type_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    max_stops = table.Column<int>(type: "int", nullable: false),
                    max_duration_minutes = table.Column<int>(type: "int", nullable: false),
                    departure_time = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    days = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_milk_run_templates", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "milk_run_stops",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    milk_run_template_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    location_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    type = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    service_minutes = table.Column<int>(type: "int", nullable: false),
                    window_from = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    window_to = table.Column<TimeOnly>(type: "time(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_milk_run_stops", x => x.id);
                    table.ForeignKey(
                        name: "fk_milk_run_stops_milk_run_templates_milk_run_template_id",
                        column: x => x.milk_run_template_id,
                        principalSchema: "shipments",
                        principalTable: "milk_run_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_milk_run_stops_milk_run_template_id_sequence",
                schema: "shipments",
                table: "milk_run_stops",
                columns: new[] { "milk_run_template_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_milk_run_stops_tenant_id_location_id",
                schema: "shipments",
                table: "milk_run_stops",
                columns: new[] { "tenant_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_milk_run_templates_tenant_id_code",
                schema: "shipments",
                table: "milk_run_templates",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_milk_run_templates_tenant_id_is_active",
                schema: "shipments",
                table: "milk_run_templates",
                columns: new[] { "tenant_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "milk_run_stops",
                schema: "shipments");

            migrationBuilder.DropTable(
                name: "milk_run_templates",
                schema: "shipments");
        }
    }
}
