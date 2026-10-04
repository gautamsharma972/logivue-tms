using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Application.Performance;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Operations;

internal static class OperationsMapping
{
    public static PlacementDto ToDto(this VehiclePlacement p, DateTimeOffset now, int grace) => new(
        p.Id, p.ShipmentId, p.ShipmentNumber, p.TransporterId, p.VehicleId, p.VehicleRegistration, p.RequiredAt, p.ReportedAt, p.PlacedAt, p.LoadingStartedAt, p.Status,
        p.Sla(now, grace), p.DelayMinutes, p.ReplacementCount, p.ExceptionReason,
        p.Events.OrderBy(e => e.EventAt).Select(e => new PlacementEventDto(e.EventType, e.EventAt, e.Remarks)).ToList());

    public static ClaimDto ToDto(this ClaimRecord c) => new(c.Id, c.TransporterId, c.ShipmentId, c.ShipmentNumber, c.ClaimType, c.ClaimDate, c.ClaimValue, c.Status, c.Remarks, c.ResolvedAt);

    public static LoadCostDto ToDto(this LoadCost c) => new(c.Id, c.TransporterId, c.ShipmentId, c.ShipmentNumber, c.ServiceDate, c.AgreedAmount, c.InvoicedAmount, c.OnBudget);

    public static CapacityDayDto ToDto(this CapacityDay c) => new(c.Id, c.TransporterId, c.Date, c.VehiclesCommitted, c.VehiclesAvailable);

    public static AlertDto ToDto(this TransporterAlert a, string? transporterName) => new(
        a.Id, a.AlertType, a.Severity, a.TransporterId, transporterName, a.ShipmentId, a.ShipmentNumber, a.Message, a.Status, a.CreatedAt, a.AcknowledgedAt, a.ResolvedAt, a.Resolution);
}

