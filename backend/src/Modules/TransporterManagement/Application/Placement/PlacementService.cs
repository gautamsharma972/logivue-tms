using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Performance;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Severity = LogiVue.Tms.TransporterManagement.Domain.Common.Severity;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Placement;

/// <summary>
/// Vehicle placement for an accepted load: the transporter confirms, reports the vehicle, and the vehicle is placed
/// at the site. Vehicle assignment happens on the tender response; <see cref="SyncVehicleAssignedAsync"/> carries it
/// across to the placement. A scope argument is set for vendor callers, who see only their own placements.
/// </summary>
public interface IPlacementService
{
    Task<PlacementDto> CreateAsync(CreatePlacementRequest request, CancellationToken cancellationToken = default);

    Task<PlacementDto> GetAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default);

    Task<PagedResult<PlacementDto>> ListAsync(long? transporterId, PlacementStatus? status, long? scopeTransporterId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);

    Task<PlacementDto> ConfirmAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default);

    Task<PlacementDto> ReportAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default);

    Task<PlacementDto> PlaceAsync(long id, CancellationToken cancellationToken = default);

    Task<PlacementDto> NoShowAsync(long id, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<PlacementDto> CancelAsync(long id, ReasonRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Carries a vehicle assignment from the tender response onto the open placement. A different vehicle after an
    /// assignment is a replacement. The caller owns the transaction; this method saves and refreshes KPIs.
    /// </summary>
    Task SyncVehicleAssignedAsync(string loadReference, long transporterId, TransporterVehicle vehicle, CancellationToken cancellationToken = default);
}

public sealed class PlacementService(
    IRepository<VehiclePlacementRequest> placements,
    IRepository<VehiclePlacementEvent> events,
    IRepository<Tender> tenders,
    IRepository<TenderResponse> responses,
    IRepository<TransporterVehicle> vehicles,
    IRepository<TransporterAlert> alerts,
    IPerformanceService performance,
    ITransporterSettings settings,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    TimeProvider clock,
    IValidator<CreatePlacementRequest> createValidator,
    IValidator<ReasonRequest> reasonValidator,
    ILogger<PlacementService> logger) : IPlacementService
{
    public async Task<PlacementDto> CreateAsync(CreatePlacementRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var load = request.LoadReference.Trim();
        var tender = (await tenders.ListAsync(t => t.LoadReference == load && t.TransporterId == request.TransporterId
            && (t.Status == TenderStatus.Accepted || t.Status == TenderStatus.Awarded), cancellationToken))
            .OrderByDescending(t => t.Id)
            .FirstOrDefault()
            ?? throw new BusinessRuleException("A placement can be requested only for a load the transporter has accepted or been awarded.", "PLACEMENT_REQUIRES_ACCEPTED_LOAD");

        if (request.RequiredPlacementAt > tender.PickupDateTime)
        {
            throw new BusinessRuleException("The required placement must be on or before pickup.", "PLACEMENT_AFTER_PICKUP");
        }

        if (await placements.AnyAsync(p => p.LoadReference == load && p.TransporterId == request.TransporterId
            && (p.Status == PlacementStatus.Requested || p.Status == PlacementStatus.Confirmed || p.Status == PlacementStatus.VehicleAssigned
                || p.Status == PlacementStatus.Reported || p.Status == PlacementStatus.Placed || p.Status == PlacementStatus.LoadingStarted), cancellationToken))
        {
            throw new ConflictException($"Load {load} already has an open placement for this transporter.", "PLACEMENT_ALREADY_OPEN");
        }

        var acceptance = (await responses.ListAsync(r => r.TenderId == tender.Id && r.Response == TenderResponseType.Accepted, cancellationToken))
            .OrderByDescending(r => r.Id)
            .FirstOrDefault();

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var placement = new VehiclePlacementRequest
        {
            LoadReference = load,
            TransporterId = request.TransporterId,
            VehicleTypeReference = request.VehicleTypeReference ?? tender.VehicleTypeReference,
            RequestedAt = now,
            RequiredPlacementAt = request.RequiredPlacementAt,
            Status = PlacementStatus.Requested,
            VehicleId = acceptance?.VehicleId
        };
        placements.Add(placement);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        Record(placement, "Requested", now);
        await audit.RecordAsync(new AuditEntry("VehiclePlacement", placement.Id.ToString(), "PlacementRequested",
            NewValueJson: AuditJson.Serialize(new { placement.LoadReference, placement.TransporterId, placement.RequiredPlacementAt })), cancellationToken);
        await performance.RefreshAsync(placement.TransporterId, [placement.RequiredPlacementAt], cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Placement {PlacementId} requested for load {LoadReference}", placement.Id, load);
        return (await ToDtosAsync([placement], cancellationToken)).Single();
    }

    public async Task<PlacementDto> GetAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default) =>
        (await ToDtosAsync([await LoadAsync(id, scopeTransporterId, cancellationToken)], cancellationToken)).Single();

    public async Task<PagedResult<PlacementDto>> ListAsync(long? transporterId, PlacementStatus? status, long? scopeTransporterId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var owner = scopeTransporterId ?? transporterId;
        var normalisedPage = Paging.NormalisePage(page);
        var size = Paging.NormalisePageSize(pageSize);
        var rows = await placements.PageAsync(p => (owner == null || p.TransporterId == owner) && (status == null || p.Status == status),
            normalisedPage, size, cancellationToken);

        return new PagedResult<PlacementDto>(await ToDtosAsync(rows.Items, cancellationToken), normalisedPage, size, rows.TotalCount);
    }

    public async Task<PlacementDto> ConfirmAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default)
    {
        var placement = await LoadAsync(id, scopeTransporterId, cancellationToken);
        EnsureTransition(placement, PlacementStatus.Requested, "confirmed");

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        placement.ConfirmedAt = now;
        placement.Status = placement.VehicleId is null ? PlacementStatus.Confirmed : PlacementStatus.VehicleAssigned;
        Record(placement, "Confirmed", now);
        if (placement.Status == PlacementStatus.VehicleAssigned)
        {
            Record(placement, "VehicleAssigned", now);
        }

        await audit.RecordAsync(new AuditEntry("VehiclePlacement", id.ToString(), "PlacementConfirmed"), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await ToDtosAsync([placement], cancellationToken)).Single();
    }

    public async Task<PlacementDto> ReportAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken = default)
    {
        var placement = await LoadAsync(id, scopeTransporterId, cancellationToken);
        EnsureTransition(placement, PlacementStatus.VehicleAssigned, "reported");

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        placement.ReportedAt = now;
        placement.Status = PlacementStatus.Reported;
        Record(placement, "Reported", now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await ToDtosAsync([placement], cancellationToken)).Single();
    }

    public async Task<PlacementDto> PlaceAsync(long id, CancellationToken cancellationToken = default)
    {
        var placement = await LoadAsync(id, null, cancellationToken);
        if (placement.Status is not (PlacementStatus.VehicleAssigned or PlacementStatus.Reported))
        {
            throw Illegal(placement, "placed");
        }

        if (placement.VehicleId is null)
        {
            throw new BusinessRuleException("A vehicle must be assigned before it can be placed.", "PLACEMENT_VEHICLE_REQUIRED");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        placement.PlacedAt = now;
        placement.Status = PlacementStatus.Placed;
        Record(placement, "Placed", now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await performance.RefreshAsync(placement.TransporterId, [placement.RequiredPlacementAt], cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await ToDtosAsync([placement], cancellationToken)).Single();
    }

    public async Task<PlacementDto> NoShowAsync(long id, ReasonRequest request, CancellationToken cancellationToken = default)
    {
        await reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var placement = await LoadAsync(id, null, cancellationToken);
        if (!PlacementRules.IsOpen(placement.Status))
        {
            throw Illegal(placement, "marked as a no-show");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var grace = await settings.GetAsync<int>(SettingKeys.PlacementGraceMinutes, cancellationToken);
        if (now <= placement.RequiredPlacementAt.AddMinutes(grace))
        {
            throw new BusinessRuleException("A no-show can be recorded only after the placement grace period has passed.", "NO_SHOW_TOO_EARLY");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        placement.Status = PlacementStatus.NoShow;
        placement.ExceptionReason = request.Reason.Trim();
        Record(placement, "NoShow", now, request.Reason.Trim());
        alerts.Add(new TransporterAlert
        {
            AlertType = "PLACEMENT_NO_SHOW",
            Severity = Severity.High,
            TransporterId = placement.TransporterId,
            LoadReference = placement.LoadReference,
            EntityType = "VehiclePlacement",
            EntityId = placement.Id.ToString(),
            Message = $"No vehicle was placed for load {placement.LoadReference} by {placement.RequiredPlacementAt:yyyy-MM-dd HH:mm} UTC.",
            CreatedAt = now,
            Status = AlertStatus.Open
        });
        await audit.RecordAsync(new AuditEntry("VehiclePlacement", id.ToString(), "PlacementNoShow", Reason: request.Reason.Trim()), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await performance.RefreshAsync(placement.TransporterId, [placement.RequiredPlacementAt], cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await ToDtosAsync([placement], cancellationToken)).Single();
    }

    public async Task<PlacementDto> CancelAsync(long id, ReasonRequest request, CancellationToken cancellationToken = default)
    {
        await reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var placement = await LoadAsync(id, null, cancellationToken);
        if (!PlacementRules.IsOpen(placement.Status))
        {
            throw Illegal(placement, "cancelled");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        placement.Status = PlacementStatus.Cancelled;
        placement.CancelledAt = now;
        placement.ExceptionReason = request.Reason.Trim();
        Record(placement, "Cancelled", now, request.Reason.Trim());
        await audit.RecordAsync(new AuditEntry("VehiclePlacement", id.ToString(), "PlacementCancelled", Reason: request.Reason.Trim()), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await performance.RefreshAsync(placement.TransporterId, [placement.RequiredPlacementAt], cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await ToDtosAsync([placement], cancellationToken)).Single();
    }

    public async Task SyncVehicleAssignedAsync(string loadReference, long transporterId, TransporterVehicle vehicle, CancellationToken cancellationToken = default)
    {
        var placement = (await placements.ListAsync(p => p.LoadReference == loadReference && p.TransporterId == transporterId
            && (p.Status == PlacementStatus.Requested || p.Status == PlacementStatus.Confirmed || p.Status == PlacementStatus.VehicleAssigned
                || p.Status == PlacementStatus.Reported), cancellationToken))
            .OrderByDescending(p => p.Id)
            .FirstOrDefault();

        // Changes after placement do not alter the placement record; the placed vehicle is what happened.
        if (placement is null || placement.VehicleId == vehicle.Id)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var isReplacement = placement.VehicleId is not null && placement.Status is PlacementStatus.VehicleAssigned or PlacementStatus.Reported;
        placement.VehicleId = vehicle.Id;

        if (isReplacement)
        {
            placement.ReplacementCount++;
            placement.Status = PlacementStatus.VehicleAssigned;
            placement.ReportedAt = null;
            Record(placement, "VehicleReplaced", now, vehicle.RegistrationNumber);
        }
        else if (placement.Status == PlacementStatus.Confirmed)
        {
            placement.Status = PlacementStatus.VehicleAssigned;
            Record(placement, "VehicleAssigned", now, vehicle.RegistrationNumber);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (isReplacement)
        {
            await performance.RefreshAsync(placement.TransporterId, [placement.RequiredPlacementAt], cancellationToken);
        }
    }

    private void Record(VehiclePlacementRequest placement, string eventType, DateTime now, string? remarks = null) =>
        events.Add(new VehiclePlacementEvent
        {
            PlacementRequestId = placement.Id,
            EventType = eventType,
            EventAt = now,
            VehicleId = placement.VehicleId,
            Remarks = remarks
        });

    private async Task<VehiclePlacementRequest> LoadAsync(long id, long? scopeTransporterId, CancellationToken cancellationToken)
    {
        var placement = await placements.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Placement {id} was not found.", "PLACEMENT_NOT_FOUND");
        if (scopeTransporterId is { } scope && placement.TransporterId != scope)
        {
            throw new NotFoundException($"Placement {id} was not found.", "PLACEMENT_NOT_FOUND");
        }

        return placement;
    }

    private static void EnsureTransition(VehiclePlacementRequest placement, PlacementStatus expected, string action)
    {
        if (placement.Status != expected)
        {
            throw Illegal(placement, action);
        }
    }

    private static BusinessRuleException Illegal(VehiclePlacementRequest placement, string action) =>
        new($"Placement {placement.Id} cannot be {action} while it is {placement.Status}.", "PLACEMENT_ILLEGAL_TRANSITION");

    /// <summary>Maps placements to DTOs with one query each for events and vehicles, so lists never run per-row queries.</summary>
    private async Task<IReadOnlyList<PlacementDto>> ToDtosAsync(IReadOnlyCollection<VehiclePlacementRequest> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var vehicleIds = rows.Where(r => r.VehicleId is not null).Select(r => r.VehicleId!.Value).Distinct().ToList();
        var grace = await settings.GetAsync<int>(SettingKeys.PlacementGraceMinutes, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        var eventRows = ids.Count == 0
            ? new List<VehiclePlacementEvent>()
            : await events.ListAsync(e => ids.Contains(e.PlacementRequestId), cancellationToken);
        var registrations = vehicleIds.Count == 0
            ? new Dictionary<long, string>()
            : (await vehicles.ListAsync(v => vehicleIds.Contains(v.Id), cancellationToken)).ToDictionary(v => v.Id, v => v.RegistrationNumber);
        var eventsByPlacement = eventRows.GroupBy(e => e.PlacementRequestId).ToDictionary(g => g.Key, g => g.OrderBy(e => e.EventAt).ThenBy(e => e.Id).ToList());

        return rows.Select(p => new PlacementDto(
            p.Id, p.LoadReference, p.TransporterId, p.VehicleTypeReference, p.VehicleId,
            p.VehicleId is { } vid && registrations.TryGetValue(vid, out var reg) ? reg : null,
            p.RequestedAt, p.RequiredPlacementAt, p.ConfirmedAt, p.ReportedAt, p.PlacedAt, p.LoadingStartedAt,
            p.Status, PlacementRules.SlaStatus(p, now, grace), PlacementRules.DelayMinutes(p), p.ReplacementCount, p.ExceptionReason,
            eventsByPlacement.GetValueOrDefault(p.Id, []).Select(e => new PlacementEventDto(e.EventType, e.EventAt, e.VehicleId, e.Remarks)).ToList()))
            .ToList();
    }
}
