using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanningRunProgressLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                schema: "shipments",
                table: "planning_runs",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "log",
                schema: "shipments",
                table: "planning_runs",
                type: "json",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "started_at",
                schema: "shipments",
                table: "planning_runs",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completed_at",
                schema: "shipments",
                table: "planning_runs");

            migrationBuilder.DropColumn(
                name: "log",
                schema: "shipments",
                table: "planning_runs");

            migrationBuilder.DropColumn(
                name: "started_at",
                schema: "shipments",
                table: "planning_runs");
        }
    }
}
