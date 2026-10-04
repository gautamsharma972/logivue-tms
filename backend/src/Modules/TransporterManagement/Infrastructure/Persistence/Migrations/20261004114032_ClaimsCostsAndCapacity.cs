using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClaimsCostsAndCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tm_capacity_days",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    VehiclesCommitted = table.Column<int>(type: "int", nullable: false),
                    VehiclesAvailable = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_capacity_days", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_capacity_days_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_claims",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    LoadReference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClaimValue = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResolvedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_claims_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_load_costs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    LoadReference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AgreedAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    InvoicedAmount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_load_costs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_load_costs_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_tm_capacity_days_TransporterId_Date",
                table: "tm_capacity_days",
                columns: new[] { "TransporterId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_claims_TransporterId_ClaimDate",
                table: "tm_claims",
                columns: new[] { "TransporterId", "ClaimDate" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_load_costs_TransporterId_LoadReference",
                table: "tm_load_costs",
                columns: new[] { "TransporterId", "LoadReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_load_costs_TransporterId_ServiceDate",
                table: "tm_load_costs",
                columns: new[] { "TransporterId", "ServiceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tm_capacity_days");

            migrationBuilder.DropTable(
                name: "tm_claims");

            migrationBuilder.DropTable(
                name: "tm_load_costs");
        }
    }
}
