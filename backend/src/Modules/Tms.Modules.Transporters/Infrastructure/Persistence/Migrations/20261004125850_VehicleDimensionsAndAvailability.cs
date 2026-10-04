using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Transporters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VehicleDimensionsAndAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "availability",
                schema: "transporters",
                table: "vehicles",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Available")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "availability_note",
                schema: "transporters",
                table: "vehicles",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateOnly>(
                name: "available_from",
                schema: "transporters",
                table: "vehicles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "available_to",
                schema: "transporters",
                table: "vehicles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "allows_hazardous",
                schema: "transporters",
                table: "vehicle_types",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "height_m",
                schema: "transporters",
                table: "vehicle_types",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "length_m",
                schema: "transporters",
                table: "vehicle_types",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "supports_temperature_control",
                schema: "transporters",
                table: "vehicle_types",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "width_m",
                schema: "transporters",
                table: "vehicle_types",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "availability",
                schema: "transporters",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "availability_note",
                schema: "transporters",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "available_from",
                schema: "transporters",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "available_to",
                schema: "transporters",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "allows_hazardous",
                schema: "transporters",
                table: "vehicle_types");

            migrationBuilder.DropColumn(
                name: "height_m",
                schema: "transporters",
                table: "vehicle_types");

            migrationBuilder.DropColumn(
                name: "length_m",
                schema: "transporters",
                table: "vehicle_types");

            migrationBuilder.DropColumn(
                name: "supports_temperature_control",
                schema: "transporters",
                table: "vehicle_types");

            migrationBuilder.DropColumn(
                name: "width_m",
                schema: "transporters",
                table: "vehicle_types");
        }
    }
}
