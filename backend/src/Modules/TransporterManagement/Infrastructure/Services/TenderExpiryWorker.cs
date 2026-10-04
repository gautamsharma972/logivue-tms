using LogiVue.Tms.TransporterManagement.Application.Tendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Services;

/// <summary>Expires tender invitations that pass their response deadline and advances sequential tenders. Configured under <c>TransporterManagement:TenderExpiry</c>.</summary>
internal sealed class TenderExpiryWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<TenderExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = configuration.GetSection("TransporterManagement:TenderExpiry");
        if (!section.GetValue("Enabled", true))
        {
            logger.LogInformation("Scheduled tender expiry is disabled");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, section.GetValue("IntervalSeconds", 30)));

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ITenderLifecycleService>().ExpireDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduled tender expiry failed; it will retry at the next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
