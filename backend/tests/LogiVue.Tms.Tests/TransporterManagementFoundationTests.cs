using FluentAssertions;
using LogiVue.Tms.Api.Common;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using LogiVue.Tms.TransporterManagement.Infrastructure.Services;
using LogiVue.Tms.TransporterManagement.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Milestone 1 checks: table naming for merge safety, seed data, settings, audit and module wiring.</summary>
public class TransporterManagementFoundationTests
{
    private static TransporterDbContext CreateContext(string? name = null)
    {
        var provider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<TransporterDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .UseInternalServiceProvider(provider)
            .Options;

        var db = new TransporterDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public void Every_module_table_uses_the_tm_prefix_and_is_unique()
    {
        using var db = CreateContext();

        var tables = db.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .ToList();

        tables.Should().OnlyContain(name => name!.StartsWith("tm_"));
        tables.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Module_has_no_foreign_keys_into_unknown_planning_tables()
    {
        using var db = CreateContext();

        var foreignKeyTargets = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .Select(fk => fk.PrincipalEntityType.GetTableName())
            .Distinct()
            .ToList();

        foreignKeyTargets.Should().OnlyContain(name => name!.StartsWith("tm_"));
    }

    [Fact]
    public void Seed_data_provides_lookups_and_business_settings()
    {
        using var db = CreateContext();

        db.TransporterTypes.Count().Should().Be(9);
        db.CapabilityTypes.Count().Should().Be(11);
        db.DocumentTypes.Count().Should().Be(10);
        db.ConfigurationSettings.Count().Should().Be(17);
        db.DocumentTypes.Single(d => d.Code == "INSURANCE").BlockAllocationWhenExpired.Should().BeTrue();
    }

    [Fact]
    public async Task Settings_reader_returns_kpi_weights_that_sum_to_one_hundred()
    {
        using var db = CreateContext();
        var settings = new TransporterSettings(db);

        var weights = await settings.GetAsync<KpiWeightsSetting>(SettingKeys.KpiWeightsDefault);

        var total = weights.OnTimePickup + weights.OnTimeDelivery + weights.PlacementCompliance
                    + weights.TenderAcceptance + weights.PodCompliance + weights.ClaimsRate
                    + weights.CostPerformance + weights.Availability;
        total.Should().Be(100);
    }

    [Fact]
    public async Task Settings_reader_fails_clearly_for_unknown_key()
    {
        using var db = CreateContext();
        var settings = new TransporterSettings(db);

        var act = () => settings.GetAsync<KpiWeightsSetting>("tm.does.not.exist");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tm.does.not.exist*");
    }

    [Fact]
    public async Task Audit_entries_commit_with_the_business_change()
    {
        using var db = CreateContext();
        var user = new StubUser("ops.manager@logivue.test");
        var audit = new AuditTrail(db, user, NullLogger<AuditTrail>.Instance);

        await audit.RecordAsync(new AuditEntry("Transporter", "101", "Approved", Reason: "All documents verified"));
        await db.SaveChangesAsync();

        var row = await db.AuditLogs.SingleAsync();
        row.Action.Should().Be("Approved");
        row.PerformedBy.Should().Be("ops.manager@logivue.test");
        row.Reason.Should().Be("All documents verified");
    }

    [Fact]
    public async Task Module_info_endpoint_is_reachable_through_the_host()
    {
        using var factory = new TestHost();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/transporter-management/info");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("TransporterManagement").And.Contain("tm_*");
    }

    private sealed class StubUser(string userId) : ICurrentUser
    {
        public string UserId { get; } = userId;
        public string DisplayName => UserId;
        public long? TransporterId => null;
        public IReadOnlyCollection<string> Roles { get; } = [global::LogiVue.Tms.Shared.Authorization.Roles.OperationsUser];
        public bool IsInRole(string role) => Roles.Contains(role);
    }
}
