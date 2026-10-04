using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EligibilityAndRecommendation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "tm_configuration_settings",
                columns: new[] { "Id", "Key", "UpdatedAt", "UpdatedBy", "ValueJson", "Version" },
                values: new object[,]
                {
                    { 10L, "tm.recommendation.weights", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "{\"Rate\":30,\"OnTimePickup\":20,\"OnTimeDelivery\":15,\"PlacementCompliance\":10,\"PodCompliance\":5,\"TenderAcceptance\":5,\"ClaimsRate\":5,\"Availability\":10}", 1 },
                    { 11L, "tm.recommendation.scoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "{\"ClaimsZeroAtPct\":5,\"AvailabilityPerVehicle\":40,\"PreferredBonus\":5,\"InsufficientDataScore\":60}", 1 },
                    { 12L, "tm.performance.windowDays", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "90", 1 },
                    { 13L, "tm.eligibility.restrictions", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "{\"Enabled\":false,\"MinOtdPct\":90,\"MaxClaimsRatePct\":3}", 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "tm_configuration_settings",
                keyColumn: "Id",
                keyValue: 10L);

            migrationBuilder.DeleteData(
                table: "tm_configuration_settings",
                keyColumn: "Id",
                keyValue: 11L);

            migrationBuilder.DeleteData(
                table: "tm_configuration_settings",
                keyColumn: "Id",
                keyValue: 12L);

            migrationBuilder.DeleteData(
                table: "tm_configuration_settings",
                keyColumn: "Id",
                keyValue: 13L);
        }
    }
}
