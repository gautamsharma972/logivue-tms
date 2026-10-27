using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.TransportOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LocationSacCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sac_code",
                schema: "to",
                table: "locations",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sac_code",
                schema: "to",
                table: "locations");
        }
    }
}
