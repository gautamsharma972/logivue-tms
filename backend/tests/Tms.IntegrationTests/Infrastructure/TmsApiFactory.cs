using Microsoft.AspNetCore.Hosting;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tms.Modules.Platform.Application;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Files;
using Tms.SharedKernel.Messaging;
using Tms.SharedKernel.Security;

namespace Tms.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against a real MySQL database (<c>tms_test</c>), recreated from migrations once per test run.
/// Override the server with the TMS_TEST_CONNECTION environment variable (e.g. in CI).
/// </summary>
public sealed class TmsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DemoTenant = "DEMO";
    public const string DemoAdminEmail = "admin@demo.tms";
    public const string DemoAdminPassword = "Admin@12345678";
    public const string Approver1 = "test.approve.l1";
    public const string Approver2 = "test.approve.l2";
    public const string OtherTenant = "ACME";
    public const string OtherAdminEmail = "admin@acme.tms";
    public const string OtherAdminPassword = "Acme@12345678";

    public static string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"tms-test-storage-{Guid.NewGuid():N}");

    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("TMS_TEST_CONNECTION")
        ?? "Server=localhost;Port=3306;Database=tms_test;User=tms_app;Password=tms_dev_password;";

    public async ValueTask InitializeAsync()
    {
        // Start every run from an empty schema so tests never depend on leftovers.
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PlatformDbContextOptions.Configure(options, ConnectionString, new Version(8, 4, 0));
        await using (var db = new PlatformDbContext(options.Options, SystemUser.Instance))
        {
            await db.Database.EnsureDeletedAsync();
        }

        _ = Services; // boots the host: migrates and seeds the DEMO tenant
        await SeedSecondTenantAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            if (Directory.Exists(StorageRoot))
            {
                Directory.Delete(StorageRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort: leftover temp files are harmless
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting (not ConfigureAppConfiguration): Program.cs reads configuration eagerly while registering services,
        // before late configuration callbacks are applied under minimal hosting.
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:Default"] = ConnectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["Database:SeedDemoData"] = "true",
            ["Database:DemoAdminEmail"] = DemoAdminEmail,
            ["Database:DemoAdminPassword"] = DemoAdminPassword,
            ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789abcdef",
            ["RateLimiting:AuthPermitsPerMinute"] = "100000",
            ["Storage:RootPath"] = StorageRoot,
            ["FieldEncryption:Keys:1"] = "I7SO9wGeyFMUYDhd81VLYuK+M9qY2I6xsmExlE6aGV4=",
            ["Contracts:LifecycleEnabled"] = "false",
            ["Tracking:HealthCheckMinimumSeconds"] = "0",
            ["Reports:WorkerPollSeconds"] = "1",
            ["Reports:SchedulerPollSeconds"] = "5",
            ["Reports:AggregationEnabled"] = "false",
            ["Outbox:PollSeconds"] = "1",
            ["Outbox:MinAgeSeconds"] = "1",
            ["Outbox:FirstRetrySeconds"] = "1",
            ["Outbox:MaxAttempts"] = "3",
        };

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            // Approver permissions that real modules will define later, so approval chains can be exercised now.
            services.AddSingleton(new PermissionDefinition(Approver1, "Test", "Test approver, level 1"));
            services.AddSingleton(new PermissionDefinition(Approver2, "Test", "Test approver, level 2"));

            // Observes the integration event other modules would react to.
            services.AddSingleton<ApprovalEventLog>();
            services.AddScoped<IDomainEventHandler<ApprovalCompleted>, ApprovalEventRecorder>();
            services.AddSingleton<CapturingEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
            services.AddSingleton<IFileScanner, MarkerFileScanner>();
            services.AddScoped<ITrackingPlanningIntegration, TestTrackingPlanning>();
            services.AddSingleton<TrackingEventLog>();
            services.AddScoped<IDomainEventHandler<TrackingStarted>, TrackingEventRecorder<TrackingStarted>>();
            services.AddScoped<IDomainEventHandler<TrackingStopped>, TrackingEventRecorder<TrackingStopped>>();
            services.AddScoped<IDomainEventHandler<TrackingStale>, TrackingEventRecorder<TrackingStale>>();
            services.AddScoped<IDomainEventHandler<TrackingLost>, TrackingEventRecorder<TrackingLost>>();
            services.AddScoped<IDomainEventHandler<TrackingVehicleArrived>, TrackingEventRecorder<TrackingVehicleArrived>>();
            services.AddScoped<IDomainEventHandler<TrackingVehicleDeparted>, TrackingEventRecorder<TrackingVehicleDeparted>>();
            services.AddScoped<IDomainEventHandler<EnteredGeofence>, TrackingEventRecorder<EnteredGeofence>>();
            services.AddScoped<IDomainEventHandler<ExitedGeofence>, TrackingEventRecorder<ExitedGeofence>>();
            services.AddScoped<IDomainEventHandler<RouteDeviationDetected>, TrackingEventRecorder<RouteDeviationDetected>>();
            services.AddScoped<IDomainEventHandler<RouteDeviationResolved>, TrackingEventRecorder<RouteDeviationResolved>>();
            services.AddScoped<IDomainEventHandler<ExcessiveDwellDetected>, TrackingEventRecorder<ExcessiveDwellDetected>>();
            services.AddScoped<IDomainEventHandler<ShipmentAtRisk>, TrackingEventRecorder<ShipmentAtRisk>>();
            services.AddScoped<IDomainEventHandler<ShipmentDelayed>, TrackingEventRecorder<ShipmentDelayed>>();
            services.AddScoped<IDomainEventHandler<DeliveryTrackingEvent>, TrackingEventRecorder<DeliveryTrackingEvent>>();
            services.AddScoped<IDomainEventHandler<TrackingPerformanceEvent>, TrackingEventRecorder<TrackingPerformanceEvent>>();
            services.AddSingleton<FlakySubscriberState>();
            services.AddScoped<IDomainEventHandler<ApprovalCompleted>, FlakySubscriber>();
        });
    }

    /// <summary>Creates an independent tenant so isolation can be proven against real data.</summary>
    private async Task SeedSecondTenantAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var catalog = scope.ServiceProvider.GetRequiredService<PermissionCatalog>();

        var tenant = Tenant.Create(OtherTenant, "Acme Freight Ltd");
        var admin = Role.Create(tenant.Id, Role.AdministratorName, null, catalog.Codes, isSystem: true);
        var user = User.Create(tenant.Id, OtherAdminEmail, "Acme Administrator", passwords.Hash(OtherAdminPassword), UserType.Internal, [admin]);
        db.AddRange(tenant, admin, user);
        await db.SaveChangesAsync();
    }
}

