using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage2RoutesAndReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "peak_onboard_kg",
                schema: "shipments",
                table: "shipments",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "direction",
                schema: "shipments",
                table: "shipment_orders",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Forward")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "is_return",
                schema: "shipments",
                table: "shipment_orders",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "delivery_window_from",
                schema: "shipments",
                table: "orders",
                type: "time(6)",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "delivery_window_to",
                schema: "shipments",
                table: "orders",
                type: "time(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "peak_onboard_kg",
                schema: "shipments",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "direction",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "is_return",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "delivery_window_from",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "delivery_window_to",
                schema: "shipments",
                table: "orders");
        }
    }
}
