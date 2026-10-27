using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_locations",
                schema: "platform",
                table: "tenants",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_users",
                schema: "platform",
                table: "tenants",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_locations",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "max_users",
                schema: "platform",
                table: "tenants");
        }
    }
}
