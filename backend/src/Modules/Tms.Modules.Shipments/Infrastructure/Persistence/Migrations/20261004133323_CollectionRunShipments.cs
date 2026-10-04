using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CollectionRunShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "collection_city",
                schema: "shipments",
                table: "shipments",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "collection_state",
                schema: "shipments",
                table: "shipments",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "collection_city",
                schema: "shipments",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "collection_state",
                schema: "shipments",
                table: "shipments");
        }
    }
}
