using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryAndPod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "damaged_packages",
                schema: "shipments",
                table: "shipment_orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivered_at",
                schema: "shipments",
                table: "shipment_orders",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delivered_packages",
                schema: "shipments",
                table: "shipment_orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_remarks",
                schema: "shipments",
                table: "shipment_orders",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "packages_shipped",
                schema: "shipments",
                table: "shipment_orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pod_rejection_reason",
                schema: "shipments",
                table: "shipment_orders",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pod_reviewed_at",
                schema: "shipments",
                table: "shipment_orders",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pod_reviewed_by",
                schema: "shipments",
                table: "shipment_orders",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "pod_status",
                schema: "shipments",
                table: "shipment_orders",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Awaiting")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "receiver_name",
                schema: "shipments",
                table: "shipment_orders",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_documents",
                schema: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    order_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    file_key = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_documents", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_orders_tenant_id_pod_status_delivered_at",
                schema: "shipments",
                table: "shipment_orders",
                columns: new[] { "tenant_id", "pod_status", "delivered_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_documents_tenant_id_shipment_id_order_id",
                schema: "shipments",
                table: "pod_documents",
                columns: new[] { "tenant_id", "shipment_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_documents_tenant_id_transporter_id",
                schema: "shipments",
                table: "pod_documents",
                columns: new[] { "tenant_id", "transporter_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pod_documents",
                schema: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipment_orders_tenant_id_pod_status_delivered_at",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "damaged_packages",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "delivered_at",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "delivered_packages",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "delivery_remarks",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "packages_shipped",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "pod_rejection_reason",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "pod_reviewed_at",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "pod_reviewed_by",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "pod_status",
                schema: "shipments",
                table: "shipment_orders");

            migrationBuilder.DropColumn(
                name: "receiver_name",
                schema: "shipments",
                table: "shipment_orders");
        }
    }
}