/// <summary>
/// Vehicle placement for an accepted load: the transporter reports the vehicle, and it is placed at the pickup. Created when the load is accepted. A vendor sees
/// and reports only its own placements; staff place, mark no-shows and cancel.
/// </summary>
internal sealed class PlacementHandler(
    TransportersDbContext db, PerformanceAccess access, ITransporterSettings settings, PerformanceEngine engine, AlertService alerts, TimeProvider clock)
{
    public async Task<Result<PagedResult<PlacementDto>>> ListAsync(ListPlacementsQuery query, CancellationToken cancellationToken)
    {
        // A vendor is limited to its own company whatever it asks for.
        var owner = access.OwnTransporterId ?? query.TransporterId;
        if (owner is { } id ? access.CheckRead(id).IsFailure : !access.CanSeeAll)
        {
            return owner is null ? PerformanceAccess.Forbidden : PerformanceAccess.NotFound;
        }

        var rows = db.Placements.AsNoTracking().Include(p => p.Events).AsQueryable();
        if (owner is { } own)
        {
            rows = rows.Where(p => p.TransporterId == own);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(p => p.Status == status);
        }

        var page = await rows.OrderByDescending(p => p.RequiredAt).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        var grace = await settings.GetAsync<int>(SettingKeys.PlacementGraceMinutes, cancellationToken);
        var now = clock.GetUtcNow();
        return new PagedResult<PlacementDto>(page.Items.Select(p => p.ToDto(now, grace)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<PlacementDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var placement = await db.Placements.AsNoTracking().Include(p => p.Events).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (placement is null || access.CheckRead(placement.TransporterId).IsFailure)
        {
            return NotFound;
        }

        return placement.ToDto(clock.GetUtcNow(), await settings.GetAsync<int>(SettingKeys.PlacementGraceMinutes, cancellationToken));
    }

    public Task<Result<PlacementDto>> ReportAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, vendorMayWrite: true, (p, now, _) => p.Report(now), cancellationToken);

    public Task<Result<PlacementDto>> PlaceAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, vendorMayWrite: false, (p, now, _) => p.Place(now), cancellationToken);

    public Task<Result<PlacementDto>> NoShowAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, vendorMayWrite: false, (p, now, grace) => p.NoShow(request.Reason, now, grace), cancellationToken, async p =>
            await alerts.RaiseAsync(AlertService.PlacementNoShow, AlertSeverity.High, p.TransporterId, p.ShipmentId, p.ShipmentNumber, $"placement:{p.Id:N}:noshow",
                $"No vehicle was placed for {p.ShipmentNumber} by {p.RequiredAt.ToOffset(TimeSpan.FromMinutes(330)):dd MMM HH:mm}.", cancellationToken));

    public Task<Result<PlacementDto>> CancelAsync(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(id, vendorMayWrite: false, (p, now, _) => p.Cancel(request.Reason, now), cancellationToken);

    private async Task<Result<PlacementDto>> ChangeAsync(
        Guid id, bool vendorMayWrite, Func<VehiclePlacement, DateTimeOffset, int, Result> change, CancellationToken cancellationToken, Func<VehiclePlacement, Task>? after = null)
    {
        var placement = await db.Placements.Include(p => p.Events).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (placement is null)
        {
            return NotFound;
        }

        if (access.CheckWrite(placement.TransporterId, vendorMayWrite) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var grace = await settings.GetAsync<int>(SettingKeys.PlacementGraceMinutes, cancellationToken);
        var now = clock.GetUtcNow();
        var changed = change(placement, now, grace);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        if (after is not null)
        {
            await after(placement);
        }

        await alerts.ResolveAsync($"placement:{placement.Id:N}", [AlertService.PlacementOverdue], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await engine.RefreshAsync(placement.TransporterId, [placement.RequiredAt], cancellationToken);
        return placement.ToDto(now, grace);
    }

    private static readonly Error NotFound = Error.NotFound("placements.not_found", "Placement not found.");
}

internal sealed class ClaimHandler(TransportersDbContext db, PerformanceAccess access, IShipmentOperationsFeed feed, PerformanceEngine engine, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<ClaimDto>>> ListAsync(Guid transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (access.CheckRead(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var claims = await db.Claims.AsNoTracking().Where(c => c.TransporterId == transporterId && c.ClaimDate >= from && c.ClaimDate <= to).OrderByDescending(c => c.ClaimDate).ToListAsync(cancellationToken);
        return claims.Select(c => c.ToDto()).ToList();
    }

    public async Task<Result<ClaimDto>> RecordAsync(Guid transporterId, RecordClaimRequest request, CancellationToken cancellationToken)
    {
        if (access.CheckWrite(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        if (!await db.TransporterExistsAsync(transporterId, cancellationToken) || user.TenantId is not { } tenantId)
        {
            return PerformanceAccess.NotFound;
        }

        string? number = null;
        if (request.ShipmentId is { } shipmentId)
        {
            var fact = await feed.GetAsync(shipmentId, cancellationToken);
            if (fact is null || fact.TransporterId != transporterId)
            {
                return Error.NotFound("claims.shipment_not_found", "That shipment was not carried by this transporter.");
            }

            number = fact.Number;
        }

        var claim = ClaimRecord.Create(tenantId, transporterId, request.ShipmentId, number, request.ClaimType, request.ClaimDate, request.ClaimValue, request.Remarks, clock.TodayInIndia());
        if (claim.IsFailure)
        {
            return claim.Error;
        }

        db.Claims.Add(claim.Value);
        await db.SaveChangesAsync(cancellationToken);
        await engine.RefreshAsync(transporterId, [Day(claim.Value.ClaimDate)], cancellationToken);
        return claim.Value.ToDto();
    }

    public Task<Result<ClaimDto>> ResolveAsync(Guid claimId, CancellationToken cancellationToken) => ChangeAsync(claimId, (c, now) => c.Resolve(now), cancellationToken);

    /// <summary>A claim raised from a delivery exception starts with no value; the person handling it fills it in.</summary>
    public Task<Result<ClaimDto>> SetValueAsync(Guid claimId, SetClaimValueRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(claimId, (c, _) => c.SetValue(request.ClaimValue), cancellationToken);

    private async Task<Result<ClaimDto>> ChangeAsync(Guid claimId, Func<ClaimRecord, DateTimeOffset, Result> change, CancellationToken cancellationToken)
    {
        var claim = await db.Claims.FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);
        if (claim is null || access.CheckWrite(claim.TransporterId).IsFailure)
        {
            return Error.NotFound("claims.not_found", "Claim not found.");
        }

        var changed = change(claim, clock.GetUtcNow());
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        await engine.RefreshAsync(claim.TransporterId, [Day(claim.ClaimDate)], cancellationToken);
        return claim.ToDto();
    }

    private static DateTimeOffset Day(DateOnly date) => PerformanceEngine.StartOf(date);
}

/// <summary>Invoiced cost per load. Staff only: it carries prices. One record per load, so a load cannot be counted twice in cost performance.</summary>
internal sealed class LoadCostHandler(TransportersDbContext db, PerformanceAccess access, IShipmentOperationsFeed feed, PerformanceEngine engine, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<LoadCostDto>>> ListAsync(Guid transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (!access.CanSeeAll)
        {
            return PerformanceAccess.Forbidden;
        }

        var costs = await db.Costs.AsNoTracking().Where(c => c.TransporterId == transporterId && c.ServiceDate >= from && c.ServiceDate <= to).OrderByDescending(c => c.ServiceDate).ToListAsync(cancellationToken);
        return costs.Select(c => c.ToDto()).ToList();
    }

    public async Task<Result<LoadCostDto>> RecordAsync(Guid transporterId, RecordLoadCostRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return PerformanceAccess.Forbidden;
        }

        if (!await db.TransporterExistsAsync(transporterId, cancellationToken) || user.TenantId is not { } tenantId)
        {
            return PerformanceAccess.NotFound;
        }

        var fact = await feed.GetAsync(request.ShipmentId, cancellationToken);
        if (fact is null || fact.TransporterId != transporterId)
        {
            return Error.NotFound("costs.shipment_not_found", "That shipment was not carried by this transporter.");
        }

        if (await db.Costs.AnyAsync(c => c.ShipmentId == request.ShipmentId, cancellationToken))
        {
            return Error.Conflict("costs.duplicate", $"A cost is already recorded for {fact.Number}.");
        }

        var agreed = request.AgreedAmount ?? fact.FreightEstimate;
        if (agreed is null)
        {
            return Error.Validation("costs.agreed_required", "Enter the agreed amount: the shipment has no freight estimate to use.");
        }

        var served = DateOnly.FromDateTime((fact.DeliveredAt ?? fact.DispatchedAt ?? fact.AcceptedAt ?? DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromMinutes(330)).DateTime);
        var cost = LoadCost.Create(tenantId, transporterId, fact.ShipmentId, fact.Number, served, agreed.Value, request.InvoicedAmount);
        if (cost.IsFailure)
        {
            return cost.Error;
        }

        db.Costs.Add(cost.Value);
        await db.SaveChangesAsync(cancellationToken);
        await engine.RefreshAsync(transporterId, [PerformanceEngine.StartOf(served)], cancellationToken);
        return cost.Value.ToDto();
    }
}

/// <summary>Daily vehicle capacity, reported by the vendor (or entered by staff). Saving a day replaces the earlier figures for it.</summary>
internal sealed class CapacityHandler(TransportersDbContext db, PerformanceAccess access, PerformanceEngine engine, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<CapacityDayDto>>> ListAsync(Guid transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (access.CheckRead(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var days = await db.Capacity.AsNoTracking().Where(c => c.TransporterId == transporterId && c.Date >= from && c.Date <= to).OrderBy(c => c.Date).ToListAsync(cancellationToken);
        return days.Select(d => d.ToDto()).ToList();
    }

    public async Task<Result<CapacityDayDto>> SaveAsync(Guid transporterId, SaveCapacityRequest request, CancellationToken cancellationToken)
    {
        if (access.CheckWrite(transporterId, vendorMayWrite: true) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        if (!await db.TransporterExistsAsync(transporterId, cancellationToken) || user.TenantId is not { } tenantId)
        {
            return PerformanceAccess.NotFound;
        }

        var day = await db.Capacity.FirstOrDefaultAsync(c => c.TransporterId == transporterId && c.Date == request.Date, cancellationToken);
        if (day is null)
        {
            day = CapacityDay.For(tenantId, transporterId, request.Date);
            db.Capacity.Add(day);
        }

        var set = day.Set(request.VehiclesCommitted, request.VehiclesAvailable);
        if (set.IsFailure)
        {
            return set.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        await engine.RefreshAsync(transporterId, [PerformanceEngine.StartOf(request.Date)], cancellationToken);
        return day.ToDto();
    }
}

internal sealed class AlertHandler(TransportersDbContext db, PerformanceAccess access, AlertService alerts, TimeProvider clock)
{
    /// <summary>Alerts, newest first. Overdue ones are worked out as the list is read, so nobody has to run a job to see them.</summary>
    public async Task<Result<PagedResult<AlertDto>>> ListAsync(ListAlertsQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanSeeAll)
        {
            return PerformanceAccess.Forbidden;
        }

        await alerts.EvaluateOverdueAsync(cancellationToken);
        var rows = db.Alerts.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
        {
            rows = rows.Where(a => a.Status == status);
        }

        if (query.Severity is { } severity)
        {
            rows = rows.Where(a => a.Severity == severity);
        }

        if (query.TransporterId is { } id)
        {
            rows = rows.Where(a => a.TransporterId == id);
        }

        var page = await rows.OrderBy(a => a.Status == AlertStatus.Resolved ? 1 : 0).ThenByDescending(a => a.Severity).ThenByDescending(a => a.CreatedAt).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        var ids = page.Items.Select(a => a.TransporterId).Distinct().ToList();
        var names = await db.Transporters.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.LegalName, cancellationToken);
        return new PagedResult<AlertDto>(page.Items.Select(a => a.ToDto(names.GetValueOrDefault(a.TransporterId))).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<int>> EvaluateAsync(CancellationToken cancellationToken) =>
        access.CanManage ? await alerts.EvaluateOverdueAsync(cancellationToken) : PerformanceAccess.Forbidden;

    public Task<Result<AlertDto>> AcknowledgeAsync(Guid id, CancellationToken cancellationToken) => ChangeAsync(id, a => a.Acknowledge(clock.GetUtcNow()), cancellationToken);

    public Task<Result<AlertDto>> ResolveAsync(Guid id, ResolveAlertRequest request, CancellationToken cancellationToken) => ChangeAsync(id, a => a.Resolve(clock.GetUtcNow(), request.Comments), cancellationToken);

    private async Task<Result<AlertDto>> ChangeAsync(Guid id, Func<TransporterAlert, Result> change, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return PerformanceAccess.Forbidden;
        }

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert is null)
        {
            return Error.NotFound("alerts.not_found", "Alert not found.");
        }

        var changed = change(alert);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        var name = await db.Transporters.AsNoTracking().Where(t => t.Id == alert.TransporterId).Select(t => t.LegalName).FirstOrDefaultAsync(cancellationToken);
        return alert.ToDto(name);
    }
}
