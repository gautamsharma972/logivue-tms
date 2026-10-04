using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTransporterManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_capability_types",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_capability_types", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_configuration_settings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValueJson = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_configuration_settings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_document_types",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsMandatory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsTransporterLevel = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsVehicleLevel = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsDriverLevel = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    VerificationRequired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExpiryRequired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RenewalReminderDays = table.Column<int>(type: "int", nullable: true),
                    BlockAllocationWhenExpired = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_document_types", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_audit_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EntityType = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OldValueJson = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewValueJson = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PerformedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PerformedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CorrelationId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_audit_logs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_types",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_types", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterCode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LegalName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TradeName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterTypeId = table.Column<long>(type: "bigint", nullable: true),
                    CompanyType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Pan = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Gstin = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegistrationNumber = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Address = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    City = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Country = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrimaryContactName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrimaryContactEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrimaryContactPhone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Website = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporters_tm_transporter_types_TransporterTypeId",
                        column: x => x.TransporterTypeId,
                        principalTable: "tm_transporter_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_tenders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenderNumber = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LoadReference = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    TenderType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OfferedRate = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    VehicleTypeReference = table.Column<long>(type: "bigint", nullable: true),
                    PickupDateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeliveryDateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResponseDeadline = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_tenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_tenders_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_alerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AlertType = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Severity = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    LoadReference = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityType = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_alerts_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_branches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BranchName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Address = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    City = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Latitude = table.Column<double>(type: "double", nullable: true),
                    Longitude = table.Column<double>(type: "double", nullable: true),
                    ContactName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContactPhone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_branches_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_capabilities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    CapabilityTypeId = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_capabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_capabilities_tm_capability_types_CapabilityTy~",
                        column: x => x.CapabilityTypeId,
                        principalTable: "tm_capability_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tm_transporter_capabilities_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_contacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Designation = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContactType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_contacts_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    VehicleId = table.Column<long>(type: "bigint", nullable: true),
                    DocumentTypeId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentNumber = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FileReference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerificationStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerifiedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Remarks = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_documents_tm_document_types_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "tm_document_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tm_transporter_documents_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_exceptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExceptionType = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Severity = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    LoadReference = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RootCause = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Owner = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActionTaken = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_exceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_exceptions_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_lanes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    OriginLocationReference = table.Column<long>(type: "bigint", nullable: false),
                    DestinationLocationReference = table.Column<long>(type: "bigint", nullable: false),
                    ServiceType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VehicleTypeReference = table.Column<long>(type: "bigint", nullable: true),
                    TransitSlaMinutes = table.Column<int>(type: "int", nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_lanes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_lanes_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_performance_kpis",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    LaneReference = table.Column<long>(type: "bigint", nullable: true),
                    VehicleTypeReference = table.Column<long>(type: "bigint", nullable: true),
                    ServiceType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KpiType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Numerator = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    Denominator = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    KpiValue = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    CalculationVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_performance_kpis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_performance_kpis_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_planning_feedback",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    LaneReference = table.Column<long>(type: "bigint", nullable: true),
                    OverallScore = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    OtpPct = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    OtdPct = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    PlacementCompliancePct = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    PodCompliancePct = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    TenderAcceptancePct = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    ClaimsRatePct = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    CostPerformanceScore = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    AvailabilityScore = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    PreferredFlag = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RestrictedFlag = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RecommendationScore = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_planning_feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_planning_feedback_tm_transporters_Transporter~",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_planning_rules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    RuleType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LaneReference = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_planning_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_planning_rules_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_rankings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    LaneReference = table.Column<long>(type: "bigint", nullable: true),
                    VehicleTypeReference = table.Column<long>(type: "bigint", nullable: true),
                    ServiceType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_rankings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_rankings_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_rates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    OriginLocationReference = table.Column<long>(type: "bigint", nullable: false),
                    DestinationLocationReference = table.Column<long>(type: "bigint", nullable: false),
                    VehicleTypeReference = table.Column<long>(type: "bigint", nullable: true),
                    ServiceType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RateType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RateValue = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    MinimumCharge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    FuelSurcharge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    TollAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    OtherCharges = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_rates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_rates_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_scorecards",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    ScorecardProfile = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    OverallScore = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GeneratedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CalculationVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_scorecards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_scorecards_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Role = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastLoginAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_users_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_vehicles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    RegistrationNumber = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VehicleTypeReference = table.Column<long>(type: "bigint", nullable: false),
                    PayloadCapacityKg = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    UsableVolumeM3 = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    LengthM = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    WidthM = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    HeightM = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    OwnershipType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AvailabilityStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentLocationReference = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_vehicles_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_vehicle_placement_requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LoadReference = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    VehicleTypeReference = table.Column<long>(type: "bigint", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RequiredPlacementAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReportedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PlacedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LoadingStartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExceptionReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_vehicle_placement_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_vehicle_placement_requests_tm_transporters_TransporterId",
                        column: x => x.TransporterId,
                        principalTable: "tm_transporters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_tender_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenderId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EventAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PerformedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Comments = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_tender_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_tender_events_tm_tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "tm_tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_tender_responses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TenderId = table.Column<long>(type: "bigint", nullable: false),
                    TransporterId = table.Column<long>(type: "bigint", nullable: false),
                    Response = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResponseAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    QuotedRate = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    VehicleId = table.Column<long>(type: "bigint", nullable: true),
                    DriverReference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Comments = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_tender_responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_tender_responses_tm_tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "tm_tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_transporter_scorecard_details",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScorecardId = table.Column<long>(type: "bigint", nullable: false),
                    KpiType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KpiValue = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    WeightedScore = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    BenchmarkValue = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    Trend = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_transporter_scorecard_details", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_transporter_scorecard_details_tm_transporter_scorecards_S~",
                        column: x => x.ScorecardId,
                        principalTable: "tm_transporter_scorecards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tm_vehicle_placement_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PlacementRequestId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EventAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    VehicleId = table.Column<long>(type: "bigint", nullable: true),
                    Remarks = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tm_vehicle_placement_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tm_vehicle_placement_events_tm_vehicle_placement_requests_Pl~",
                        column: x => x.PlacementRequestId,
                        principalTable: "tm_vehicle_placement_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "tm_capability_types",
                columns: new[] { "Id", "Code", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1L, "FTL", true, "FTL" },
                    { 2L, "PTL", true, "PTL" },
                    { 3L, "HAZARDOUS", true, "Hazardous" },
                    { 4L, "TEMP_CONTROLLED", true, "Temperature Controlled" },
                    { 5L, "FRAGILE", true, "Fragile" },
                    { 6L, "HEAVY_CARGO", true, "Heavy Cargo" },
                    { 7L, "HIGH_VALUE", true, "High Value" },
                    { 8L, "REVERSE_LOGISTICS", true, "Reverse Logistics" },
                    { 9L, "MILK_RUN", true, "Milk Run" },
                    { 10L, "EXPRESS", true, "Express" },
                    { 11L, "DEDICATED", true, "Dedicated" }
                });

            migrationBuilder.InsertData(
                table: "tm_configuration_settings",
                columns: new[] { "Id", "Key", "UpdatedAt", "UpdatedBy", "ValueJson", "Version" },
                values: new object[,]
                {
                    { 1L, "tm.scorecard.weights.default", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "{\"OnTimePickup\":10,\"OnTimeDelivery\":25,\"PlacementCompliance\":15,\"TenderAcceptance\":10,\"PodCompliance\":5,\"ClaimsRate\":10,\"CostPerformance\":15,\"Availability\":10}", 1 },
                    { 2L, "tm.scorecard.minimumSampleSize", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "20", 1 },
                    { 3L, "tm.tender.responseSlaMinutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "30", 1 },
                    { 4L, "tm.placement.alertMinutesBefore", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "30", 1 },
                    { 5L, "tm.pod.submissionSlaHours", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "24", 1 },
                    { 6L, "tm.compliance.renewalReminderDays", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "30", 1 },
                    { 7L, "tm.performance.thresholds", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "{\"OnTimeDelivery\":{\"Excellent\":95,\"Acceptable\":90},\"PlacementCompliance\":{\"Excellent\":95,\"Acceptable\":90},\"PodCompliance\":{\"Excellent\":98,\"Acceptable\":95}}", 1 },
                    { 8L, "tm.alerts.enabled", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "true", 1 },
                    { 9L, "tm.alerts.delayEscalationMinutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "{\"CoordinatorMinutes\":0,\"ManagerAfterMinutes\":60,\"HeadAfterMinutes\":120}", 1 }
                });

            migrationBuilder.InsertData(
                table: "tm_document_types",
                columns: new[] { "Id", "BlockAllocationWhenExpired", "Code", "ExpiryRequired", "IsActive", "IsDriverLevel", "IsMandatory", "IsTransporterLevel", "IsVehicleLevel", "Name", "RenewalReminderDays", "VerificationRequired" },
                values: new object[,]
                {
                    { 1L, false, "GST_CERT", false, true, false, true, true, false, "GST Certificate", 30, true },
                    { 2L, false, "PAN", false, true, false, true, true, false, "PAN", null, true },
                    { 3L, false, "COMPANY_REG", false, true, false, true, true, false, "Company Registration", null, true },
                    { 4L, true, "INSURANCE", true, true, false, true, false, true, "Insurance", 30, true },
                    { 5L, true, "PERMIT", true, true, false, false, true, true, "Permit", 30, true },
                    { 6L, true, "FITNESS", true, true, false, false, true, true, "Fitness Certificate", 30, true },
                    { 7L, false, "PUC", true, true, false, false, true, true, "PUC", 15, true },
                    { 8L, false, "VEHICLE_RC", false, true, false, false, false, true, "Vehicle RC", null, true },
                    { 9L, true, "DRIVER_LICENCE", true, true, true, false, false, false, "Driver Licence", 30, true },
                    { 10L, false, "OTHER", false, true, false, false, true, false, "Other", null, true }
                });

            migrationBuilder.InsertData(
                table: "tm_transporter_types",
                columns: new[] { "Id", "Code", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1L, "FTL", true, "Full Truck Load" },
                    { 2L, "PTL", true, "Part Truck Load" },
                    { 3L, "EXPRESS", true, "Express" },
                    { 4L, "DEDICATED", true, "Dedicated" },
                    { 5L, "LAST_MILE", true, "Last Mile" },
                    { 6L, "MILK_RUN", true, "Milk Run" },
                    { 7L, "FLEET_OWNER", true, "Fleet Owner" },
                    { 8L, "BROKER", true, "Broker" },
                    { 9L, "3PL", true, "Third-Party Logistics" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_tm_capability_types_Code",
                table: "tm_capability_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_configuration_settings_Key",
                table: "tm_configuration_settings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_document_types_Code",
                table: "tm_document_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_tender_events_TenderId",
                table: "tm_tender_events",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_tender_responses_TenderId",
                table: "tm_tender_responses",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_tenders_ResponseDeadline",
                table: "tm_tenders",
                column: "ResponseDeadline");

            migrationBuilder.CreateIndex(
                name: "IX_tm_tenders_Status",
                table: "tm_tenders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_tm_tenders_TenderNumber",
                table: "tm_tenders",
                column: "TenderNumber");

            migrationBuilder.CreateIndex(
                name: "IX_tm_tenders_TransporterId",
                table: "tm_tenders",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_alerts_DueAt",
                table: "tm_transporter_alerts",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_alerts_Severity",
                table: "tm_transporter_alerts",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_alerts_Status",
                table: "tm_transporter_alerts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_alerts_TransporterId",
                table: "tm_transporter_alerts",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_audit_logs_EntityType_EntityId",
                table: "tm_transporter_audit_logs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_audit_logs_PerformedAt",
                table: "tm_transporter_audit_logs",
                column: "PerformedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_branches_TransporterId_BranchCode",
                table: "tm_transporter_branches",
                columns: new[] { "TransporterId", "BranchCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_capabilities_CapabilityTypeId",
                table: "tm_transporter_capabilities",
                column: "CapabilityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_capabilities_TransporterId_CapabilityTypeId",
                table: "tm_transporter_capabilities",
                columns: new[] { "TransporterId", "CapabilityTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_contacts_TransporterId",
                table: "tm_transporter_contacts",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_documents_DocumentTypeId",
                table: "tm_transporter_documents",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_documents_ExpiryDate",
                table: "tm_transporter_documents",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_documents_TransporterId",
                table: "tm_transporter_documents",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_documents_VerificationStatus",
                table: "tm_transporter_documents",
                column: "VerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_exceptions_Severity",
                table: "tm_transporter_exceptions",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_exceptions_Status",
                table: "tm_transporter_exceptions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_exceptions_TransporterId",
                table: "tm_transporter_exceptions",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_lanes_OriginLocationReference_DestinationLoca~",
                table: "tm_transporter_lanes",
                columns: new[] { "OriginLocationReference", "DestinationLocationReference", "ServiceType" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_lanes_TransporterId",
                table: "tm_transporter_lanes",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_performance_kpis_LaneReference",
                table: "tm_transporter_performance_kpis",
                column: "LaneReference");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_performance_kpis_TransporterId",
                table: "tm_transporter_performance_kpis",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_performance_kpis_TransporterId_KpiType_Period~",
                table: "tm_transporter_performance_kpis",
                columns: new[] { "TransporterId", "KpiType", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_planning_feedback_TransporterId_LaneReference",
                table: "tm_transporter_planning_feedback",
                columns: new[] { "TransporterId", "LaneReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_planning_rules_TransporterId_RuleType",
                table: "tm_transporter_planning_rules",
                columns: new[] { "TransporterId", "RuleType" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_rankings_LaneReference_PeriodStart_PeriodEnd_~",
                table: "tm_transporter_rankings",
                columns: new[] { "LaneReference", "PeriodStart", "PeriodEnd", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_rankings_TransporterId_PeriodStart_PeriodEnd",
                table: "tm_transporter_rankings",
                columns: new[] { "TransporterId", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_rates_OriginLocationReference_DestinationLoca~",
                table: "tm_transporter_rates",
                columns: new[] { "OriginLocationReference", "DestinationLocationReference", "ServiceType" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_rates_TransporterId",
                table: "tm_transporter_rates",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_scorecard_details_ScorecardId",
                table: "tm_transporter_scorecard_details",
                column: "ScorecardId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_scorecards_TransporterId_PeriodStart_PeriodEnd",
                table: "tm_transporter_scorecards",
                columns: new[] { "TransporterId", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_types_Code",
                table: "tm_transporter_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_users_TransporterId",
                table: "tm_transporter_users",
                column: "TransporterId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_users_Username",
                table: "tm_transporter_users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_vehicles_AvailabilityStatus",
                table: "tm_transporter_vehicles",
                column: "AvailabilityStatus");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_vehicles_TransporterId_RegistrationNumber",
                table: "tm_transporter_vehicles",
                columns: new[] { "TransporterId", "RegistrationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporter_vehicles_VehicleTypeReference",
                table: "tm_transporter_vehicles",
                column: "VehicleTypeReference");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporters_Gstin",
                table: "tm_transporters",
                column: "Gstin");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporters_Status",
                table: "tm_transporters",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporters_TransporterCode",
                table: "tm_transporters",
                column: "TransporterCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tm_transporters_TransporterTypeId",
                table: "tm_transporters",
                column: "TransporterTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_vehicle_placement_events_PlacementRequestId",
                table: "tm_vehicle_placement_events",
                column: "PlacementRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_tm_vehicle_placement_requests_LoadReference",
                table: "tm_vehicle_placement_requests",
                column: "LoadReference");

            migrationBuilder.CreateIndex(
                name: "IX_tm_vehicle_placement_requests_Status",
                table: "tm_vehicle_placement_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_tm_vehicle_placement_requests_TransporterId",
                table: "tm_vehicle_placement_requests",
                column: "TransporterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tm_configuration_settings");

            migrationBuilder.DropTable(
                name: "tm_tender_events");

            migrationBuilder.DropTable(
                name: "tm_tender_responses");

            migrationBuilder.DropTable(
                name: "tm_transporter_alerts");

            migrationBuilder.DropTable(
                name: "tm_transporter_audit_logs");

            migrationBuilder.DropTable(
                name: "tm_transporter_branches");

            migrationBuilder.DropTable(
                name: "tm_transporter_capabilities");

            migrationBuilder.DropTable(
                name: "tm_transporter_contacts");

            migrationBuilder.DropTable(
                name: "tm_transporter_documents");

            migrationBuilder.DropTable(
                name: "tm_transporter_exceptions");

            migrationBuilder.DropTable(
                name: "tm_transporter_lanes");

            migrationBuilder.DropTable(
                name: "tm_transporter_performance_kpis");

            migrationBuilder.DropTable(
                name: "tm_transporter_planning_feedback");

            migrationBuilder.DropTable(
                name: "tm_transporter_planning_rules");

            migrationBuilder.DropTable(
                name: "tm_transporter_rankings");

            migrationBuilder.DropTable(
                name: "tm_transporter_rates");

            migrationBuilder.DropTable(
                name: "tm_transporter_scorecard_details");

            migrationBuilder.DropTable(
                name: "tm_transporter_users");

            migrationBuilder.DropTable(
                name: "tm_transporter_vehicles");

            migrationBuilder.DropTable(
                name: "tm_vehicle_placement_events");

            migrationBuilder.DropTable(
                name: "tm_tenders");

            migrationBuilder.DropTable(
                name: "tm_capability_types");

            migrationBuilder.DropTable(
                name: "tm_document_types");

            migrationBuilder.DropTable(
                name: "tm_transporter_scorecards");

            migrationBuilder.DropTable(
                name: "tm_vehicle_placement_requests");

            migrationBuilder.DropTable(
                name: "tm_transporters");

            migrationBuilder.DropTable(
                name: "tm_transporter_types");
        }
    }
}
