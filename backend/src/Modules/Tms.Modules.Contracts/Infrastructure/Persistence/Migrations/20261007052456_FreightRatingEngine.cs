using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Modules.Contracts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FreightRatingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "contracts",
                table: "rate_cards",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "dph_rule_code",
                schema: "contracts",
                table: "rate_cards",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "max_volume_cbm",
                schema: "contracts",
                table: "rate_cards",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_weight_kg",
                schema: "contracts",
                table: "rate_cards",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "maximum_charge",
                schema: "contracts",
                table: "rate_cards",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_volume_cbm",
                schema: "contracts",
                table: "rate_cards",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_weight_kg",
                schema: "contracts",
                table: "rate_cards",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "minimum_charge",
                schema: "contracts",
                table: "rate_cards",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "contracts",
                table: "rate_cards",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "priority",
                schema: "contracts",
                table: "rate_cards",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<string>(
                name: "required_capabilities",
                schema: "contracts",
                table: "rate_cards",
                type: "json",
                nullable: false,
                defaultValueSql: "(JSON_ARRAY())")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateOnly>(
                name: "valid_from",
                schema: "contracts",
                table: "rate_cards",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "valid_to",
                schema: "contracts",
                table: "rate_cards",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "contracts",
                table: "rate_cards",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "document_version",
                schema: "contracts",
                table: "documents",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_date",
                schema: "contracts",
                table: "documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "contracts",
                table: "documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "issue_date",
                schema: "contracts",
                table: "documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "number",
                schema: "contracts",
                table: "documents",
                type: "varchar(60)",
                maxLength: 60,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "contracts",
                table: "documents",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Pending")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at",
                schema: "contracts",
                table: "documents",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "verified_by",
                schema: "contracts",
                table: "documents",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "contracts",
                table: "diesel_prices",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "auto_renewal",
                schema: "contracts",
                table: "contracts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "business_unit",
                schema: "contracts",
                table: "contracts",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "calculation_version",
                schema: "contracts",
                table: "contracts",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "1.0")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "contracts",
                table: "contracts",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "INR")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "primary_contact",
                schema: "contracts",
                table: "contracts",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "renewal_notice_days",
                schema: "contracts",
                table: "contracts",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "revision_kind",
                schema: "contracts",
                table: "contracts",
                type: "varchar(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Original")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "services",
                schema: "contracts",
                table: "contracts",
                type: "json",
                nullable: false,
                defaultValueSql: "(JSON_ARRAY())")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "suspensions",
                schema: "contracts",
                table: "contracts",
                type: "json",
                nullable: false,
                defaultValueSql: "(JSON_ARRAY())")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "accessorial_types",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    calc = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
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
                    table.PrimaryKey("pk_accessorial_types", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contract_accessorials",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    spec = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_accessorials", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_accessorials_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "contracts",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contract_capacity",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    spec = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_capacity", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_capacity_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "contracts",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contract_sla",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    spec = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_sla", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_sla_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "contracts",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "dph_rules",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    version = table.Column<int>(type: "int", nullable: false),
                    spec = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dph_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_dph_rules_contracts_contract_id",
                        column: x => x.contract_id,
                        principalSchema: "contracts",
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "dph_snapshots",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    rule_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    rule_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rule_version = table.Column<int>(type: "int", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    reference_price = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    variation_percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    adjustment_percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    calculation_version = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dph_snapshots", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "import_batches",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    contract_number = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    applied_contract_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    row_count = table.Column<int>(type: "int", nullable: false),
                    error_rows = table.Column<int>(type: "int", nullable: false),
                    warning_rows = table.Column<int>(type: "int", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batches", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ratings",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    shipment_reference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    committed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    qualified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    error_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transporter_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    contract_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    contract_reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    contract_revision = table.Column<int>(type: "int", nullable: true),
                    rate_card_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    rate_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rate_version = table.Column<int>(type: "int", nullable: true),
                    dph_rule_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    dph_rule_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    dph_version = table.Column<int>(type: "int", nullable: true),
                    lane = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    service = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    vehicle_type_id = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    weight_kg = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    volume_cbm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    distance_km = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    stop_count = table.Column<int>(type: "int", nullable: false),
                    shipment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    base_freight = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    dph_adjustment = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    accessorial_amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    total_freight = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    calculation_version = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    calculated_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    input_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    trace_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reasons_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    override_amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    override_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    override_approved_by = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    overridden_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    overridden_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    modified_at = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    modified_by = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ratings", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "import_rows",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    batch_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    row_number = table.Column<int>(type: "int", nullable: false),
                    values = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issues = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_rows_import_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "contracts",
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "rating_components",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    rating_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: true),
                    unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rate = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rating_components", x => x.id);
                    table.ForeignKey(
                        name: "fk_rating_components_ratings_rating_id",
                        column: x => x.rating_id,
                        principalSchema: "contracts",
                        principalTable: "ratings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "rating_exclusions",
                schema: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    tenant_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    rating_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    contract_reference = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rate_reference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason_code = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rating_exclusions", x => x.id);
                    table.ForeignKey(
                        name: "fk_rating_exclusions_ratings_rating_id",
                        column: x => x.rating_id,
                        principalSchema: "contracts",
                        principalTable: "ratings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Existing rate cards have no code yet: give each a unique one before the unique index is built.
            migrationBuilder.Sql(
                "UPDATE contracts_rate_cards SET code = CONCAT('RATE-', UPPER(SUBSTRING(REPLACE(CAST(id AS CHAR), '-', ''), 21, 12))) WHERE code = '';");

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_contract_id_code",
                schema: "contracts",
                table: "rate_cards",
                columns: new[] { "contract_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_tenant_id_origin_city_destination_city",
                schema: "contracts",
                table: "rate_cards",
                columns: new[] { "tenant_id", "origin_city", "destination_city" });

            migrationBuilder.CreateIndex(
                name: "ix_rate_cards_tenant_id_vehicle_type_id",
                schema: "contracts",
                table: "rate_cards",
                columns: new[] { "tenant_id", "vehicle_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_tenant_id_expiry_date",
                schema: "contracts",
                table: "documents",
                columns: new[] { "tenant_id", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_accessorial_types_tenant_id_code",
                schema: "contracts",
                table: "accessorial_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_accessorials_contract_id",
                schema: "contracts",
                table: "contract_accessorials",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_capacity_contract_id",
                schema: "contracts",
                table: "contract_capacity",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_sla_contract_id",
                schema: "contracts",
                table: "contract_sla",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_dph_rules_contract_id_version",
                schema: "contracts",
                table: "dph_rules",
                columns: new[] { "contract_id", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_dph_snapshots_rule_id_period_start",
                schema: "contracts",
                table: "dph_snapshots",
                columns: new[] { "rule_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dph_snapshots_tenant_id_contract_id",
                schema: "contracts",
                table: "dph_snapshots",
                columns: new[] { "tenant_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_tenant_id_reference",
                schema: "contracts",
                table: "import_batches",
                columns: new[] { "tenant_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_tenant_id_status",
                schema: "contracts",
                table: "import_batches",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_batch_id_row_number",
                schema: "contracts",
                table: "import_rows",
                columns: new[] { "batch_id", "row_number" });

            migrationBuilder.CreateIndex(
                name: "ix_rating_components_rating_id_sequence",
                schema: "contracts",
                table: "rating_components",
                columns: new[] { "rating_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_rating_exclusions_rating_id",
                schema: "contracts",
                table: "rating_exclusions",
                column: "rating_id");

            migrationBuilder.CreateIndex(
                name: "ix_ratings_tenant_id_contract_id_rate_card_id",
                schema: "contracts",
                table: "ratings",
                columns: new[] { "tenant_id", "contract_id", "rate_card_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ratings_tenant_id_qualified_calculated_at",
                schema: "contracts",
                table: "ratings",
                columns: new[] { "tenant_id", "qualified", "calculated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ratings_tenant_id_reference",
                schema: "contracts",
                table: "ratings",
                columns: new[] { "tenant_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ratings_tenant_id_shipment_reference",
                schema: "contracts",
                table: "ratings",
                columns: new[] { "tenant_id", "shipment_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_ratings_tenant_id_transporter_id_calculated_at",
                schema: "contracts",
                table: "ratings",
                columns: new[] { "tenant_id", "transporter_id", "calculated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accessorial_types",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "contract_accessorials",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "contract_capacity",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "contract_sla",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "dph_rules",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "dph_snapshots",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "import_rows",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "rating_components",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "rating_exclusions",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "import_batches",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "ratings",
                schema: "contracts");

            migrationBuilder.DropIndex(
                name: "ix_rate_cards_contract_id_code",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropIndex(
                name: "ix_rate_cards_tenant_id_origin_city_destination_city",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropIndex(
                name: "ix_rate_cards_tenant_id_vehicle_type_id",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropIndex(
                name: "ix_documents_tenant_id_expiry_date",
                schema: "contracts",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "dph_rule_code",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "max_volume_cbm",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "max_weight_kg",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "maximum_charge",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "min_volume_cbm",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "min_weight_kg",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "minimum_charge",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "priority",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "required_capabilities",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "valid_from",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "valid_to",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "contracts",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "document_version",
                schema: "contracts",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "effective_date",
                schema: "contracts",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "contracts",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "issue_date",
                schema: "contracts",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "number",
                schema: "contracts",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "contracts",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "verified_at",
                schema: "contracts",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "verified_by",
                schema: "contracts",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "contracts",
                table: "diesel_prices");

            migrationBuilder.DropColumn(
                name: "auto_renewal",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "business_unit",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "calculation_version",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "primary_contact",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "renewal_notice_days",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "revision_kind",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "services",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "suspensions",
                schema: "contracts",
                table: "contracts");
        }
    }
}
