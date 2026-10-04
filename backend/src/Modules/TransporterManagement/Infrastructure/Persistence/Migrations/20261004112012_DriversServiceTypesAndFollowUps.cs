using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriversServiceTypesAndFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Denominator",
                table: "tm_transporter_scorecard_details",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Numerator",
                table: "tm_transporter_scorecard_details",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "DriverId",
                table: "tm_transporter_documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tm_service_types",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_service_types", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_drivers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    FullName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Mobile = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LicenceNumber = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_drivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_drivers_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "tm_service_types",
                columns: new[] { "Id", "Code", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1L, "FTL", true, "Full Truck Load" },
                    { 2L, "PTL", true, "Part Truck Load" },
                    { 3L, "EXPRESS", true, "Express" },
                    { 4L, "DEDICATED", true, "Dedicated" },
                    { 5L, "LAST_MILE", true, "Last Mile" },
                    { 6L, "MILK_RUN", true, "Milk Run" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_documents_DriverId",
                table: "tm_transporter_documents",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_service_types_Code",
                table: "tm_service_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_drivers_TransporterId",
                table: "tm_transporter_drivers",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_drivers_TransporterId_LicenceNumber",
                table: "tm_transporter_drivers",
                columns: new[] { "TransporterId", "LicenceNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_tm_transporter_documents_tm_transporter_drivers_DriverId",
                table: "tm_transporter_documents",
                column: "DriverId",
                principalTable: "tm_transporter_drivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tm_transporter_documents_tm_transporter_drivers_DriverId",
                table: "tm_transporter_documents");

            migrationBuilder.DropTable(
                name: "tm_service_types");

            migrationBuilder.DropTable(
                name: "tm_transporter_drivers");

            migrationBuilder.DropIndex(
                name: "IX_tm_transporter_documents_DriverId",
                table: "tm_transporter_documents");

            migrationBuilder.DropColumn(
                name: "Denominator",
                table: "tm_transporter_scorecard_details");

            migrationBuilder.DropColumn(
                name: "Numerator",
                table: "tm_transporter_scorecard_details");

            migrationBuilder.DropColumn(
                name: "DriverId",
                table: "tm_transporter_documents");
        }
    }
}
