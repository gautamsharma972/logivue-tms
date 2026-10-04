using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlacementExecutionAndPod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "tm_vehicle_placement_requests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplacementCount",
                table: "tm_vehicle_placement_requests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "VehicleId",
                table: "tm_vehicle_placement_requests",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tm_load_executions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LoadReference = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    TenderId = table.Column<long>(type: "bigint", nullable: false),
                    PlannedPickupAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActualPickupAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PlannedDeliveryAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActualDeliveryAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PickupDelayMinutes = table.Column<int>(type: "int", nullable: true),
                    PickupDelayReasonCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PickupAttribution = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveryDelayMinutes = table.Column<int>(type: "int", nullable: true),
                    DeliveryDelayReasonCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveryAttribution = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PodRequired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_load_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_load_executions_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_load_execution_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LoadExecutionId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EventAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DelayReasonCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_load_execution_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_load_execution_events_tm_load_executions_LoadExecutionId",
                        column: x => x.LoadExecutionId,
                        principalTable: "tm_load_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_pod_records",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LoadReference = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    LoadExecutionId = table.Column<long>(type: "bigint", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SubmittedWithinSla = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    PodDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedBy = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileReference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OriginalFileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RejectionCount = table.Column<int>(type: "int", nullable: false),
                    RejectionReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_pod_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_pod_records_tm_load_executions_LoadExecutionId",
                        column: x => x.LoadExecutionId,
                        principalTable: "tm_load_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tm_pod_records_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "tm_configuration_settings",
                columns: new[] { "Id", "Key", "UpdatedAt", "UpdatedBy", "ValueJson", "Version" },
                values: new object[,]
                {
                    { 16L, "tm.placement.graceMinutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "15", 1 },
                    { 17L, "tm.execution.delayPolicy", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "{\"ToleranceMinutes\":15,\"Reasons\":[{\"Code\":\"TRANSPORTER_DELAY\",\"Name\":\"Transporter Delay\",\"Attribution\":\"Carrier\"},{\"Code\":\"VEHICLE_BREAKDOWN\",\"Name\":\"Vehicle Breakdown\",\"Attribution\":\"Carrier\"},{\"Code\":\"DOCUMENTATION_ISSUE\",\"Name\":\"Documentation Issue\",\"Attribution\":\"Carrier\"},{\"Code\":\"CUSTOMER_DELAY\",\"Name\":\"Customer Delay\",\"Attribution\":\"NonCarrier\"},{\"Code\":\"WAREHOUSE_DELAY\",\"Name\":\"Warehouse Delay\",\"Attribution\":\"NonCarrier\"},{\"Code\":\"TRAFFIC\",\"Name\":\"Traffic\",\"Attribution\":\"NonCarrier\"},{\"Code\":\"ROUTE_RESTRICTION\",\"Name\":\"Route Restriction\",\"Attribution\":\"NonCarrier\"},{\"Code\":\"WEATHER\",\"Name\":\"Weather\",\"Attribution\":\"NonCarrier\"},{\"Code\":\"FORCE_MAJEURE\",\"Name\":\"Force Majeure\",\"Attribution\":\"NonCarrier\"},{\"Code\":\"OTHER\",\"Name\":\"Other\",\"Attribution\":\"Unattributed\"}]}", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_tm_load_execution_events_LoadExecutionId",
                table: "tm_load_execution_events",
                column: "LoadExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_load_executions_LoadReference_TransporterId",
                table: "tm_load_executions",
                columns: new[] { "LoadReference", "TransporterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_load_executions_PlannedDeliveryAt",
                table: "tm_load_executions",
                column: "PlannedDeliveryAt");

            migrationBuilder.CreateIndex(
                name: "IX_tm_load_executions_PlannedPickupAt",
                table: "tm_load_executions",
                column: "PlannedPickupAt");

            migrationBuilder.CreateIndex(
                name: "IX_tm_load_executions_TransporterId",
                table: "tm_load_executions",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_pod_records_DueAt",
                table: "tm_pod_records",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_tm_pod_records_LoadExecutionId",
                table: "tm_pod_records",
                column: "LoadExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_pod_records_TransporterId_Status",
                table: "tm_pod_records",
                columns: new[] { "TransporterId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tm_load_execution_events");

            migrationBuilder.DropTable(
                name: "tm_pod_records");

            migrationBuilder.DropTable(
                name: "tm_load_executions");

            migrationBuilder.DeleteData(
                table: "tm_configuration_settings",
                keyColumn: "Id",
                keyValue: 16L);

            migrationBuilder.DeleteData(
                table: "tm_configuration_settings",
                keyColumn: "Id",
                keyValue: 17L);

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "tm_vehicle_placement_requests");

            migrationBuilder.DropColumn(
                name: "ReplacementCount",
                table: "tm_vehicle_placement_requests");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "tm_vehicle_placement_requests");
        }
    }
}
