using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingAndCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "tm_transporter_documents",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "tm_transporter_documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "tm_transporter_documents",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_onboarding_steps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequiredRole = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_onboarding_steps", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_approval_actions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FromStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ToStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorUserId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActionAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Comments = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_approval_actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_approval_actions_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "tm_onboarding_steps",
                columns: new[] { "Id", "IsActive", "Name", "RequiredRole", "Sequence", "Status" },
                values: new object[,]
                {
                    { 1L, true, "Document Verification", "Compliance User", 1, "DocumentVerification" },
                    { 2L, true, "Operations Review", "Operations User", 2, "OperationsReview" },
                    { 3L, true, "Commercial Review", "Transport Manager", 3, "CommercialReview" },
                    { 4L, true, "Finance Review", "Finance User", 4, "FinanceReview" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_tm_onboarding_steps_Sequence",
                table: "tm_onboarding_steps",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_onboarding_steps_Status",
                table: "tm_onboarding_steps",
                column: "Status",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_approval_actions_TransporterId_ActionAt",
                table: "tm_transporter_approval_actions",
                columns: new[] { "TransporterId", "ActionAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tm_onboarding_steps");

            migrationBuilder.DropTable(
                name: "tm_transporter_approval_actions");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "tm_transporter_documents");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "tm_transporter_documents");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "tm_transporter_documents");
        }
    }
}
