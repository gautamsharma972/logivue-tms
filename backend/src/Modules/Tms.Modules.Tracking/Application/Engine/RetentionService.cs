using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;

namespace Tms.Modules.Tracking.Application.Engine;

public sealed record RetentionResult(int Tenants, int LocationsPurged, int PredictionsPurged, int RoutesSummarised, int RoutesExpired);

/// <summary>
/// Keeps raw GPS for as long as each tenant's setting says (90 days by default) and the simplified path for longer (a year). Only finished trips are touched: a trip still on the road
/// keeps every point. A trip's simplified path is written before its raw points are deleted, so nothing is lost without a summary. Milestones, events, alerts and exceptions are not
/// purged. Nothing here is hard-coded: both periods come from <c>retention</c> in <c>st_settings</c>.
/// </summary>
internal sealed class RetentionService(TrackingDbContext db, RouteHistory history, TimeProvider clock)
{
    public async Task<RetentionResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        // The only cross-tenant read: which tenants have trips at all. Everything after is predicated on the tenant explicitly.
        var tenants = await db.Shipments.IgnoreQueryFilters().AsNoTracking().Select(s => s.TenantId).Distinct().ToListAsync(cancellationToken);
        int locations = 0, predictions = 0, summarised = 0, expired = 0;
        foreach (var tenant in tenants)
        {
            var json = await db.Settings.IgnoreQueryFilters().AsNoTracking().Where(s => s.TenantId == tenant && s.Key == TrackingSettingKeys.Retention).Select(s => s.ValueJson).FirstOrDefaultAsync(cancellationToken);
            var setting = (json is null ? null : JsonSerializer.Deserialize<RetentionSetting>(json, TrackingSettings.Json)) ?? new RetentionSetting();
            var rawCutoff = now.AddDays(-Math.Max(1, setting.RawLocationDays));
            var routeCutoff = now.AddDays(-Math.Max(setting.RawLocationDays, setting.AggregatedRouteDays));

            // Finished trips whose raw points are old enough: summarise first, then delete.
            var due = await db.Shipments.IgnoreQueryFilters().Where(s => s.TenantId == tenant && s.CompletedAt != null && s.CompletedAt < rawCutoff).Select(s => s.Id).ToListAsync(cancellationToken);
            foreach (var id in due)
            {
                var trip = await db.Shipments.IgnoreQueryFilters().FirstAsync(s => s.Id == id, cancellationToken);
                if (trip.ActualRouteJson is null && await history.BuildSummaryAsync(trip.ShipmentId, cancellationToken) is { } summary)
                {
                    trip.SetActualRoute(summary);
                    summarised++;
                    await db.SaveChangesAsync(cancellationToken);
                }

                // One statement per trip: a trip's points are bounded, and a limited delete loses its table prefix in the MySQL provider.
                locations += await db.Locations.IgnoreQueryFilters().Where(l => l.TenantId == tenant && l.ShipmentId == trip.ShipmentId).ExecuteDeleteAsync(cancellationToken);

                predictions += await db.EtaPredictions.IgnoreQueryFilters().Where(p => p.TenantId == tenant && p.TrackedShipmentId == id && p.IsFinalDestination == false).ExecuteDeleteAsync(cancellationToken);
            }

            // Past the simplified path's own retention it goes too; the trip, its milestones and its exceptions stay.
            expired += await db.Shipments.IgnoreQueryFilters().Where(s => s.TenantId == tenant && s.CompletedAt != null && s.CompletedAt < routeCutoff && s.ActualRouteJson != null)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.ActualRouteJson, (string?)null), cancellationToken);
        }

        return new RetentionResult(tenants.Count, locations, predictions, summarised, expired);
    }
}

/// <summary>Runs the retention pass once a day (configurable; <c>Tracking:Retention:Enabled</c> switches it off, as the tests do).</summary>
internal sealed class RetentionWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Tracking:Retention:Enabled", true))
        {
            return;
        }

        var every = TimeSpan.FromHours(Math.Max(1, configuration.GetValue("Tracking:Retention:IntervalHours", 24)));
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); // let the host finish starting first
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(stoppingToken);
                    logger.LogInformation("Tracking retention: {Locations} locations purged, {Summaries} routes summarised, {Expired} routes expired.", result.LocationsPurged, result.RoutesSummarised, result.RoutesExpired);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Tracking retention pass failed; it will try again next time.");
                }

                await Task.Delay(every, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
