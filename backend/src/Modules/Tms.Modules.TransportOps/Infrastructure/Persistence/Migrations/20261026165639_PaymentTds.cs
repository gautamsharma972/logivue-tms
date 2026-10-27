using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.TransportOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentTds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tds_amount",
                schema: "to",
                table: "payments",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tds_deducted",
                schema: "to",
                table: "invoices",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tds_amount",
                schema: "to",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "tds_deducted",
                schema: "to",
                table: "invoices");
        }
    }
}
