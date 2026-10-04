using LogiVue.Tms.TransporterManagement.Application.Compliance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Services;

/// <summary>
/// Re-evaluates compliance on a schedule so expiry alerts appear without anyone opening a record.
/// Configured under <c>TransporterManagement:ComplianceEvaluation</c>.
/// </summary>
internal sealed class ComplianceEvaluationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ComplianceEvaluationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = configuration.GetSection("TransporterManagement:ComplianceEvaluation");
        if (!section.GetValue("Enabled", true))
        {
            logger.LogInformation("Scheduled compliance evaluation is disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, section.GetValue("IntervalMinutes", 360)));

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IComplianceService>().EvaluateAsync(cancellationToken: stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduled compliance evaluation failed; it will retry at the next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
