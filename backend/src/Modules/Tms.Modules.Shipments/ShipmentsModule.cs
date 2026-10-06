using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tms.BuildingBlocks.Web;
using FluentValidation;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Endpoints;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments;

/// <summary>Public surface of the Shipments module: orders, load planning, tendering to transporters, dispatch and lorry receipts.</summary>
public static class ShipmentsModule
{
    public static IServiceCollection AddShipmentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedKernel();
        services.AddDomainEventTypes(typeof(ShipmentsModule).Assembly);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var serverVersion = Version.Parse(configuration["Database:ServerVersion"] ?? "8.4.0");

        services.AddDbContext<ShipmentsDbContext>((sp, options) =>
        {
            ShipmentsDbContextOptions.Configure(options, connectionString, serverVersion);
            options.UseTmsInterceptors(sp);
        });

        foreach (var permission in ShipmentPermissions.All)
        {
            services.AddSingleton(permission);
        }

        services.AddScoped<ShipmentAccess>();
        services.AddScoped<IPlanningOptimizer, RuleBasedPlanningOptimizer>();
        services.AddScoped<Application.PlanningRuns.PlanInputBuilder>();
        services.AddScoped<Application.PlanningRuns.RunLoader>();
        services.AddScoped<Application.Locations.PlannableFactory>();
        services.AddScoped<MilkRunPlanner>();
        services.AddScoped<IShipmentOperationsFeed, Integration.ShipmentOperationsFeed>();
        services.AddScoped<IShipmentDeliveryFeed, Integration.ShipmentDeliveryFeed>();
        services.AddSingleton<Application.PlanningRuns.IPlanningJobQueue, Application.PlanningRuns.PlanningJobQueue>();
        if (configuration.GetValue("Planning:WorkerEnabled", true))
        {
            services.AddHostedService<Application.PlanningRuns.PlanningWorker>();
        }


        services.Configure<Infrastructure.Routing.RoutingOptions>(configuration.GetSection(Infrastructure.Routing.RoutingOptions.SectionName));
        services.AddMemoryCache();
        services.AddHttpClient<Infrastructure.Routing.OsrmRoutingProvider>();
        services.AddSingleton<Infrastructure.Routing.EstimatedRoutingProvider>();
        services.AddScoped<IRoutingProvider, Infrastructure.Routing.ResilientRoutingProvider>();
        services.AddScoped<ITrackingPlanningIntegration, Integration.ShipmentTrackingFeed>();
        services.AddScoped<Application.Shipments.ShipmentLoader>();
        services.AddScoped<Application.Tendering.TenderLifecycle>();
        services.AddScoped<Application.Tendering.TenderMapper>();
        services.AddHandlers(typeof(ShipmentsModule).Assembly, "Tms.Modules.Shipments.Application");
        services.AddValidatorsFrom<ShipmentsDbContext>();
        return services;
    }

    public static IEndpointRouteBuilder MapShipmentsEndpoints(this IEndpointRouteBuilder app)
    {
        ShipmentEndpoints.Map(app);
        return app;
    }

    /// <summary>Applies this module's migrations when enabled (development / test); production migrates as a deploy step.</summary>
    public static async Task InitialiseShipmentsAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
