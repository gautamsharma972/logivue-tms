using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanningRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planning_runs",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    run_group_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    number = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    plan_version = table.Column<int>(type: "int", nullable: false),
                    planning_date = table.Column<DateOnly>(type: "date", nullable: false),
                    options = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    order_ids = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    plan = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    approved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    approved_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    committed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
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
                    table.PrimaryKey("pk_planning_runs", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_planning_runs_tenant_id_number_plan_version",
                schema: "shipments",
                table: "planning_runs",
                columns: new[] { "tenant_id", "number", "plan_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_runs_tenant_id_run_group_id_plan_version",
                schema: "shipments",
                table: "planning_runs",
                columns: new[] { "tenant_id", "run_group_id", "plan_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planning_runs_tenant_id_status_planning_date",
                schema: "shipments",
                table: "planning_runs",
                columns: new[] { "tenant_id", "status", "planning_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planning_runs",
                schema: "shipments");
        }
    }
}
