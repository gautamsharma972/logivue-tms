using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tms.BuildingBlocks.Web;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Application.Dashboard;
using Tms.Modules.Deliveries.Application.Claims;
using Tms.Modules.Deliveries.Application.Exceptions;
using Tms.Modules.Deliveries.Application.Notifications;
using Tms.Modules.Deliveries.Application.Execution;
using Tms.Modules.Deliveries.Application.Ocr;
using Tms.Modules.Deliveries.Application.Pods;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Endpoints;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.Modules.Deliveries.Integration;
using Tms.SharedKernel;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Deliveries;

/// <summary>
/// Public surface of the Deliveries module: delivery execution on the road, the proof of delivery that evidences it, the reading and review of the paper POD, and the
/// exceptions that come out of it. Tables are prefixed <c>pd_</c>. It knows other modules only through shared contracts.
/// </summary>
public static class DeliveriesModule
{
    public static IServiceCollection AddDeliveriesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedKernel();
        services.AddDomainEventTypes(typeof(DeliveriesModule).Assembly);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var serverVersion = Version.Parse(configuration["Database:ServerVersion"] ?? "8.4.0");

        services.AddDbContext<DeliveriesDbContext>((sp, options) =>
        {
            DeliveriesDbContextOptions.Configure(options, connectionString, serverVersion);
            options.UseTmsInterceptors(sp);
        });

        foreach (var permission in DeliveryPermissions.All)
        {
            services.AddSingleton(permission);
        }

        services.AddScoped<DeliveryAccess>();
        services.AddScoped<IDeliverySettings, DeliverySettings>();
        services.AddScoped<DeliveryMapper>();
        services.AddScoped<ExceptionFactory>();
        services.AddScoped<PodEngine>();
        services.AddScoped<OtpService>();
        services.AddScoped<PodOcrProcessor>();
        services.Configure<OllamaOcrOptions>(configuration.GetSection(OllamaOcrOptions.Section));
        services.AddSingleton<TextLayerOcrService>();
        if (configuration.GetValue<bool>($"{OllamaOcrOptions.Section}:Enabled"))
        {
            services.AddHttpClient<OllamaPodOcrService>(c => c.Timeout = Timeout.InfiniteTimeSpan);
            services.AddSingleton<IPodOcrService>(sp => new CompositePodOcrService(sp.GetRequiredService<TextLayerOcrService>(), sp.GetRequiredService<OllamaPodOcrService>()));
        }
        else
        {
            services.AddSingleton<IPodOcrService>(sp => sp.GetRequiredService<TextLayerOcrService>());
        }

        services.AddSingleton<IPodOcrQueue, PodOcrQueue>();
        if (configuration.GetValue("Deliveries:OcrWorkerEnabled", true))
        {
            services.AddHostedService<PodOcrWorker>();
        }

        services.AddScoped<ProofRowSource>();
        services.AddScoped<AgeingService>();
        services.AddScoped<NotificationPublisher>();
        services.AddScoped<SlaMonitor>();
        services.AddScoped<ClaimService>();
        services.AddScoped<FreightAuditReporter>();
        // Local stand-ins, used only until another module implements the contract (the first registration wins).
        services.TryAddScoped<IClaimsIntegration, LocalClaimsIntegration>();
        services.TryAddScoped<IFreightAuditIntegration, LocalFreightAuditIntegration>();
        services.AddScoped<IDeliveryReliabilityFeed, DeliveryReliabilityFeed>();
        services.AddScoped<IDomainEventHandler<ShipmentDispatched>, ShipmentDispatchedSubscriber>();
        services.AddScoped<IDomainEventHandler<DeliveryTrackingEvent>, DeliveryTrackingSiteSubscriber>();
        services.AddScoped<IDomainEventHandler<DeliveryCompleted>, DeliveryCompletedSubscriber>();
        services.AddScoped<IDomainEventHandler<PodSubmitted>, PodSubmittedSubscriber>();
        services.AddScoped<IDomainEventHandler<PodAccepted>, PodAcceptedSubscriber>();
        services.AddScoped<IDomainEventHandler<PodRejected>, PodRejectedSubscriber>();
        services.AddHandlers(typeof(DeliveriesModule).Assembly, "Tms.Modules.Deliveries.Application");
        services.AddValidatorsFrom<DeliveriesDbContext>();
        return services;
    }

    public static IEndpointRouteBuilder MapDeliveriesEndpoints(this IEndpointRouteBuilder app)
    {
        DeliveryEndpoints.Map(app);
        return app;
    }

    /// <summary>Applies this module's migrations when enabled (development / test); production migrates as a deploy step.</summary>
    public static async Task InitialiseDeliveriesAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DeliveriesDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
