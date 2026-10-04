using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Transporters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptBankAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "bank_account_number",
                schema: "transporters",
                table: "transporters",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(24)",
                oldMaxLength: 24,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "bank_account_number",
                schema: "transporters",
                table: "transporters",
                type: "varchar(24)",
                maxLength: 24,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(160)",
                oldMaxLength: 160,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
