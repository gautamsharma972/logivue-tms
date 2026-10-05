using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Transporters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProofPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proof_performance",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    on_time = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    submitted_within_sla = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    accepted_first_time = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    rejections = table.Column<int>(type: "int", nullable: false),
                    last_rejected_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    short_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    damaged_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    refused = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    failed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proof_performance", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_proof_performance_tenant_id_delivery_id",
                schema: "transporters",
                table: "proof_performance",
                columns: new[] { "tenant_id", "delivery_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_proof_performance_tenant_id_transporter_id_updated_at",
                schema: "transporters",
                table: "proof_performance",
                columns: new[] { "tenant_id", "transporter_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proof_performance",
                schema: "transporters");
        }
    }
}
