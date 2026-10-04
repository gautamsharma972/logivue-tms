using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Eligibility;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Notifications;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using LogiVue.Tms.TransporterManagement.Infrastructure.Queries;
using LogiVue.Tms.TransporterManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("LogiVue.Tms.Tests")]

namespace LogiVue.Tms.TransporterManagement.Infrastructure;

public static class TransporterManagementInfrastructure
{
    /// <summary>
    /// Registers the module's DbContext and infrastructure services. The host supplies the MySQL connection string
    /// and server version, so the module never reads host configuration directly.
    /// </summary>
    public static IServiceCollection AddTransporterManagementInfrastructure(
        this IServiceCollection services,
        string connectionString,
        Version serverVersion,
        string documentStoragePath)
    {
        services.AddDbContext<TransporterDbContext>(options => options.UseMySql(
            connectionString,
            new MySqlServerVersion(serverVersion),
            mysql => mysql.MigrationsHistoryTable(TransporterDbContext.MigrationsHistoryTable)));

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ITransporterQueries, TransporterQueries>();
        services.AddScoped<IEligibilityDataProvider, EligibilityDataProvider>();
        services.AddScoped<ITenderQueries, TenderQueries>();
        services.AddScoped<ITransporterPodProvider, LocalPodProvider>();
        services.AddScoped<ITransporterClaimsProvider, LocalClaimsProvider>();
        services.AddScoped<ITransporterCostProvider, LocalCostProvider>();
        services.AddScoped<ITransporterAvailabilityProvider, LocalAvailabilityProvider>();
        services.AddSingleton<IDocumentStorage>(new LocalDocumentStorage(documentStoragePath));
        services.AddHostedService<ComplianceEvaluationWorker>();
        services.AddHostedService<TenderExpiryWorker>();
        services.AddHostedService<OverdueMonitorWorker>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<ITransporterSettings, TransporterSettings>();
        services.AddScoped<INotificationService, LoggingNotificationService>();

        return services;
    }
}
