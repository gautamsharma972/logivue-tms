using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tms.BuildingBlocks.Web;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Application.Lifecycle;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Endpoints;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.Modules.Contracts.Integration;
using Tms.SharedKernel;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Contracts;

/// <summary>Public surface of the Contracts module: freight agreements, rate cards, diesel clauses and the freight price engine.</summary>
public static class ContractsModule
{
    public static IServiceCollection AddContractsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedKernel();
        services.AddDomainEventTypes(typeof(ContractsModule).Assembly);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var serverVersion = Version.Parse(configuration["Database:ServerVersion"] ?? "8.4.0");

        services.AddDbContext<ContractsDbContext>((sp, options) =>
        {
            ContractsDbContextOptions.Configure(options, connectionString, serverVersion);
            options.UseTmsInterceptors(sp);
        });

        foreach (var permission in ContractPermissions.All)
        {
            services.AddSingleton(permission);
        }

        services.AddOptions<ContractLifecycleOptions>().Bind(configuration.GetSection(ContractLifecycleOptions.SectionName));
        services.AddHostedService<ContractLifecycleService>();

        services.AddScoped<ContractAccess>();
        services.AddScoped<NumberSequence>();
        services.AddScoped<Application.Contracts.ContractLoader>();
        services.AddScoped<ContractRateChecker>();
        services.AddScoped<IDomainEventHandler<ApprovalCompleted>, ContractApprovalSubscriber>();
        services.AddScoped<IFreightQuoteService, Application.Contracts.FreightQuoteService>();
        services.AddScoped<Application.Rating.RatingCandidateLoader>();
        services.AddScoped<Application.Rating.RatingService>();
        services.AddScoped<FreightRatingIntegration>();
        services.AddScoped<IFreightPlanningIntegration>(sp => sp.GetRequiredService<FreightRatingIntegration>());
        services.AddScoped<IContractualBaselineService>(sp => sp.GetRequiredService<FreightRatingIntegration>());
        services.AddScoped<IFreightTransporterIntegration, TransporterCoverageIntegration>();
        services.AddScoped<ITransporterDirectoryLookup, DirectoryLookup>();
        services.AddScoped<IContractedCapacityProvider, ContractedCapacityProvider>();
        // Delivery and tracking will supply what happened on a shipment; until one does, a stand-in says there are no actuals.
        services.TryAddScoped<IFreightActualsProvider, NoFreightActuals>();
        services.AddHandlers(typeof(ContractsModule).Assembly, "Tms.Modules.Contracts.Application");
        services.AddValidatorsFrom<ContractsDbContext>();
        return services;
    }

    public static IEndpointRouteBuilder MapContractsEndpoints(this IEndpointRouteBuilder app)
    {
        ContractEndpoints.Map(app);
        return app;
    }

    /// <summary>Applies this module's migrations when enabled (development / test); production migrates as a deploy step.</summary>
    public static async Task InitialiseContractsAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ContractsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
