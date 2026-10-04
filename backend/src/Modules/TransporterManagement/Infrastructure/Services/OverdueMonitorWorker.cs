using LogiVue.Tms.TransporterManagement.Application.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Services;

/// <summary>Periodically raises overdue placement, pickup, delivery and POD alerts. Configured under <c>TransporterManagement:OverdueMonitor</c>.</summary>
internal sealed class OverdueMonitorWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<OverdueMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = configuration.GetSection("TransporterManagement:OverdueMonitor");
        if (!section.GetValue("Enabled", true))
        {
            logger.LogInformation("Overdue monitoring is disabled");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(30, section.GetValue("IntervalSeconds", 300)));

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IOperationalMonitor>().RaiseOverdueAlertsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Overdue monitoring failed; it will retry at the next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
