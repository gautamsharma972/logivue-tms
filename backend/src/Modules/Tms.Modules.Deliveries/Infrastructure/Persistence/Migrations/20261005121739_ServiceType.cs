using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Deliveries.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "service_type",
                schema: "pd",
                table: "deliveries",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "service_type",
                schema: "pd",
                table: "deliveries");
        }
    }
}
