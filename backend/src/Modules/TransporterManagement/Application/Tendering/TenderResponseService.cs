using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Placement;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Tendering;

/// <summary>
/// A transporter's responses to a tender invitation. The <c>scopeTransporterId</c> argument is set for vendor callers,
/// who can only act on their own invitations; internal callers pass null and act on behalf of the transporter.
/// </summary>
public interface ITenderResponseService
{
    Task<TenderDetailDto> ViewAsync(long invitationId, long? scopeTransporterId, CancellationToken cancellationToken = default);

    Task<TenderDetailDto> AcceptAsync(long invitationId, long? scopeTransporterId, AcceptTenderRequest request, CancellationToken cancellationToken = default);

    Task<TenderDetailDto> RejectAsync(long invitationId, long? scopeTransporterId, RejectTenderRequest request, CancellationToken cancellationToken = default);

    Task<TenderDetailDto> CounterOfferAsync(long invitationId, long? scopeTransporterId, CounterOfferRequest request, CancellationToken cancellationToken = default);

    Task<TenderDetailDto> AssignVehicleAsync(long invitationId, long? scopeTransporterId, VehicleAssignmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Internal decision: the buyer accepts the transporter's counter-offer, which becomes the offered rate.</summary>
    Task<TenderDetailDto> AcceptCounterOfferAsync(long invitationId, CommentsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Internal decision: the buyer declines the counter-offer. The invitation closes as rejected for a rate issue.</summary>
    Task<TenderDetailDto> DeclineCounterOfferAsync(long invitationId, CommentsRequest request, CancellationToken cancellationToken = default);
}

public sealed class TenderResponseService(
    IRepository<Tender> tenders,
    IRepository<TenderEvent> events,
    IRepository<TenderResponse> responses,
    IRepository<TransporterVehicle> vehicles,
    IRepository<TransporterDriver> drivers,
    IComplianceService compliance,
    IRepository<TransporterAlert> alerts,
    ITenderLifecycleService lifecycle,
    ITransporterSettings settings,
    ITenderQueries queries,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<RejectTenderRequest> rejectValidator,
    IValidator<CounterOfferRequest> counterValidator,
    IValidator<VehicleAssignmentRequest> vehicleValidator,
    IPlacementService placementService,
    ILogger<TenderResponseService> logger) : ITenderResponseService
{
    public async Task<TenderDetailDto> ViewAsync(long invitationId, long? scopeTransporterId, CancellationToken cancellationToken = default)
    {
        var tender = await LoadScopedAsync(invitationId, scopeTransporterId, cancellationToken);
        if (tender.Status == TenderStatus.Draft)
        {
            throw NotFound(invitationId);
        }

        if (tender.Status == TenderStatus.Sent)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            tender.Status = TenderStatus.Viewed;
            tender.UpdatedAt = now;
            events.Add(new TenderEvent { TenderId = tender.Id, EventType = "Viewed", EventAt = now, PerformedBy = currentUser.UserId });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DetailAsync(invitationId, cancellationToken);
    }

    public async Task<TenderDetailDto> AcceptAsync(long invitationId, long? scopeTransporterId, AcceptTenderRequest request, CancellationToken cancellationToken = default)
    {
        var tender = await LoadScopedAsync(invitationId, scopeTransporterId, cancellationToken);
        TenderRules.EnsureResponsive(tender.Status, "accepted");
        await ExpireIfPastDeadlineAsync(tender, cancellationToken);

        if (request.QuotedRate is { } quoted && tender.OfferedRate is { } offered && quoted != offered)
        {
            throw new BusinessRuleException($"The quoted rate must match the offered rate of {offered:0.00}. Use a counter-offer to propose a different rate.", "RATE_MISMATCH");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        tender.Status = TenderStatus.Accepted;
        tender.UpdatedAt = now;
        responses.Add(new TenderResponse
        {
            TenderId = tender.Id,
            TransporterId = tender.TransporterId,
            Response = TenderResponseType.Accepted,
            ResponseAt = now,
            QuotedRate = tender.OfferedRate,
            Comments = Mapping.Clean(request.Comments)
        });
        events.Add(new TenderEvent { TenderId = tender.Id, EventType = "Accepted", EventAt = now, PerformedBy = currentUser.UserId, Comments = Mapping.Clean(request.Comments) });

        await audit.RecordAsync(new AuditEntry("Tender", tender.TenderNumber, "TenderAccepted", Reason: Mapping.Clean(request.Comments)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Tender {TenderNumber} accepted by transporter {TransporterId}", tender.TenderNumber, tender.TransporterId);
        return await DetailAsync(invitationId, cancellationToken);
    }

    public async Task<TenderDetailDto> RejectAsync(long invitationId, long? scopeTransporterId, RejectTenderRequest request, CancellationToken cancellationToken = default)
    {
        await rejectValidator.ValidateAndThrowAsync(request, cancellationToken);

        var reasons = await settings.GetAsync<List<string>>(SettingKeys.TenderRejectionReasons, cancellationToken);
        var code = request.ReasonCode.Trim().ToUpperInvariant();
        if (!reasons.Contains(code))
        {
            throw new BusinessRuleException($"'{request.ReasonCode}' is not a valid rejection reason.", "REJECTION_REASON_INVALID");
        }

        if (code == "OTHER" && string.IsNullOrWhiteSpace(request.Comments))
        {
            throw new BusinessRuleException("Describe the reason when rejecting with 'Other'.", "REJECTION_COMMENT_REQUIRED");
        }

        var tender = await LoadScopedAsync(invitationId, scopeTransporterId, cancellationToken);
        TenderRules.EnsureResponsive(tender.Status, "rejected");
        await ExpireIfPastDeadlineAsync(tender, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        tender.Status = TenderStatus.Rejected;
        tender.UpdatedAt = now;
        responses.Add(new TenderResponse
        {
            TenderId = tender.Id,
            TransporterId = tender.TransporterId,
            Response = TenderResponseType.Rejected,
            ResponseAt = now,
            Reason = code,
            Comments = Mapping.Clean(request.Comments)
        });
        events.Add(new TenderEvent { TenderId = tender.Id, EventType = "Rejected", EventAt = now, PerformedBy = currentUser.UserId, Comments = code });
        alerts.Add(new TransporterAlert
        {
            AlertType = "TENDER_REJECTED",
            Severity = LogiVue.Tms.TransporterManagement.Domain.Common.Severity.Medium,
            TransporterId = tender.TransporterId,
            LoadReference = tender.LoadReference,
            EntityType = "Tender",
            EntityId = tender.Id.ToString(),
            Message = $"Tender {tender.TenderNumber} was rejected: {code}.",
            CreatedAt = now,
            Status = AlertStatus.Open
        });

        await lifecycle.AdvanceAfterRejectionAsync(tender, now, cancellationToken);
        await audit.RecordAsync(new AuditEntry("Tender", tender.TenderNumber, "TenderRejected", Reason: code), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DetailAsync(invitationId, cancellationToken);
    }

    public async Task<TenderDetailDto> CounterOfferAsync(long invitationId, long? scopeTransporterId, CounterOfferRequest request, CancellationToken cancellationToken = default)
    {
        await counterValidator.ValidateAndThrowAsync(request, cancellationToken);

        if (!await settings.GetAsync<bool>(SettingKeys.TenderAllowCounterOffer, cancellationToken))
        {
            throw new BusinessRuleException("Counter-offers are not enabled for this tender process.", "COUNTER_OFFER_DISABLED");
        }

        var tender = await LoadScopedAsync(invitationId, scopeTransporterId, cancellationToken);
        TenderRules.EnsureResponsive(tender.Status, "counter-offered");
        await ExpireIfPastDeadlineAsync(tender, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        tender.Status = TenderStatus.Viewed;
        tender.UpdatedAt = now;
        responses.Add(new TenderResponse
        {
            TenderId = tender.Id,
            TransporterId = tender.TransporterId,
            Response = TenderResponseType.CounterOffered,
            ResponseAt = now,
            QuotedRate = request.Rate,
            Comments = Mapping.Clean(request.Comments)
        });
        events.Add(new TenderEvent { TenderId = tender.Id, EventType = "CounterOffered", EventAt = now, PerformedBy = currentUser.UserId, Comments = $"{request.Rate:0.00}" });

        await audit.RecordAsync(new AuditEntry("Tender", tender.TenderNumber, "TenderCounterOffered",
            NewValueJson: AuditJson.Serialize(new { request.Rate })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DetailAsync(invitationId, cancellationToken);
    }

    public async Task<TenderDetailDto> AssignVehicleAsync(long invitationId, long? scopeTransporterId, VehicleAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        await vehicleValidator.ValidateAndThrowAsync(request, cancellationToken);

        var tender = await LoadScopedAsync(invitationId, scopeTransporterId, cancellationToken);
        if (tender.Status is not (TenderStatus.Accepted or TenderStatus.Awarded))
        {
            throw new BusinessRuleException("A vehicle can be assigned only to an accepted or awarded load.", "ILLEGAL_TRANSITION");
        }

        var registration = VehicleSelection.Normalise(request.RegistrationNumber);
        var fleet = VehicleSelection.Require(
            (await vehicles.ListAsync(v => v.TransporterId == tender.TransporterId && v.RegistrationNumber == registration, cancellationToken)).SingleOrDefault(),
            registration, tender.VehicleTypeReference, tender.WeightKg);

        // A driver matched by mobile must hold a valid licence: an expired one blocks the assignment.
        var mobile = LogiVue.Tms.TransporterManagement.Application.Fleet.DriverService.MobileDigits(request.DriverMobile);
        var driver = (await drivers.ListAsync(d => d.TransporterId == tender.TransporterId, cancellationToken))
            .FirstOrDefault(d => LogiVue.Tms.TransporterManagement.Application.Fleet.DriverService.MobileDigits(d.Mobile) == mobile);
        if (driver is not null)
        {
            var complianceReport = await compliance.GetReportAsync(tender.TransporterId, cancellationToken);
            if (complianceReport.BlockedDriverIds?.Contains(driver.Id) == true)
            {
                throw new BusinessRuleException($"Driver {driver.FullName} has an expired licence and cannot be assigned.", "DRIVER_LICENCE_EXPIRED");
            }
        }

        if (request.ExpectedPlacementAt is { } placement && placement > tender.PickupDateTime)
        {
            throw new BusinessRuleException("Expected placement must be on or before pickup.", "PLACEMENT_AFTER_PICKUP");
        }

        if (request.EtaAt is { } eta && eta > tender.DeliveryDateTime)
        {
            throw new BusinessRuleException("The ETA must be on or before the delivery time.", "ETA_AFTER_DELIVERY");
        }

        var acceptance = (await responses.ListAsync(r => r.TenderId == tender.Id && r.Response == TenderResponseType.Accepted, cancellationToken))
            .OrderByDescending(r => r.Id)
            .FirstOrDefault()
            ?? throw new BusinessRuleException("No acceptance is recorded for this load.", "ILLEGAL_TRANSITION");

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var before = AuditJson.Serialize(acceptance);
        acceptance.VehicleId = fleet.Id;
        acceptance.DriverReference = request.DriverName.Trim();
        acceptance.DriverMobile = request.DriverMobile.Trim();
        acceptance.ExpectedPlacementAt = request.ExpectedPlacementAt;
        acceptance.EtaAt = request.EtaAt;

        tender.UpdatedAt = now;
        events.Add(new TenderEvent { TenderId = tender.Id, EventType = "VehicleAssigned", EventAt = now, PerformedBy = currentUser.UserId, Comments = registration });
        await audit.RecordAsync(new AuditEntry("TenderResponse", acceptance.Id.ToString(), "VehicleAssigned",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(acceptance)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await placementService.SyncVehicleAssignedAsync(tender.LoadReference, tender.TransporterId, fleet, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DetailAsync(invitationId, cancellationToken);
    }

    public async Task<TenderDetailDto> AcceptCounterOfferAsync(long invitationId, CommentsRequest request, CancellationToken cancellationToken = default)
    {
        var tender = await tenders.FindAsync(invitationId, cancellationToken) ?? throw NotFound(invitationId);
        TenderRules.EnsureResponsive(tender.Status, "accepted");
        await ExpireIfPastDeadlineAsync(tender, cancellationToken);

        var counter = await LatestCounterOfferAsync(tender, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var previousRate = tender.OfferedRate;
        tender.OfferedRate = counter.QuotedRate;
        tender.Status = TenderStatus.Accepted;
        tender.UpdatedAt = now;
        responses.Add(new TenderResponse
        {
            TenderId = tender.Id,
            TransporterId = tender.TransporterId,
            Response = TenderResponseType.Accepted,
            ResponseAt = now,
            QuotedRate = counter.QuotedRate,
            Comments = Mapping.Clean(request.Comments) ?? "Counter-offer accepted"
        });
        events.Add(new TenderEvent { TenderId = tender.Id, EventType = "CounterOfferAccepted", EventAt = now, PerformedBy = currentUser.UserId, Comments = $"{counter.QuotedRate:0.00}" });
        await audit.RecordAsync(new AuditEntry("Tender", tender.TenderNumber, "CounterOfferAccepted",
            OldValueJson: AuditJson.Serialize(new { Rate = previousRate }), NewValueJson: AuditJson.Serialize(new { Rate = counter.QuotedRate }),
            Reason: Mapping.Clean(request.Comments)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DetailAsync(invitationId, cancellationToken);
    }

    public async Task<TenderDetailDto> DeclineCounterOfferAsync(long invitationId, CommentsRequest request, CancellationToken cancellationToken = default)
    {
        var tender = await tenders.FindAsync(invitationId, cancellationToken) ?? throw NotFound(invitationId);
        await LatestCounterOfferAsync(tender, cancellationToken);

        return await RejectAsync(invitationId, null,
            new RejectTenderRequest("RATE_ISSUE", Mapping.Clean(request.Comments) ?? "Counter-offer declined"), cancellationToken);
    }

    private async Task<TenderResponse> LatestCounterOfferAsync(Tender tender, CancellationToken cancellationToken) =>
        (await responses.ListAsync(r => r.TenderId == tender.Id && r.Response == TenderResponseType.CounterOffered, cancellationToken))
            .OrderByDescending(r => r.Id)
            .FirstOrDefault()
        ?? throw new BusinessRuleException("No counter-offer is recorded for this invitation.", "NO_COUNTER_OFFER");

    /// <summary>
    /// An unanswered invitation past its deadline is expired and the expiry is committed before the caller
    /// is refused, so the record reflects what happened.
    /// </summary>
    private async Task ExpireIfPastDeadlineAsync(Tender tender, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (tender.ResponseDeadline > now)
        {
            return;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await lifecycle.ExpireInvitationAsync(tender, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        throw new BusinessRuleException("The response deadline has passed; the invitation has expired.", "TENDER_EXPIRED");
    }

    private async Task<Tender> LoadScopedAsync(long invitationId, long? scopeTransporterId, CancellationToken cancellationToken)
    {
        var tender = await tenders.FindAsync(invitationId, cancellationToken) ?? throw NotFound(invitationId);
        if (scopeTransporterId is { } scope && tender.TransporterId != scope)
        {
            throw NotFound(invitationId);
        }

        return tender;
    }

    private async Task<TenderDetailDto> DetailAsync(long invitationId, CancellationToken cancellationToken) =>
        await queries.GetDetailAsync(invitationId, null, cancellationToken) ?? throw NotFound(invitationId);

    private static NotFoundException NotFound(long invitationId) =>
        new($"Tender {invitationId} was not found.", "TENDER_NOT_FOUND");
}
