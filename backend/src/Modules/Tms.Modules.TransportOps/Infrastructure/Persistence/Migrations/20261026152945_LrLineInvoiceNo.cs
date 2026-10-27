using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.TransportOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LrLineInvoiceNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "invoice_no",
                schema: "to",
                table: "lr_lines",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "invoice_no",
                schema: "to",
                table: "lr_lines");
        }
    }
}
