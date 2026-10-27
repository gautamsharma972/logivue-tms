using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tms.BuildingBlocks.Web;
using Tms.Modules.TransportOps.Application;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Integration;
using Tms.Modules.TransportOps.Endpoints;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.TransportOps;

/// <summary>
/// Public surface of the Transporter Operations module: a small transporter's own work for its customers. Delivery requests become LRs and trips; a driver confirms arrival, unloading
/// and POD from the QR code on the printed LR (no app, no sign-in); the office reviews the POD, prices freight and detention from each customer's agreement, and invoices.
/// Tables are prefixed <c>to_</c>. It knows other modules only through shared contracts.
/// </summary>
public static class TransportOpsModule
{
    public static IServiceCollection AddTransportOpsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedKernel();
        services.AddDomainEventTypes(typeof(TransportOpsModule).Assembly);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var serverVersion = Version.Parse(configuration["Database:ServerVersion"] ?? "8.4.0");

        services.AddDbContext<TransportOpsDbContext>((sp, options) =>
        {
            TransportOpsDbContextOptions.Configure(options, connectionString, serverVersion);
            options.UseTmsInterceptors(sp);
        });

        foreach (var permission in OpsPermissions.All)
        {
            services.AddSingleton(permission);
        }

        services.AddScoped<OpsAccess>();
        services.AddScoped<IOpsSettings, OpsSettings>();
        services.AddScoped<FileIntake>();
        services.AddHandlers(typeof(TransportOpsModule).Assembly, "Tms.Modules.TransportOps.Application");
        services.AddSingleton(new ApprovalDocumentType(InvoiceHandler.DocumentType, "Customer invoice", TmsModules.TransportOps));
        services.AddScoped<IDomainEventHandler<ApprovalCompleted>, InvoiceApprovalSubscriber>();
        services.AddScoped<IOperationsReportingProvider, OperationsReportingProvider>();
        services.AddValidatorsFrom<TransportOpsDbContext>();

        services.Configure<RateLimiterOptions>(options => options.AddPolicy(TransportOpsEndpoints.PublicRateLimit, context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = configuration.GetValue("RateLimiting:PublicOpsPermitsPerMinute", 60), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            })));

        return services;
    }

    public static IEndpointRouteBuilder MapTransportOpsEndpoints(this IEndpointRouteBuilder app)
    {
        TransportOpsEndpoints.Map(app);
        return app;
    }

    /// <summary>Applies this module's migrations when enabled (development / test); production migrates as a deploy step.</summary>
    public static async Task InitialiseTransportOpsAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TransportOpsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
