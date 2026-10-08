using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tms.BuildingBlocks.Web;
using Tms.Modules.Tracking.Application;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Application.Mobile;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Endpoints;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.Modules.Tracking.Integration;
using Tms.SharedKernel;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Tracking;

/// <summary>
/// Public surface of the Tracking module: where each trip's vehicle is, how well tracking is working, whether the trip is on its route and on time, and what needs doing about it.
/// Tables are prefixed <c>st_</c>. It knows other modules only through shared contracts: planning supplies the trip, and everything it tells the others is an event.
/// </summary>
public static class TrackingModule
{
    public static IServiceCollection AddTrackingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedKernel();
        services.AddDomainEventTypes(typeof(TrackingModule).Assembly);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var serverVersion = Version.Parse(configuration["Database:ServerVersion"] ?? "8.4.0");

        services.AddDbContext<TrackingDbContext>((sp, options) =>
        {
            TrackingDbContextOptions.Configure(options, connectionString, serverVersion);
            options.UseTmsInterceptors(sp);
        });

        foreach (var permission in TrackingPermissions.All)
        {
            services.AddSingleton(permission);
        }

        services.AddScoped<TrackingAccess>();
        services.AddScoped<ITrackingSettings, TrackingSettings>();
        services.AddScoped<PendingEvents>();
        services.AddScoped<Timeline>();

        // The engine. Each step is its own service behind an interface so it can be replaced or tested alone, and every location source feeds the same pipeline.
        services.AddScoped<ITrackingEventPublisher, OutboxTrackingEventPublisher>();
        services.AddScoped<ITrackingTransporterIntegration, OutboxTrackingTransporterIntegration>();
        services.AddScoped<ITrackingDeliveryIntegration, OutboxTrackingDeliveryIntegration>();
        services.AddScoped<ITrackingClaimsIntegration, TrackingClaimsEvidence>();
        services.AddScoped<ITrackingPositionFeed, TrackingPositionFeed>();
        services.AddScoped<ITrackingReportingProvider, Integration.TrackingReportingProvider>();
        services.AddScoped<ITrackingNotificationService, InAppTrackingNotificationService>();
        services.TryAddSingleton<ITrackingLiveNotifier, SignalRTrackingLiveNotifier>();
        services.AddScoped<ITrackingAlertService, TrackingAlertService>();
        services.AddScoped<IMilestoneDetectionService, MilestoneService>();
        services.AddScoped<IGeofenceService, GeofenceService>();
        services.AddScoped<IRouteDeviationService, RouteDeviationService>();
        services.AddScoped<IDwellDetectionService, DwellDetectionService>();
        services.AddScoped<IShipmentEtaService, ShipmentEtaService>();
        services.AddScoped<ITrackingContextLoader, TrackingContextLoader>();
        services.AddScoped<TrackedShipmentFactory>();
        services.AddScoped<LocationPipeline>();
        services.AddScoped<SessionService>();
        services.AddScoped<TrackingHealthMonitor>();
        services.AddScoped<RouteHistory>();
        services.AddScoped<RetentionService>();
        services.AddHostedService<RetentionWorker>();
        services.AddScoped<ITrackingLocationProvider, MobileTrackingLocationProvider>();

        // Local stand-in for planning, used only until Shipments (or another module) provides the real one: the first registration wins.
        services.TryAddScoped<ITrackingPlanningIntegration, LocalTrackingPlanningIntegration>();
        services.AddScoped<IDomainEventHandler<ShipmentDispatched>, ShipmentDispatchedSubscriber>();
        services.AddScoped<IDomainEventHandler<ShipmentDelivered>, ShipmentDeliveredSubscriber>();
        services.AddScoped<IDomainEventHandler<DeliveryCompleted>, DeliveryCompletedSubscriber>();
        services.AddHandlers(typeof(TrackingModule).Assembly, "Tms.Modules.Tracking.Application");
        services.AddValidatorsFrom<TrackingDbContext>();

        services.AddSignalR();
        // A browser cannot set a header on a WebSocket, so the hub accepts the access token in the query string, for the hub path only.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var existing = options.Events?.OnMessageReceived;
            options.Events ??= new JwtBearerEvents();
            options.Events.OnMessageReceived = async context =>
            {
                if (existing is not null)
                {
                    await existing(context);
                }

                var token = context.Request.Query["access_token"].ToString();
                if (string.IsNullOrEmpty(context.Token) && !string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(HubPath))
                {
                    context.Token = token;
                }
            };
        });

        services.Configure<RateLimiterOptions>(options => options.AddPolicy(TrackingEndpoints.PublicRateLimit, context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = configuration.GetValue("RateLimiting:PublicTrackingPermitsPerMinute", 60), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            })));

        services.AddScoped<ITrackingDemoSeeder, TrackingDemoSeeder>();
        return services;
    }

    public const string HubPath = "/hubs/tracking";

    public static IEndpointRouteBuilder MapTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        TrackingEndpoints.Map(app);
        app.MapHub<TrackingHub>(HubPath);
        return app;
    }

    /// <summary>Applies this module's migrations when enabled (development / test); production migrates as a deploy step.</summary>
    public static async Task InitialiseTrackingAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TrackingDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

/// <summary>Builds believable trips for a demonstration through the same pipeline real locations use. Development and test only: the host maps it behind a dev-only endpoint.</summary>
public interface ITrackingDemoSeeder
{
    Task<DemoSeedResult> SeedAsync(DemoSeedRequest request, CancellationToken cancellationToken);
}

/// <summary>The carriers the demo trips are given to (the seeder knows no carriers of its own).</summary>
public sealed record DemoCarrier(Guid TransporterId, string Name);

public sealed record DemoSeedRequest(IReadOnlyList<DemoCarrier> Carriers);

public sealed record DemoSeedResult(int Shipments, int Locations, int Alerts, int Exceptions, string Message);
