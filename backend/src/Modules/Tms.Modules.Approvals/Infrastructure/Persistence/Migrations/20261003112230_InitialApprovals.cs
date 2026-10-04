using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Approvals.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "approvals");

            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "delegations",
                schema: "approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delegator_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delegate_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    valid_from = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    reason = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delegations", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "policies",
                schema: "approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    document_type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    steps = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policies", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "requests",
                schema: "approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    document_type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    document_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    requester_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    completed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    steps = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    current_step_index = table.Column<int>(type: "int", nullable: true),
                    current_permission = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    decider_key = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requests", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_delegations_tenant_id_delegate_id",
                schema: "approvals",
                table: "delegations",
                columns: new[] { "tenant_id", "delegate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_delegations_tenant_id_delegator_id",
                schema: "approvals",
                table: "delegations",
                columns: new[] { "tenant_id", "delegator_id" });

            migrationBuilder.CreateIndex(
                name: "ix_policies_tenant_id_document_type",
                schema: "approvals",
                table: "policies",
                columns: new[] { "tenant_id", "document_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requests_tenant_id_document_type_document_id",
                schema: "approvals",
                table: "requests",
                columns: new[] { "tenant_id", "document_type", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_requests_tenant_id_requester_id_created_at",
                schema: "approvals",
                table: "requests",
                columns: new[] { "tenant_id", "requester_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_requests_tenant_id_status_current_permission",
                schema: "approvals",
                table: "requests",
                columns: new[] { "tenant_id", "status", "current_permission" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delegations",
                schema: "approvals");

            migrationBuilder.DropTable(
                name: "policies",
                schema: "approvals");

            migrationBuilder.DropTable(
                name: "requests",
                schema: "approvals");
        }
    }
}
