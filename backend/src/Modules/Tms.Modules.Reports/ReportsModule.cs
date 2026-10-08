using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tms.BuildingBlocks.Web;
using Tms.Modules.Reports.Application;
using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Endpoints;
using Tms.Modules.Reports.Infrastructure.Persistence;
using Tms.Modules.Reports.Infrastructure.Providers;
using Tms.SharedKernel;

namespace Tms.Modules.Reports;

/// <summary>
/// Public surface of Reports &amp; Analytics: the report catalogue, KPI engine, drill-down, exports, schedules and dashboards. It reads the other modules only through their
/// reporting providers (<c>I*ReportingProvider</c> in the shared contracts); a module that provides none is stood in for by the demonstration dataset.
/// </summary>
public static class ReportsModule
{
    public static IServiceCollection AddReportsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedKernel();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var serverVersion = Version.Parse(configuration["Database:ServerVersion"] ?? "8.4.0");

        services.AddDbContext<ReportsDbContext>((sp, options) =>
        {
            ReportsDbContextOptions.Configure(options, connectionString, serverVersion);
            options.UseTmsInterceptors(sp);
        });

        foreach (var permission in ReportingPermissions.All)
        {
            services.AddSingleton(permission);
        }

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ReportsOptions>().Bind(configuration.GetSection(ReportsOptions.SectionName));
        services.AddScoped<DemoReportingData>();
        services.AddScoped<ReportSettingsStore>();
        services.AddScoped<ReportingProviderResolver>();
        services.AddScoped<PrincipalFactory>();
        services.AddScoped<ReportDefinitionService>();
        services.AddScoped<ReportAuditor>();
        services.AddScoped<DailyKpiTrendSource>();
        services.AddScoped<ReportExecutor>();
        services.AddScoped<IKpiCalculationService, KpiCalculationService>();
        services.AddScoped<ReportJobService>();
        services.AddScoped<ReportNotifier>();
        services.AddHandlers(typeof(ReportsModule).Assembly, "Tms.Modules.Reports.Application");
        services.AddSingleton<DailyKpiAggregator>();
        services.AddHostedService(sp => sp.GetRequiredService<DailyKpiAggregator>());
        services.AddHostedService<ReportJobWorker>();
        services.AddHostedService<ReportScheduler>();
        return services;
    }

    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        ReportEndpoints.Map(app);
        return app;
    }

    /// <summary>Applies this module's migrations when enabled (development / test); production migrates as a deploy step.</summary>
    public static async Task InitialiseReportsAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
