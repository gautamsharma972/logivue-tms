using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Transporters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MasterDataAndDocumentRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tender_invitations_tenant_id_shipment_id_sent_at",
                schema: "transporters",
                table: "tender_invitations");

            migrationBuilder.AddColumn<string>(
                name: "type_code",
                schema: "transporters",
                table: "transporters",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "document_rules",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    kind = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_mandatory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    expiry_required = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    renewal_reminder_days = table.Column<int>(type: "int", nullable: false),
                    block_when_expired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_rules", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "master_items",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_master_items", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_tender_invitations_tenant_id_shipment_id_transporter_id_sent",
                schema: "transporters",
                table: "tender_invitations",
                columns: new[] { "tenant_id", "shipment_id", "transporter_id", "sent_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_rules_tenant_id_kind",
                schema: "transporters",
                table: "document_rules",
                columns: new[] { "tenant_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_master_items_tenant_id_kind_code",
                schema: "transporters",
                table: "master_items",
                columns: new[] { "tenant_id", "kind", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_rules",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "master_items",
                schema: "transporters");

            migrationBuilder.DropIndex(
                name: "ix_tender_invitations_tenant_id_shipment_id_transporter_id_sent",
                schema: "transporters",
                table: "tender_invitations");

            migrationBuilder.DropColumn(
                name: "type_code",
                schema: "transporters",
                table: "transporters");

            migrationBuilder.CreateIndex(
                name: "ix_tender_invitations_tenant_id_shipment_id_sent_at",
                schema: "transporters",
                table: "tender_invitations",
                columns: new[] { "tenant_id", "shipment_id", "sent_at" },
                unique: true);
        }
    }
}
