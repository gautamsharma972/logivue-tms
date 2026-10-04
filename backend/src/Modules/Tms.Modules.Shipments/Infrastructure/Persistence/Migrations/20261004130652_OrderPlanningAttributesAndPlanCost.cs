using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderPlanningAttributesAndPlanCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "plan_reference",
                schema: "shipments",
                table: "shipments",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "planned_cost",
                schema: "shipments",
                table: "shipments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "handling",
                schema: "shipments",
                table: "orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_hazardous",
                schema: "shipments",
                table: "orders",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_stackable",
                schema: "shipments",
                table: "orders",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "longest_item_m",
                schema: "shipments",
                table: "orders",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "pickup_window_from",
                schema: "shipments",
                table: "orders",
                type: "time(6)",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "pickup_window_to",
                schema: "shipments",
                table: "orders",
                type: "time(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "priority",
                schema: "shipments",
                table: "orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "product_category",
                schema: "shipments",
                table: "orders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "return_reason",
                schema: "shipments",
                table: "orders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "return_type",
                schema: "shipments",
                table: "orders",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "plan_reference",
                schema: "shipments",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "planned_cost",
                schema: "shipments",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "handling",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "is_hazardous",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "is_stackable",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "longest_item_m",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pickup_window_from",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pickup_window_to",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "priority",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "product_category",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "return_reason",
                schema: "shipments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "return_type",
                schema: "shipments",
                table: "orders");
        }
    }
}
