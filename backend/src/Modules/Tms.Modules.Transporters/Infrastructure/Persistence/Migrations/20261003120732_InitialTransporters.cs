using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Transporters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTransporters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "transporters");

            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    owner_kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    owner_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    number = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    file_key = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "drivers",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    full_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    phone = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    license_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
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
                    table.PrimaryKey("pk_drivers", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sequences",
                schema: "transporters",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sequences", x => new { x.tenant_id, x.name });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "transporters",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    legal_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trade_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pan = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    gstin = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contact_person = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    phone = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    address_line1 = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    address_line2 = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    state = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pincode = table.Column<string>(type: "varchar(6)", maxLength: 6, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    service_modes = table.Column<int>(type: "int", nullable: false),
                    bank_account_holder = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bank_account_number = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bank_ifsc = table.Column<string>(type: "varchar(11)", maxLength: 11, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bank_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    approval_request_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    suspension_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    activated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transporters", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "vehicle_types",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    payload_kg = table.Column<int>(type: "int", nullable: false),
                    volume_cbm = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_types", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "vehicles",
                schema: "transporters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    registration_number = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_type_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ownership = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    make = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    year_of_manufacture = table.Column<int>(type: "int", nullable: true),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicles", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_documents_tenant_id_expires_on",
                schema: "transporters",
                table: "documents",
                columns: new[] { "tenant_id", "expires_on" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_tenant_id_owner_kind_owner_id_kind",
                schema: "transporters",
                table: "documents",
                columns: new[] { "tenant_id", "owner_kind", "owner_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_tenant_id_transporter_id",
                schema: "transporters",
                table: "documents",
                columns: new[] { "tenant_id", "transporter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_drivers_tenant_id_license_number",
                schema: "transporters",
                table: "drivers",
                columns: new[] { "tenant_id", "license_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_drivers_tenant_id_transporter_id",
                schema: "transporters",
                table: "drivers",
                columns: new[] { "tenant_id", "transporter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transporters_tenant_id_code",
                schema: "transporters",
                table: "transporters",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transporters_tenant_id_gstin",
                schema: "transporters",
                table: "transporters",
                columns: new[] { "tenant_id", "gstin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transporters_tenant_id_pan",
                schema: "transporters",
                table: "transporters",
                columns: new[] { "tenant_id", "pan" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transporters_tenant_id_status",
                schema: "transporters",
                table: "transporters",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_types_tenant_id_code",
                schema: "transporters",
                table: "vehicle_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_tenant_id_registration_number",
                schema: "transporters",
                table: "vehicles",
                columns: new[] { "tenant_id", "registration_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_tenant_id_transporter_id",
                schema: "transporters",
                table: "vehicles",
                columns: new[] { "tenant_id", "transporter_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "drivers",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "sequences",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "transporters",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "vehicle_types",
                schema: "transporters");

            migrationBuilder.DropTable(
                name: "vehicles",
                schema: "transporters");
        }
    }
}
