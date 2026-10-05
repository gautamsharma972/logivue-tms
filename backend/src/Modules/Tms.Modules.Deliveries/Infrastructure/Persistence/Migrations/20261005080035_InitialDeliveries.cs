using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Deliveries.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pd");

            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    outcome = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    remaining_disposition = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    order_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    order_reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    load_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    lr_number = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    transporter_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_phone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origin_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_address = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_latitude = table.Column<double>(type: "double", nullable: true),
                    customer_longitude = table.Column<double>(type: "double", nullable: true),
                    geofence_radius_m = table.Column<int>(type: "int", nullable: true),
                    planned_delivery_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    window_end = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    actual_arrival_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    actual_delivery_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    arrival_latitude = table.Column<double>(type: "double", nullable: true),
                    arrival_longitude = table.Column<double>(type: "double", nullable: true),
                    arrival_accuracy = table.Column<double>(type: "double", nullable: true),
                    has_quantity_mismatch = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    otp_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    otp_salt = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    otp_expires_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    otp_attempts = table.Column<int>(type: "int", nullable: false),
                    otp_verified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deliveries", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "delivery_exceptions",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pod_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    exception_type = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    severity = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    department = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    due_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    root_cause = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    responsible_party = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    action_taken = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    resolution = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    financial_impact = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    claim_reference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    raised_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_exceptions", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_records",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pod_number = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    pod_version = table.Column<int>(type: "int", nullable: false),
                    is_current = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    supersedes_pod_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    method = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipient_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipient_designation = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipient_phone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    arrival_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    delivery_completed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    first_submitted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true),
                    gps_accuracy = table.Column<double>(type: "double", nullable: true),
                    geofence = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipient_remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_confirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    otp_verified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    otp_attempts = table.Column<int>(type: "int", nullable: false),
                    otp_verified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    customer_acknowledged = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    rejection_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rejection_count = table.Column<int>(type: "int", nullable: false),
                    auto_accepted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_records", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_sync_records",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    client_record_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    device_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    operation = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    server_record_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    sync_status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sync_attempt = table.Column<int>(type: "int", nullable: false),
                    client_created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    client_updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    last_error = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    result_json = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_sync_records", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    value_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    attempt_number = table.Column<int>(type: "int", nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    result = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipient_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true),
                    gps_accuracy = table.Column<double>(type: "double", nullable: true),
                    device_reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_attempts_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalSchema: "pd",
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "delivery_discrepancies",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_item_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    type = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    reason_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_acknowledged = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    claim_reference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_discrepancies", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_discrepancies_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalSchema: "pd",
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "delivery_events",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    event_type = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    event_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true),
                    performed_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    device_reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_events_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalSchema: "pd",
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "delivery_items",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sku_reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ordered_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    dispatched_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    delivered_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    short_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    damaged_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    rejected_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_of_measure = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shortage_reason_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    damage_type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    damage_reason = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    damage_description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_items_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalSchema: "pd",
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "exception_notes",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    exception_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    text = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exception_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_exception_notes_delivery_exceptions_exception_id",
                        column: x => x.exception_id,
                        principalSchema: "pd",
                        principalTable: "delivery_exceptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_evidence",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pod_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    evidence_type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_key = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    file_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true),
                    device_reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    captured_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    width = table.Column<int>(type: "int", nullable: true),
                    height = table.Column<int>(type: "int", nullable: true),
                    warnings = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    client_record_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    removed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    removed_reason = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_pod_evidence_pod_records_pod_id",
                        column: x => x.pod_id,
                        principalSchema: "pd",
                        principalTable: "pod_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_items",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pod_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    delivery_item_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sku_reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ordered_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    dispatched_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    delivered_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    short_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    damaged_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    rejected_quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_pod_items_pod_records_pod_id",
                        column: x => x.pod_id,
                        principalSchema: "pd",
                        principalTable: "pod_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_ocr_results",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pod_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    evidence_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    provider = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    document_type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    overall_confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    processing_status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    raw_response_reference = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    queued_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_ocr_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_pod_ocr_results_pod_records_pod_id",
                        column: x => x.pod_id,
                        principalSchema: "pd",
                        principalTable: "pod_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_review_actions",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pod_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    action = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    field_name = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    old_value = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    new_value = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    performed_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    performed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_review_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_pod_review_actions_pod_records_pod_id",
                        column: x => x.pod_id,
                        principalSchema: "pd",
                        principalTable: "pod_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_signatures",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pod_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    signer_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    signer_designation = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_key = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true),
                    verification_method = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_signatures", x => x.id);
                    table.ForeignKey(
                        name: "fk_pod_signatures_pod_records_pod_id",
                        column: x => x.pod_id,
                        principalSchema: "pd",
                        principalTable: "pod_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_validation_results",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    pod_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    validation_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    check = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    validated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_validation_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_pod_validation_results_pod_records_pod_id",
                        column: x => x.pod_id,
                        principalSchema: "pd",
                        principalTable: "pod_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pod_ocr_fields",
                schema: "pd",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ocr_result_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    field_name = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    raw_value = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    normalized_value = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    bounding_box_json = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    validation_status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    validation_message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reviewed_value = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reviewed_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pod_ocr_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_pod_ocr_fields_pod_ocr_results_ocr_result_id",
                        column: x => x.ocr_result_id,
                        principalSchema: "pd",
                        principalTable: "pod_ocr_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_tenant_id_customer_reference",
                schema: "pd",
                table: "deliveries",
                columns: new[] { "tenant_id", "customer_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_tenant_id_load_reference",
                schema: "pd",
                table: "deliveries",
                columns: new[] { "tenant_id", "load_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_tenant_id_number",
                schema: "pd",
                table: "deliveries",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_tenant_id_planned_delivery_at",
                schema: "pd",
                table: "deliveries",
                columns: new[] { "tenant_id", "planned_delivery_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_tenant_id_shipment_id_order_id",
                schema: "pd",
                table: "deliveries",
                columns: new[] { "tenant_id", "shipment_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_tenant_id_shipment_reference",
                schema: "pd",
                table: "deliveries",
                columns: new[] { "tenant_id", "shipment_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_tenant_id_status",
                schema: "pd",
                table: "deliveries",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_tenant_id_transporter_id_status",
                schema: "pd",
                table: "deliveries",
                columns: new[] { "tenant_id", "transporter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempts_delivery_id",
                schema: "pd",
                table: "delivery_attempts",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempts_tenant_id_delivery_id_attempt_number",
                schema: "pd",
                table: "delivery_attempts",
                columns: new[] { "tenant_id", "delivery_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_discrepancies_delivery_id",
                schema: "pd",
                table: "delivery_discrepancies",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_discrepancies_tenant_id_delivery_id",
                schema: "pd",
                table: "delivery_discrepancies",
                columns: new[] { "tenant_id", "delivery_id" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_discrepancies_tenant_id_type_created_at",
                schema: "pd",
                table: "delivery_discrepancies",
                columns: new[] { "tenant_id", "type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_events_delivery_id",
                schema: "pd",
                table: "delivery_events",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_events_tenant_id_delivery_id_event_at",
                schema: "pd",
                table: "delivery_events",
                columns: new[] { "tenant_id", "delivery_id", "event_at" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_exceptions_tenant_id_delivery_id_exception_type",
                schema: "pd",
                table: "delivery_exceptions",
                columns: new[] { "tenant_id", "delivery_id", "exception_type" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_exceptions_tenant_id_number",
                schema: "pd",
                table: "delivery_exceptions",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_exceptions_tenant_id_status_severity_due_at",
                schema: "pd",
                table: "delivery_exceptions",
                columns: new[] { "tenant_id", "status", "severity", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_exceptions_tenant_id_transporter_id_status",
                schema: "pd",
                table: "delivery_exceptions",
                columns: new[] { "tenant_id", "transporter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_items_delivery_id",
                schema: "pd",
                table: "delivery_items",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_items_tenant_id_delivery_id",
                schema: "pd",
                table: "delivery_items",
                columns: new[] { "tenant_id", "delivery_id" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_items_tenant_id_sku_reference",
                schema: "pd",
                table: "delivery_items",
                columns: new[] { "tenant_id", "sku_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_exception_notes_exception_id",
                schema: "pd",
                table: "exception_notes",
                column: "exception_id");

            migrationBuilder.CreateIndex(
                name: "ix_exception_notes_tenant_id_exception_id_at",
                schema: "pd",
                table: "exception_notes",
                columns: new[] { "tenant_id", "exception_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_evidence_pod_id",
                schema: "pd",
                table: "pod_evidence",
                column: "pod_id");

            migrationBuilder.CreateIndex(
                name: "ix_pod_evidence_tenant_id_file_hash",
                schema: "pd",
                table: "pod_evidence",
                columns: new[] { "tenant_id", "file_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_evidence_tenant_id_pod_id",
                schema: "pd",
                table: "pod_evidence",
                columns: new[] { "tenant_id", "pod_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_evidence_tenant_id_pod_id_client_record_id",
                schema: "pd",
                table: "pod_evidence",
                columns: new[] { "tenant_id", "pod_id", "client_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pod_items_pod_id",
                schema: "pd",
                table: "pod_items",
                column: "pod_id");

            migrationBuilder.CreateIndex(
                name: "ix_pod_items_tenant_id_pod_id",
                schema: "pd",
                table: "pod_items",
                columns: new[] { "tenant_id", "pod_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_ocr_fields_ocr_result_id",
                schema: "pd",
                table: "pod_ocr_fields",
                column: "ocr_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_pod_ocr_fields_tenant_id_ocr_result_id_field_name",
                schema: "pd",
                table: "pod_ocr_fields",
                columns: new[] { "tenant_id", "ocr_result_id", "field_name" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_ocr_fields_tenant_id_validation_status",
                schema: "pd",
                table: "pod_ocr_fields",
                columns: new[] { "tenant_id", "validation_status" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_ocr_results_pod_id",
                schema: "pd",
                table: "pod_ocr_results",
                column: "pod_id");

            migrationBuilder.CreateIndex(
                name: "ix_pod_ocr_results_tenant_id_pod_id",
                schema: "pd",
                table: "pod_ocr_results",
                columns: new[] { "tenant_id", "pod_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_ocr_results_tenant_id_processing_status",
                schema: "pd",
                table: "pod_ocr_results",
                columns: new[] { "tenant_id", "processing_status" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_records_tenant_id_approved_at",
                schema: "pd",
                table: "pod_records",
                columns: new[] { "tenant_id", "approved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_records_tenant_id_delivery_id_is_current",
                schema: "pd",
                table: "pod_records",
                columns: new[] { "tenant_id", "delivery_id", "is_current" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_records_tenant_id_pod_number_pod_version",
                schema: "pd",
                table: "pod_records",
                columns: new[] { "tenant_id", "pod_number", "pod_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pod_records_tenant_id_status_submitted_at",
                schema: "pd",
                table: "pod_records",
                columns: new[] { "tenant_id", "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_review_actions_pod_id",
                schema: "pd",
                table: "pod_review_actions",
                column: "pod_id");

            migrationBuilder.CreateIndex(
                name: "ix_pod_review_actions_tenant_id_pod_id_performed_at",
                schema: "pd",
                table: "pod_review_actions",
                columns: new[] { "tenant_id", "pod_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_signatures_pod_id",
                schema: "pd",
                table: "pod_signatures",
                column: "pod_id");

            migrationBuilder.CreateIndex(
                name: "ix_pod_signatures_tenant_id_pod_id",
                schema: "pd",
                table: "pod_signatures",
                columns: new[] { "tenant_id", "pod_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_sync_records_tenant_id_client_record_id",
                schema: "pd",
                table: "pod_sync_records",
                columns: new[] { "tenant_id", "client_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pod_sync_records_tenant_id_sync_status",
                schema: "pd",
                table: "pod_sync_records",
                columns: new[] { "tenant_id", "sync_status" });

            migrationBuilder.CreateIndex(
                name: "ix_pod_validation_results_pod_id",
                schema: "pd",
                table: "pod_validation_results",
                column: "pod_id");

            migrationBuilder.CreateIndex(
                name: "ix_pod_validation_results_tenant_id_pod_id",
                schema: "pd",
                table: "pod_validation_results",
                columns: new[] { "tenant_id", "pod_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settings_tenant_id_key",
                schema: "pd",
                table: "settings",
                columns: new[] { "tenant_id", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "delivery_discrepancies",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "delivery_events",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "delivery_items",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "exception_notes",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "pod_evidence",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "pod_items",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "pod_ocr_fields",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "pod_review_actions",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "pod_signatures",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "pod_sync_records",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "pod_validation_results",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "delivery_exceptions",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "pod_ocr_results",
                schema: "pd");

            migrationBuilder.DropTable(
                name: "pod_records",
                schema: "pd");
        }
    }
}
