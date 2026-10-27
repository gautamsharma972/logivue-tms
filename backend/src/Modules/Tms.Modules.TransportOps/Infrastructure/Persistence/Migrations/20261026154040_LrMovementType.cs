using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.TransportOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LrMovementType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cross_amount",
                schema: "to",
                table: "lrs",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cross_kg",
                schema: "to",
                table: "lrs",
                type: "decimal(14,3)",
                precision: 14,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cross_mode",
                schema: "to",
                table: "lrs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "cross_rate_per_kg",
                schema: "to",
                table: "lrs",
                type: "decimal(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "movement",
                schema: "to",
                table: "lrs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Supply")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cross_amount",
                schema: "to",
                table: "lrs");

            migrationBuilder.DropColumn(
                name: "cross_kg",
                schema: "to",
                table: "lrs");

            migrationBuilder.DropColumn(
                name: "cross_mode",
                schema: "to",
                table: "lrs");

            migrationBuilder.DropColumn(
                name: "cross_rate_per_kg",
                schema: "to",
                table: "lrs");

            migrationBuilder.DropColumn(
                name: "movement",
                schema: "to",
                table: "lrs");
        }
    }
}
