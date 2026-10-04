using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Performance;

/// <summary>
/// Creates and updates load executions. Used by the API (a planner or the vendor recording a milestone) and by the reactions to shipment events,
/// so a shipment that is accepted, dispatched and delivered builds its execution record without anyone typing it in.
/// </summary>
internal sealed class ExecutionService(
    TransportersDbContext db, IShipmentOperationsFeed feed, ITransporterSettings settings, PerformanceEngine engine, ICurrentUser user, TimeProvider clock)
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    /// <summary>The planned pickup is due by the end of the planned pickup date; delivery by the deliver-by date. The time of day is configurable.</summary>
    public static (DateTimeOffset? Pickup, DateTimeOffset? Delivery) PlannedTimes(ShipmentFact fact, PlannedTimesSetting setting) =>
        (At(fact.PlannedPickupDate, setting.PickupDueTime), fact.DeliverBy is { } by ? At(by, setting.DeliveryDueTime) : null);

    private static DateTimeOffset At(DateOnly date, string time)
    {
        var parsed = TimeOnly.TryParse(time, out var t) ? t : new TimeOnly(20, 0);
        return new DateTimeOffset(date.Year, date.Month, date.Day, parsed.Hour, parsed.Minute, 0, India);
    }

    /// <summary>The execution of a shipment, created from the shipment's facts when it does not exist yet. Null if the shipment is unknown or has no transporter.</summary>
    public async Task<LoadExecution?> EnsureAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var existing = await db.Executions.Include(e => e.Events).FirstOrDefaultAsync(e => e.ShipmentId == shipmentId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var fact = await feed.GetAsync(shipmentId, cancellationToken);
        if (fact is null || user.TenantId is not { } tenantId)
        {
            return null;
        }

        var (pickup, delivery) = PlannedTimes(fact, await settings.GetAsync<PlannedTimesSetting>(SettingKeys.PlannedTimes, cancellationToken));
        var created = LoadExecution.Create(tenantId, fact, pickup, delivery);
        db.Executions.Add(created);
        return created;
    }

    /// <summary>Records a milestone and rebuilds the KPI months it touches. Saves.</summary>
    public async Task<Result> RecordAsync(
        LoadExecution execution, ExecutionEventType type, DateTimeOffset at, string? reasonCode, string? remarks, CancellationToken cancellationToken)
    {
        var policy = await settings.GetAsync<DelayPolicySetting>(SettingKeys.ExecutionDelayPolicy, cancellationToken);
        DelayReason? reason = null;
        if (!string.IsNullOrWhiteSpace(reasonCode))
        {
            reason = policy.Find(reasonCode);
            if (reason is null)
            {
                return Error.Validation("executions.delay_reason_invalid", $"'{reasonCode}' is not a valid delay reason.");
            }
        }

        var recorded = execution.Record(type, at, reason, policy.ToleranceMinutes, remarks, user.UserId, clock.GetUtcNow());
        if (recorded.IsFailure)
        {
            return recorded;
        }

        await db.SaveChangesAsync(cancellationToken);
        await engine.RefreshAsync(execution.TransporterId, Anchors(execution, at), cancellationToken);
        return Result.Success();
    }

    public Task RefreshKpisAsync(LoadExecution execution, CancellationToken cancellationToken) =>
        engine.RefreshAsync(execution.TransporterId, Anchors(execution, execution.ActualDeliveryAt ?? execution.ActualPickupAt ?? clock.GetUtcNow()), cancellationToken);

    public static IEnumerable<DateTimeOffset> Anchors(LoadExecution execution, DateTimeOffset at) =>
        new[] { execution.PlannedPickupAt, execution.PlannedDeliveryAt, at }.Where(d => d is not null).Select(d => d!.Value);
}
