using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenderingAndVendorPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "tm_tenders",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "tm_tenders",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "DestinationLocationReference",
                table: "tm_tenders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "tm_tenders",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "OriginLocationReference",
                table: "tm_tenders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAt",
                table: "tm_tenders",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceType",
                table: "tm_tenders",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeM3",
                table: "tm_tenders",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "tm_tenders",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DriverMobile",
                table: "tm_tender_responses",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "EtaAt",
                table: "tm_tender_responses",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpectedPlacementAt",
                table: "tm_tender_responses",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.InsertData(
                table: "tm_configuration_settings",
                columns: new[] { "Id", "Key", "UpdatedAt", "UpdatedBy", "ValueJson", "Version" },
                values: new object[,]
                {
                    { 14L, "tm.tender.rejectionReasons", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "[\"VEHICLE_UNAVAILABLE\",\"LANE_UNAVAILABLE\",\"RATE_ISSUE\",\"PICKUP_TIME_ISSUE\",\"CAPACITY_UNAVAILABLE\",\"DESTINATION_ISSUE\",\"OTHER\"]", 1 },
                    { 15L, "tm.tender.allowCounterOffer", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "false", 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "tm_configuration_settings",
                keyColumn: "Id",
                keyValue: 14L);

            migrationBuilder.DeleteData(
                table: "tm_configuration_settings",
                keyColumn: "Id",
                keyValue: 15L);

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "tm_tenders");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "tm_tenders");

            migrationBuilder.DropColumn(
                name: "DestinationLocationReference",
                table: "tm_tenders");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "tm_tenders");

            migrationBuilder.DropColumn(
                name: "OriginLocationReference",
                table: "tm_tenders");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "tm_tenders");

            migrationBuilder.DropColumn(
                name: "ServiceType",
                table: "tm_tenders");

            migrationBuilder.DropColumn(
                name: "VolumeM3",
                table: "tm_tenders");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "tm_tenders");

            migrationBuilder.DropColumn(
                name: "DriverMobile",
                table: "tm_tender_responses");

            migrationBuilder.DropColumn(
                name: "EtaAt",
                table: "tm_tender_responses");

            migrationBuilder.DropColumn(
                name: "ExpectedPlacementAt",
                table: "tm_tender_responses");
        }
    }
}
