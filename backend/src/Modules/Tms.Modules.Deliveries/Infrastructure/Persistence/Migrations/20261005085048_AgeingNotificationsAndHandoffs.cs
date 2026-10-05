using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Deliveries.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgeingNotificationsAndHandoffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resubmitted_at",
                schema: "pd",
                table: "pod_records",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "returned_at",
                schema: "pd",
                table: "pod_records",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "claim_handoffs",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    discrepancy_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    system = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    payload_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_claim_handoffs", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "integration_messages",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    target = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pod_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    payload_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_messages", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_reads",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    notification_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    read_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_reads", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    kind = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    body = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    pod_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    exception_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    audience_transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    audience_permission = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dedupe_key = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_pod_records_tenant_id_returned_at",
                schema: "pd",
                table: "pod_records",
                columns: new[] { "tenant_id", "returned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_claim_handoffs_tenant_id_delivery_id",
                schema: "pd",
                table: "claim_handoffs",
                columns: new[] { "tenant_id", "delivery_id" });

            migrationBuilder.CreateIndex(
                name: "ix_claim_handoffs_tenant_id_reference",
                schema: "pd",
                table: "claim_handoffs",
                columns: new[] { "tenant_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_messages_tenant_id_delivery_id_created_at",
                schema: "pd",
                table: "integration_messages",
                columns: new[] { "tenant_id", "delivery_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_integration_messages_tenant_id_target_created_at",
                schema: "pd",
                table: "integration_messages",
                columns: new[] { "tenant_id", "target", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_reads_tenant_id_notification_id_user_id",
                schema: "pd",
                table: "notification_reads",
                columns: new[] { "tenant_id", "notification_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_reads_tenant_id_user_id",
                schema: "pd",
                table: "notification_reads",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tenant_id_audience_permission_created_at",
                schema: "pd",
                table: "notifications",
                columns: new[] { "tenant_id", "audience_permission", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tenant_id_audience_transporter_id_created_at",
                schema: "pd",
                table: "notifications",
                columns: new[] { "tenant_id", "audience_transporter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tenant_id_dedupe_key",
                schema: "pd",
                table: "notifications",
                columns: new[] { "tenant_id", "dedupe_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claim_handoffs",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "integration_messages",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "notification_reads",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "pd");

            migrationBuilder.DropIndex(
                name: "ix_pod_records_tenant_id_returned_at",
                schema: "pd",
                table: "pod_records");

            migrationBuilder.DropColumn(
                name: "resubmitted_at",
                schema: "pd",
                table: "pod_records");

            migrationBuilder.DropColumn(
                name: "returned_at",
                schema: "pd",
                table: "pod_records");
        }
    }
}
