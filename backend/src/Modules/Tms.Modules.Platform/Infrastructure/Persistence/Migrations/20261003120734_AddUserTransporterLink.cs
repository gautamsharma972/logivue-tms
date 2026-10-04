using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTransporterLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "transporter_id",
                schema: "platform",
                table: "users",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "transporter_id",
                schema: "platform",
                table: "users");
        }
    }
}