public sealed class ApprovalEventLog
{
    public ConcurrentQueue<ApprovalCompleted> Events { get; } = new();
}

internal sealed class ApprovalEventRecorder(ApprovalEventLog log) : IDomainEventHandler<ApprovalCompleted>
{
    public Task HandleAsync(ApprovalCompleted domainEvent, CancellationToken cancellationToken)
    {
        log.Events.Enqueue(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>A stand-in for a real malware scanner: refuses any file that contains the marker, so the hook can be proven without a virus.</summary>
public sealed class MarkerFileScanner : IFileScanner
{
    public const string Marker = "TEST-MALWARE-MARKER";

    public Task<FileScanResult> ScanAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken) =>
        Task.FromResult(content.Span.IndexOf(System.Text.Encoding.ASCII.GetBytes(Marker)) >= 0 ? new FileScanResult(false, "The file was refused by the security scan.") : FileScanResult.Clean);
}

public sealed class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The single-use token from the most recent email to the address (the part after <c>token=</c>).</summary>
    public string? LatestTokenFor(string email) =>
        Sent.Where(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase))
            .Select(m => System.Text.RegularExpressions.Regex.Match(m.TextBody, @"token=([^\s&]+)"))
            .Where(m => m.Success).Select(m => Uri.UnescapeDataString(m.Groups[1].Value)).LastOrDefault();
}

/// <summary>Document ids registered here make <see cref="FlakySubscriber"/> fail a set number of times before succeeding.</summary>
public sealed class FlakySubscriberState
{
    public ConcurrentDictionary<Guid, int> FailuresRemaining { get; } = new();

    public ConcurrentDictionary<Guid, (int Attempts, Guid? Tenant, Guid? User)> Seen { get; } = new();
}

internal sealed class FlakySubscriber(FlakySubscriberState state, ICurrentUser currentUser) : IDomainEventHandler<ApprovalCompleted>
{
    public Task HandleAsync(ApprovalCompleted domainEvent, CancellationToken cancellationToken)
    {
        if (!state.FailuresRemaining.TryGetValue(domainEvent.DocumentId, out var remaining))
        {
            return Task.CompletedTask;
        }

        var attempts = state.Seen.AddOrUpdate(domainEvent.DocumentId, (1, currentUser.TenantId, currentUser.UserId), (_, old) => (old.Attempts + 1, currentUser.TenantId, currentUser.UserId)).Attempts;
        if (attempts <= remaining)
        {
            throw new InvalidOperationException($"Simulated subscriber failure #{attempts}");
        }

        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<TmsApiFactory>
{
    public const string Name = "api";
}
