using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Tracking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "st");

            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "current_vehicle_positions",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    session_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    transporter_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    latitude = table.Column<double>(type: "double", nullable: false),
                    longitude = table.Column<double>(type: "double", nullable: false),
                    accuracy_meters = table.Column<double>(type: "double", nullable: true),
                    speed_kph = table.Column<double>(type: "double", nullable: true),
                    heading = table.Column<double>(type: "double", nullable: true),
                    last_captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    last_received_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    health = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    moving = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    battery_percentage = table.Column<int>(type: "int", nullable: true),
                    network_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    updated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_current_vehicle_positions", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "customer_tracking_links",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    token_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expires_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    revoked_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    view_count = table.Column<int>(type: "int", nullable: false),
                    last_viewed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_tracking_links", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "dwell_events",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stop_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    place = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    latitude = table.Column<double>(type: "double", nullable: false),
                    longitude = table.Column<double>(type: "double", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    duration_minutes = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<string>(type: "varchar(14)", maxLength: 14, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expected_duration_minutes = table.Column<int>(type: "int", nullable: false),
                    excess_duration_minutes = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dwell_events", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "eta_predictions",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stop_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    is_final_destination = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    predicted_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    predicted_eta = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    remaining_km = table.Column<double>(type: "double", nullable: false),
                    confidence = table.Column<double>(type: "double", nullable: false),
                    risk_level = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    risk = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    delay_minutes = table.Column<int>(type: "int", nullable: false),
                    source = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    calculation_version = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eta_predictions", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "geofence_events",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    geofence_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    stop_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    geofence_code = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    geofence_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    place_type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    event_type = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    detected_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: false),
                    longitude = table.Column<double>(type: "double", nullable: false),
                    gps_accuracy = table.Column<double>(type: "double", nullable: true),
                    confidence = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geofence_events", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "geofence_presence",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    subject_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    is_stop = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    state = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    since = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    points = table.Column<int>(type: "int", nullable: false),
                    inside_since = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    stayed_raised = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geofence_presence", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "geofences",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    code = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    center_latitude = table.Column<double>(type: "double", nullable: false),
                    center_longitude = table.Column<double>(type: "double", nullable: false),
                    radius_meters = table.Column<int>(type: "int", nullable: false),
                    polygon_json = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geofences", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "milestones",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    stop_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    type = table.Column<string>(type: "varchar(28)", maxLength: 28, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    label = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    order = table.Column<int>(type: "int", nullable: false),
                    planned_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    estimated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    actual_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason_code = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_milestones", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "route_deviations",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    detected_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    latitude = table.Column<double>(type: "double", nullable: false),
                    longitude = table.Column<double>(type: "double", nullable: false),
                    distance_from_route_km = table.Column<double>(type: "double", nullable: false),
                    duration_minutes = table.Column<int>(type: "int", nullable: false),
                    severity = table.Column<string>(type: "varchar(14)", maxLength: 14, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_deviations", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "routes",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    points_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    length_km = table.Column<double>(type: "double", nullable: false),
                    planned_minutes = table.Column<int>(type: "int", nullable: true),
                    source = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routes", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    value_json = table.Column<string>(type: "text", nullable: false)
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
                name: "shipment_events",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    event_type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    event_time = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    source = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true),
                    geofence_reference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stop_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reason_code = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    confidence = table.Column<double>(type: "double", nullable: true),
                    performed_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_events", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    transporter_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_phone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    origin_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    destination_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    execution = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tracking = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    risk = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    delivery = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    planned_start_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    planned_arrival_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    planned_distance_km = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    planned_duration_minutes = table.Column<int>(type: "int", nullable: true),
                    route_source = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    started_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    current_session_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    last_latitude = table.Column<double>(type: "double", nullable: true),
                    last_longitude = table.Column<double>(type: "double", nullable: true),
                    last_captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    last_received_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    last_speed_kph = table.Column<double>(type: "double", nullable: true),
                    last_heading = table.Column<double>(type: "double", nullable: true),
                    last_accuracy_m = table.Column<double>(type: "double", nullable: true),
                    travelled_km = table.Column<double>(type: "double", nullable: false),
                    remaining_km = table.Column<double>(type: "double", nullable: true),
                    progress_pct = table.Column<double>(type: "double", nullable: true),
                    along_km = table.Column<double>(type: "double", nullable: true),
                    off_route_km = table.Column<double>(type: "double", nullable: true),
                    system_eta_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    eta_confidence = table.Column<double>(type: "double", nullable: true),
                    delay_minutes = table.Column<int>(type: "int", nullable: false),
                    last_eta_recorded_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    last_recorded_eta = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    identical_count = table.Column<int>(type: "int", nullable: false),
                    recent_moving_speed_kph = table.Column<double>(type: "double", nullable: true),
                    recent_speed_count = table.Column<int>(type: "int", nullable: false),
                    off_route_since = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    off_route_points = table.Column<int>(type: "int", nullable: false),
                    max_off_route_km = table.Column<double>(type: "double", nullable: false),
                    deviation_open = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    deviation_back_since = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    dwell_anchor_latitude = table.Column<double>(type: "double", nullable: true),
                    dwell_anchor_longitude = table.Column<double>(type: "double", nullable: true),
                    dwell_since = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    dwell_last_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    dwell_open = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    dwell_planned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    dwell_where = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dwell_excess_raised = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    dwell_unplanned_raised = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    eta_override_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    eta_override_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    eta_override_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    eta_override_set_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    delay_reason = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    delay_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sync_records",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    client_key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    operation = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    device_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    result_json = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_records", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tracking_alerts",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    severity = table.Column<string>(type: "varchar(14)", maxLength: 14, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    message = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(14)", maxLength: 14, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    raised_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    dedupe_key = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    acknowledged_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    resolution_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    exception_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracking_alerts", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tracking_devices",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    device_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_reference = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    app_version = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    last_battery_percentage = table.Column<int>(type: "int", nullable: true),
                    last_network_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    location_permission = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracking_devices", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tracking_exceptions",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    severity = table.Column<string>(type: "varchar(14)", maxLength: 14, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    transporter_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(14)", maxLength: 14, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    raised_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    department = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    escalation_level = table.Column<int>(type: "int", nullable: false),
                    escalated_to = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    escalated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    root_cause = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    delay_reason = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    action_taken = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    condition_cleared_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    alert_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracking_exceptions", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tracking_gaps",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    gap_start = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    gap_end = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    duration_minutes = table.Column<int>(type: "int", nullable: false),
                    last_known_latitude = table.Column<double>(type: "double", nullable: false),
                    last_known_longitude = table.Column<double>(type: "double", nullable: false),
                    severity = table.Column<string>(type: "varchar(14)", maxLength: 14, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracking_gaps", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tracking_locations",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    client_location_reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    session_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_reference = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    latitude = table.Column<double>(type: "double", nullable: false),
                    longitude = table.Column<double>(type: "double", nullable: false),
                    accuracy_meters = table.Column<double>(type: "double", nullable: true),
                    speed_kph = table.Column<double>(type: "double", nullable: true),
                    heading = table.Column<double>(type: "double", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    device_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    validation = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    anomalies = table.Column<int>(type: "int", nullable: false),
                    reasons = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_late = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    app_version = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    battery_percentage = table.Column<int>(type: "int", nullable: true),
                    network_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracking_locations", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tracking_sessions",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    trip_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    driver_reference = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    device_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    started_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    stopped_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    stop_reason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    started_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    last_location_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    last_received_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    location_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracking_sessions", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "shipment_stops",
                schema: "st",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tracked_shipment_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    city = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    latitude = table.Column<double>(type: "double", nullable: true),
                    longitude = table.Column<double>(type: "double", nullable: true),
                    radius_m = table.Column<int>(type: "int", nullable: false),
                    geofence_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    place_type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    order_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    customer_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    planned_arrival = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    window_start = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    window_end = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    expected_dwell_minutes = table.Column<int>(type: "int", nullable: true),
                    along_km = table.Column<double>(type: "double", nullable: true),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    arrived_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    departed_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    eta_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    eta_confidence = table.Column<double>(type: "double", nullable: true),
                    delay_minutes = table.Column<int>(type: "int", nullable: false),
                    risk = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_stops", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_stops_shipments_tracked_shipment_id",
                        column: x => x.tracked_shipment_id,
                        principalSchema: "st",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "exception_notes",
                schema: "st",
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
                        name: "fk_exception_notes_tracking_exceptions_exception_id",
                        column: x => x.exception_id,
                        principalSchema: "st",
                        principalTable: "tracking_exceptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_current_vehicle_positions_tenant_id_transporter_id",
                schema: "st",
                table: "current_vehicle_positions",
                columns: new[] { "tenant_id", "transporter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_current_vehicle_positions_tenant_id_trip_reference",
                schema: "st",
                table: "current_vehicle_positions",
                columns: new[] { "tenant_id", "trip_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_current_vehicle_positions_tenant_id_vehicle_reference",
                schema: "st",
                table: "current_vehicle_positions",
                columns: new[] { "tenant_id", "vehicle_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_tracking_links_tenant_id_tracked_shipment_id",
                schema: "st",
                table: "customer_tracking_links",
                columns: new[] { "tenant_id", "tracked_shipment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_tracking_links_token_hash",
                schema: "st",
                table: "customer_tracking_links",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dwell_events_tenant_id_tracked_shipment_id_start_at",
                schema: "st",
                table: "dwell_events",
                columns: new[] { "tenant_id", "tracked_shipment_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_eta_predictions_tenant_id_tracked_shipment_id_stop_id_predic",
                schema: "st",
                table: "eta_predictions",
                columns: new[] { "tenant_id", "tracked_shipment_id", "stop_id", "predicted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_exception_notes_exception_id",
                schema: "st",
                table: "exception_notes",
                column: "exception_id");

            migrationBuilder.CreateIndex(
                name: "ix_exception_notes_tenant_id_exception_id_at",
                schema: "st",
                table: "exception_notes",
                columns: new[] { "tenant_id", "exception_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_geofence_events_tenant_id_geofence_id_detected_at",
                schema: "st",
                table: "geofence_events",
                columns: new[] { "tenant_id", "geofence_id", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_geofence_events_tenant_id_tracked_shipment_id_detected_at",
                schema: "st",
                table: "geofence_events",
                columns: new[] { "tenant_id", "tracked_shipment_id", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_geofence_presence_tenant_id_tracked_shipment_id_subject_id",
                schema: "st",
                table: "geofence_presence",
                columns: new[] { "tenant_id", "tracked_shipment_id", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_geofences_tenant_id_code",
                schema: "st",
                table: "geofences",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_geofences_tenant_id_status_type",
                schema: "st",
                table: "geofences",
                columns: new[] { "tenant_id", "status", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_milestones_tenant_id_tracked_shipment_id_order",
                schema: "st",
                table: "milestones",
                columns: new[] { "tenant_id", "tracked_shipment_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_route_deviations_tenant_id_status_severity",
                schema: "st",
                table: "route_deviations",
                columns: new[] { "tenant_id", "status", "severity" });

            migrationBuilder.CreateIndex(
                name: "ix_route_deviations_tenant_id_tracked_shipment_id_detected_at",
                schema: "st",
                table: "route_deviations",
                columns: new[] { "tenant_id", "tracked_shipment_id", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_routes_tenant_id_tracked_shipment_id",
                schema: "st",
                table: "routes",
                columns: new[] { "tenant_id", "tracked_shipment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_settings_tenant_id_key",
                schema: "st",
                table: "settings",
                columns: new[] { "tenant_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_events_tenant_id_tracked_shipment_id_event_time",
                schema: "st",
                table: "shipment_events",
                columns: new[] { "tenant_id", "tracked_shipment_id", "event_time" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_events_tenant_id_trip_reference_event_type_event_ti",
                schema: "st",
                table: "shipment_events",
                columns: new[] { "tenant_id", "trip_reference", "event_type", "event_time" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_stops_tenant_id_tracked_shipment_id_sequence",
                schema: "st",
                table: "shipment_stops",
                columns: new[] { "tenant_id", "tracked_shipment_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_stops_tracked_shipment_id",
                schema: "st",
                table: "shipment_stops",
                column: "tracked_shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_execution_tracking_risk",
                schema: "st",
                table: "shipments",
                columns: new[] { "tenant_id", "execution", "tracking", "risk" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_shipment_id",
                schema: "st",
                table: "shipments",
                columns: new[] { "tenant_id", "shipment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_transporter_id",
                schema: "st",
                table: "shipments",
                columns: new[] { "tenant_id", "transporter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_trip_reference",
                schema: "st",
                table: "shipments",
                columns: new[] { "tenant_id", "trip_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_vehicle_reference",
                schema: "st",
                table: "shipments",
                columns: new[] { "tenant_id", "vehicle_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_records_tenant_id_operation_client_key",
                schema: "st",
                table: "sync_records",
                columns: new[] { "tenant_id", "operation", "client_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracking_alerts_tenant_id_dedupe_key",
                schema: "st",
                table: "tracking_alerts",
                columns: new[] { "tenant_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracking_alerts_tenant_id_status_severity_raised_at",
                schema: "st",
                table: "tracking_alerts",
                columns: new[] { "tenant_id", "status", "severity", "raised_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_alerts_tenant_id_tracked_shipment_id",
                schema: "st",
                table: "tracking_alerts",
                columns: new[] { "tenant_id", "tracked_shipment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_devices_tenant_id_device_id",
                schema: "st",
                table: "tracking_devices",
                columns: new[] { "tenant_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracking_exceptions_tenant_id_number",
                schema: "st",
                table: "tracking_exceptions",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracking_exceptions_tenant_id_status_severity_due_at",
                schema: "st",
                table: "tracking_exceptions",
                columns: new[] { "tenant_id", "status", "severity", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_exceptions_tenant_id_tracked_shipment_id",
                schema: "st",
                table: "tracking_exceptions",
                columns: new[] { "tenant_id", "tracked_shipment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_exceptions_tenant_id_transporter_id_status",
                schema: "st",
                table: "tracking_exceptions",
                columns: new[] { "tenant_id", "transporter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_gaps_tenant_id_tracked_shipment_id_gap_start",
                schema: "st",
                table: "tracking_gaps",
                columns: new[] { "tenant_id", "tracked_shipment_id", "gap_start" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_locations_tenant_id_device_id_trip_reference_client",
                schema: "st",
                table: "tracking_locations",
                columns: new[] { "tenant_id", "device_id", "trip_reference", "client_location_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracking_locations_tenant_id_received_at",
                schema: "st",
                table: "tracking_locations",
                columns: new[] { "tenant_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_locations_tenant_id_shipment_id_captured_at",
                schema: "st",
                table: "tracking_locations",
                columns: new[] { "tenant_id", "shipment_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_locations_tenant_id_trip_reference_captured_at",
                schema: "st",
                table: "tracking_locations",
                columns: new[] { "tenant_id", "trip_reference", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_locations_tenant_id_vehicle_reference_captured_at",
                schema: "st",
                table: "tracking_locations",
                columns: new[] { "tenant_id", "vehicle_reference", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_sessions_tenant_id_reference",
                schema: "st",
                table: "tracking_sessions",
                columns: new[] { "tenant_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracking_sessions_tenant_id_tracked_shipment_id_status",
                schema: "st",
                table: "tracking_sessions",
                columns: new[] { "tenant_id", "tracked_shipment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_sessions_tenant_id_transporter_id_status",
                schema: "st",
                table: "tracking_sessions",
                columns: new[] { "tenant_id", "transporter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tracking_sessions_tenant_id_vehicle_reference_status",
                schema: "st",
                table: "tracking_sessions",
                columns: new[] { "tenant_id", "vehicle_reference", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "current_vehicle_positions",
                schema: "st");

            migrationBuilder.DropTable(
                name: "customer_tracking_links",
                schema: "st");

            migrationBuilder.DropTable(
                name: "dwell_events",
                schema: "st");

            migrationBuilder.DropTable(
                name: "eta_predictions",
                schema: "st");

            migrationBuilder.DropTable(
                name: "exception_notes",
                schema: "st");

            migrationBuilder.DropTable(
                name: "geofence_events",
                schema: "st");

            migrationBuilder.DropTable(
                name: "geofence_presence",
                schema: "st");

            migrationBuilder.DropTable(
                name: "geofences",
                schema: "st");

            migrationBuilder.DropTable(
                name: "milestones",
                schema: "st");

            migrationBuilder.DropTable(
                name: "route_deviations",
                schema: "st");

            migrationBuilder.DropTable(
                name: "routes",
                schema: "st");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "st");

            migrationBuilder.DropTable(
                name: "shipment_events",
                schema: "st");

            migrationBuilder.DropTable(
                name: "shipment_stops",
                schema: "st");

            migrationBuilder.DropTable(
                name: "sync_records",
                schema: "st");

            migrationBuilder.DropTable(
                name: "tracking_alerts",
                schema: "st");

            migrationBuilder.DropTable(
                name: "tracking_devices",
                schema: "st");

            migrationBuilder.DropTable(
                name: "tracking_gaps",
                schema: "st");

            migrationBuilder.DropTable(
                name: "tracking_locations",
                schema: "st");

            migrationBuilder.DropTable(
                name: "tracking_sessions",
                schema: "st");

            migrationBuilder.DropTable(
                name: "tracking_exceptions",
                schema: "st");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "st");
        }
    }
}
