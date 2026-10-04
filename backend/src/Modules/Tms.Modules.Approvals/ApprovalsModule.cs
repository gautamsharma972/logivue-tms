using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tms.BuildingBlocks.Web;
using Tms.Modules.Approvals.Application;
using Tms.Modules.Approvals.Domain;
using Tms.Modules.Approvals.Endpoints;
using Tms.Modules.Approvals.Infrastructure.Persistence;
using Tms.SharedKernel;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Approvals;

/// <summary>Public surface of the Approvals module: configurable multi-step approval of business documents.</summary>
public static class ApprovalsModule
{
    /// <summary>
    /// Document types that can be put through approval. Each owning module will register its own once it exists;
    /// they are declared here for now so policies can be configured ahead of those modules.
    /// </summary>
    private static readonly ApprovalDocumentType[] BuiltInDocumentTypes =
    [
        new("transporter_onboarding", "Transporter onboarding"),
        new("freight_contract", "Freight contract"),
        new("spot_rate", "Spot rate"),
        new("freight_bill", "Freight bill"),
        new("claim", "Claim"),
    ];

    public static IServiceCollection AddApprovalsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedKernel();
        services.AddDomainEventTypes(typeof(ApprovalsModule).Assembly);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var serverVersion = Version.Parse(configuration["Database:ServerVersion"] ?? "8.4.0");

        services.AddDbContext<ApprovalsDbContext>((sp, options) =>
        {
            ApprovalsDbContextOptions.Configure(options, connectionString, serverVersion);
            options.UseTmsInterceptors(sp);
        });

        foreach (var permission in ApprovalPermissions.All)
        {
            services.AddSingleton(permission);
        }

        foreach (var type in BuiltInDocumentTypes)
        {
            services.AddSingleton(type);
        }

        services.AddSingleton<DocumentTypeCatalog>();
        services.AddScoped<ApprovalAuthority>();
        services.AddScoped<RequestMapper>();
        services.AddScoped<Application.Delegations.DelegationMapper>();
        services.AddScoped<IApprovalGateway, Application.Requests.ApprovalGateway>();
        services.AddHandlers(typeof(ApprovalsModule).Assembly, "Tms.Modules.Approvals.Application");
        services.AddValidatorsFrom<ApprovalsDbContext>();
        return services;
    }

    public static IEndpointRouteBuilder MapApprovalsEndpoints(this IEndpointRouteBuilder app)
    {
        ApprovalEndpoints.Map(app);
        return app;
    }

    /// <summary>Applies this module's migrations when enabled (development / test); production migrates as a deploy step.</summary>
    public static async Task InitialiseApprovalsAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApprovalsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
