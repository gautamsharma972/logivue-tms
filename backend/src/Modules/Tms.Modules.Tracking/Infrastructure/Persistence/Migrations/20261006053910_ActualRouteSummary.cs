using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Tracking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActualRouteSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actual_route_json",
                schema: "st",
                table: "shipments",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actual_route_json",
                schema: "st",
                table: "shipments");
        }
    }
}
