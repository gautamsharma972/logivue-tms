using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoleAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "audience",
                schema: "platform",
                table: "roles",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Internal")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "audience",
                schema: "platform",
                table: "roles");
        }
    }
}
