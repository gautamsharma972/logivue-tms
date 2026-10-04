using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Queries;

internal sealed class TenderQueries(TransporterDbContext db) : ITenderQueries
{
    private IQueryable<TenderInvitationDto> Invitations(IQueryable<Tender> tenders) =>
        tenders.Select(t => new TenderInvitationDto(
            t.Id, t.TenderNumber, t.TenderType, t.TransporterId,
            db.Transporters.Where(x => x.Id == t.TransporterId).Select(x => x.LegalName).FirstOrDefault() ?? string.Empty,
            t.Status, t.SequenceNumber, t.LoadReference, t.OriginLocationReference, t.DestinationLocationReference,
            t.ServiceType, t.VehicleTypeReference, t.WeightKg, t.VolumeM3, t.OfferedRate, t.Currency,
            t.PickupDateTime, t.DeliveryDateTime, t.ResponseDeadline, t.SentAt, t.Notes, t.CreatedAt));

    public async Task<PagedResult<TenderInvitationDto>> SearchAsync(TenderSearch search, CancellationToken cancellationToken = default)
    {
        var page = Paging.NormalisePage(search.Page);
        var pageSize = Paging.NormalisePageSize(search.PageSize);

        var query = db.Tenders.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search.TenderNumber))
        {
            var number = search.TenderNumber.Trim().ToUpperInvariant();
            query = query.Where(t => t.TenderNumber == number);
        }

        if (search.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        if (search.TransporterId is { } transporterId)
        {
            query = query.Where(t => t.TransporterId == transporterId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await Invitations(query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<TenderInvitationDto>(items, page, pageSize, total);
    }

    public async Task<TenderDetailDto?> GetDetailAsync(long invitationId, long? scopeTransporterId, CancellationToken cancellationToken = default)
    {
        var tender = await db.Tenders.AsNoTracking().FirstOrDefaultAsync(t => t.Id == invitationId, cancellationToken);
        if (tender is null || (scopeTransporterId is { } scope && tender.TransporterId != scope))
        {
            return null;
        }

        var group = await Invitations(db.Tenders.AsNoTracking().Where(t => t.TenderNumber == tender.TenderNumber)
                .OrderBy(t => t.SequenceNumber).ThenBy(t => t.Id))
            .ToListAsync(cancellationToken);

        var invitation = group.Single(g => g.Id == invitationId);

        var responses = await db.TenderResponses.AsNoTracking()
            .Where(r => r.TenderId == invitationId)
            .OrderBy(r => r.ResponseAt)
            .Select(r => new TenderResponseDto(r.Id, r.Response, r.ResponseAt, r.QuotedRate, r.VehicleId, r.DriverReference,
                r.DriverMobile, r.ExpectedPlacementAt, r.EtaAt, r.Reason, r.Comments))
            .ToListAsync(cancellationToken);

        var events = await db.TenderEvents.AsNoTracking()
            .Where(e => e.TenderId == invitationId)
            .OrderBy(e => e.EventAt).ThenBy(e => e.Id)
            .Select(e => new TenderEventDto(e.Id, e.EventType, e.EventAt, e.PerformedBy, e.Comments))
            .ToListAsync(cancellationToken);

        return new TenderDetailDto(invitation, group, responses, events);
    }

    public async Task<PagedResult<TenderInvitationDto>> VendorInvitationsAsync(long transporterId, TenderStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Paging.NormalisePage(page);
        pageSize = Paging.NormalisePageSize(pageSize);

        // Draft invitations have not been sent, so the vendor never sees them.
        var query = db.Tenders.AsNoTracking()
            .Where(t => t.TransporterId == transporterId && t.Status != TenderStatus.Draft);
        if (status is { } s)
        {
            query = query.Where(t => t.Status == s);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await Invitations(query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<TenderInvitationDto>(items, page, pageSize, total);
    }

    public async Task<VendorDashboardDto> VendorDashboardAsync(long transporterId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var todayStart = today.ToDateTime(TimeOnly.MinValue);
        var tomorrowStart = todayStart.AddDays(1);
        var mine = db.Tenders.AsNoTracking().Where(t => t.TransporterId == transporterId);

        var newTenders = await mine.CountAsync(t => t.Status == TenderStatus.Sent, cancellationToken);
        var pending = await mine.CountAsync(t => t.Status == TenderStatus.Sent || t.Status == TenderStatus.Viewed, cancellationToken);
        var accepted = await mine.CountAsync(t => t.Status == TenderStatus.Accepted || t.Status == TenderStatus.Awarded, cancellationToken);
        var pickups = await mine.CountAsync(t => (t.Status == TenderStatus.Accepted || t.Status == TenderStatus.Awarded)
            && t.PickupDateTime >= todayStart && t.PickupDateTime < tomorrowStart, cancellationToken);
        var deliveries = await mine.CountAsync(t => (t.Status == TenderStatus.Accepted || t.Status == TenderStatus.Awarded)
            && t.DeliveryDateTime >= todayStart && t.DeliveryDateTime < tomorrowStart, cancellationToken);

        var upcomingPlacements = await db.VehiclePlacementRequests.AsNoTracking()
            .CountAsync(p => p.TransporterId == transporterId
                && (p.Status == PlacementStatus.Requested || p.Status == PlacementStatus.Confirmed
                    || p.Status == PlacementStatus.VehicleAssigned || p.Status == PlacementStatus.Reported), cancellationToken);

        var openExceptions = await db.Exceptions.AsNoTracking()
            .CountAsync(e => e.TransporterId == transporterId && e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed, cancellationToken);

        var pendingPod = await db.PodRecords.AsNoTracking()
            .CountAsync(p => p.TransporterId == transporterId && (p.Status == PodStatus.Pending || p.Status == PodStatus.ResubmissionRequired), cancellationToken);

        return new VendorDashboardDto(newTenders, pending, accepted, upcomingPlacements, pickups, deliveries,
            PendingPod: pendingPod, OpenExceptions: openExceptions);
    }

    /// <summary>
    /// The transporter's accepted loads with their vehicle, placement, execution and POD stages. Each stage is read in
    /// one batched query for all loads, so the cost does not grow with the number of loads.
    /// </summary>
    public async Task<IReadOnlyList<VendorLoadDto>> VendorLoadsAsync(long transporterId, CancellationToken cancellationToken = default)
    {
        var loads = await db.Tenders.AsNoTracking()
            .Where(t => t.TransporterId == transporterId && (t.Status == TenderStatus.Accepted || t.Status == TenderStatus.Awarded))
            .OrderBy(t => t.PickupDateTime)
            .ToListAsync(cancellationToken);
        if (loads.Count == 0)
        {
            return [];
        }

        var tenderIds = loads.Select(t => t.Id).ToList();
        var loadReferences = loads.Select(t => t.LoadReference).Distinct().ToList();

        var acceptances = (await db.TenderResponses.AsNoTracking()
            .Where(r => tenderIds.Contains(r.TenderId) && r.Response == TenderResponseType.Accepted)
            .Select(r => new { r.Id, r.TenderId, r.VehicleId, r.DriverReference })
            .ToListAsync(cancellationToken))
            .GroupBy(r => r.TenderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First());

        var vehicleIds = acceptances.Values.Where(a => a.VehicleId is not null).Select(a => a.VehicleId!.Value).Distinct().ToList();
        var registrations = vehicleIds.Count == 0
            ? new Dictionary<long, string>()
            : await db.TransporterVehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.RegistrationNumber, cancellationToken);

        var placements = (await db.VehiclePlacementRequests.AsNoTracking()
            .Where(p => p.TransporterId == transporterId && loadReferences.Contains(p.LoadReference))
            .Select(p => new { p.Id, p.LoadReference, p.Status })
            .ToListAsync(cancellationToken))
            .GroupBy(p => p.LoadReference)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First().Status);

        var executions = (await db.LoadExecutions.AsNoTracking()
            .Where(e => e.TransporterId == transporterId && loadReferences.Contains(e.LoadReference))
            .Select(e => new { e.Id, e.LoadReference, e.Status })
            .ToListAsync(cancellationToken))
            .GroupBy(e => e.LoadReference)
            .ToDictionary(g => g.Key, g => g.First());

        var executionIds = executions.Values.Select(e => e.Id).ToList();
        var pods = executionIds.Count == 0
            ? new Dictionary<long, PodStatus>()
            : (await db.PodRecords.AsNoTracking().Where(p => executionIds.Contains(p.LoadExecutionId))
                .Select(p => new { p.LoadExecutionId, p.Status })
                .ToListAsync(cancellationToken))
                .ToDictionary(p => p.LoadExecutionId, p => p.Status);

        var result = new List<VendorLoadDto>(loads.Count);
        foreach (var load in loads)
        {
            var acceptance = acceptances.GetValueOrDefault(load.Id);
            var vehicle = acceptance?.VehicleId is { } vehicleId && registrations.TryGetValue(vehicleId, out var reg) ? reg : null;
            var placement = placements.TryGetValue(load.LoadReference, out var placementStatus) ? (PlacementStatus?)placementStatus : null;
            var execution = executions.GetValueOrDefault(load.LoadReference);
            var pod = execution is null ? (PodStatus?)null : pods.TryGetValue(execution.Id, out var podStatus) ? podStatus : null;

            var loadStatus = (execution?.Status, pod) switch
            {
                (ExecutionStatus.Delivered, PodStatus.Accepted) => "Completed",
                (ExecutionStatus.Delivered, null) => "Delivered",
                (ExecutionStatus.Delivered, _) => "POD Pending",
                (ExecutionStatus.PickedUp, _) => "In Transit",
                (ExecutionStatus.AtPickup, _) => "At Pickup",
                _ => placement switch
                {
                    PlacementStatus.Placed => "Vehicle Placed",
                    PlacementStatus.LoadingStarted => "Loading Started",
                    _ when vehicle is not null => "Vehicle Confirmed",
                    _ => "Assigned"
                }
            };

            result.Add(new VendorLoadDto(load.Id, load.TenderNumber, load.LoadReference, load.Status, loadStatus, load.ServiceType,
                load.OriginLocationReference, load.DestinationLocationReference, load.WeightKg, load.PickupDateTime, load.DeliveryDateTime,
                vehicle, acceptance?.DriverReference, placement?.ToString(), execution?.Status.ToString(), pod?.ToString()));
        }

        return result;
    }
}
