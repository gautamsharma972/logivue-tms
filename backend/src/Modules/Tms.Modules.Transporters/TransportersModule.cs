using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tms.BuildingBlocks.Web;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Endpoints;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.Modules.Transporters.Integration;
using Tms.SharedKernel;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Transporters;

/// <summary>Public surface of the Transporters module: vendor master, onboarding, fleet and compliance documents.</summary>
public static class TransportersModule
{
    public static IServiceCollection AddTransportersModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedKernel();
        services.AddDomainEventTypes(typeof(TransportersModule).Assembly);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var serverVersion = Version.Parse(configuration["Database:ServerVersion"] ?? "8.4.0");

        services.AddDbContext<TransportersDbContext>((sp, options) =>
        {
            TransportersDbContextOptions.Configure(options, connectionString, serverVersion);
            options.UseTmsInterceptors(sp);
        });

        foreach (var permission in TransporterPermissions.All)
        {
            services.AddSingleton(permission);
        }

        services.AddScoped<TransporterAccess>();
        services.AddScoped<NumberSequence>();
        services.AddScoped<Application.Fleet.VehicleTypeSeeder>();
        services.AddScoped<TransporterDirectory>();
        services.AddScoped<ITransporterDirectory>(sp => sp.GetRequiredService<TransporterDirectory>());
        services.AddScoped<IVehicleTypeDirectory>(sp => sp.GetRequiredService<TransporterDirectory>());
        services.AddScoped<IFleetDirectory, FleetDirectory>();
        services.AddScoped<IDomainEventHandler<ApprovalCompleted>, TransporterApprovalSubscriber>();
        services.AddScoped<Application.Settings.ITransporterSettings, Application.Settings.TransporterSettings>();
        services.AddScoped<Application.Performance.PerformanceAccess>();
        services.AddScoped<Application.Performance.PerformanceEngine>();
        services.AddScoped<Application.Performance.ExecutionService>();
        services.AddScoped<Application.Selection.SelectionService>();
        services.AddScoped<Application.Selection.TransporterPlanningPolicy>();
        services.AddScoped<ITransporterPlanningPolicy>(sp => sp.GetRequiredService<Application.Selection.TransporterPlanningPolicy>());
        services.AddScoped<IDomainEventHandler<ShipmentTendered>, ShipmentTenderedSubscriber>();
        services.AddScoped<IDomainEventHandler<ShipmentAccepted>, ShipmentAcceptedSubscriber>();
        services.AddScoped<IDomainEventHandler<ShipmentRejected>, ShipmentRejectedSubscriber>();
        services.AddScoped<IDomainEventHandler<ShipmentDispatched>, ShipmentDispatchedSubscriber>();
        services.AddScoped<IDomainEventHandler<ShipmentDelivered>, ShipmentDeliveredSubscriber>();
        services.AddHandlers(typeof(TransportersModule).Assembly, "Tms.Modules.Transporters.Application");
        services.AddValidatorsFrom<TransportersDbContext>();
        return services;
    }

    public static IEndpointRouteBuilder MapTransportersEndpoints(this IEndpointRouteBuilder app)
    {
        TransporterEndpoints.Map(app);
        return app;
    }

    /// <summary>Applies this module's migrations when enabled (development / test); production migrates as a deploy step.</summary>
    public static async Task InitialiseTransportersAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TransportersDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
