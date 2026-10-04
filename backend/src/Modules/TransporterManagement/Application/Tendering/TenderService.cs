using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Eligibility;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Notifications;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Tendering;

/// <summary>Internal tender management: creation, distribution, award and cancellation.</summary>
public interface ITenderService
{
    Task<IReadOnlyList<TenderInvitationDto>> CreateAsync(CreateTenderRequest request, CancellationToken cancellationToken = default);

    Task<TenderDetailDto> SendAsync(long invitationId, CancellationToken cancellationToken = default);

    Task<TenderDetailDto> AwardAsync(long invitationId, CommentsRequest request, CancellationToken cancellationToken = default);

    Task<TenderDetailDto> CancelAsync(long invitationId, CommentsRequest request, CancellationToken cancellationToken = default);
}

public sealed class TenderService(
    IRepository<Tender> tenders,
    IRepository<TenderEvent> events,
    IRepository<Transporter> transporters,
    IRepository<LogiVue.Tms.TransporterManagement.Domain.Configuration.ServiceTypeDefinition> serviceTypes,
    IRepository<TransporterAlert> alerts,
    ITransporterEligibilityService eligibility,
    ITransporterSettings settings,
    ITenderQueries queries,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    INotificationService notifications,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<CreateTenderRequest> createValidator,
    ILogger<TenderService> logger) : ITenderService
{
    public async Task<IReadOnlyList<TenderInvitationDto>> CreateAsync(CreateTenderRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        await ServiceTypeGuard.EnsureActiveAsync(serviceTypes, request.ServiceType, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var ids = request.TransporterIds.ToList();
        EnsureCardinality(request.TenderType, ids.Count);

        if (request.PickupDateTime >= request.DeliveryDateTime)
        {
            throw new BusinessRuleException("Pickup must be before delivery.", "TENDER_WINDOW_INVALID");
        }

        var slaMinutes = await settings.GetAsync<int>(SettingKeys.TenderResponseSlaMinutes, cancellationToken);
        var deadline = request.ResponseDeadline ?? now.AddMinutes(slaMinutes);
        if (deadline <= now)
        {
            throw new BusinessRuleException("The response deadline must be in the future.", "TENDER_DEADLINE_INVALID");
        }

        if (deadline > request.PickupDateTime)
        {
            throw new BusinessRuleException("The response deadline must be before pickup.", "TENDER_DEADLINE_INVALID");
        }

        var names = (await transporters.ListAsync(t => ids.Contains(t.Id), cancellationToken)).ToDictionary(t => t.Id);
        var missing = ids.Where(id => !names.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException($"Transporters not found: {string.Join(", ", missing)}.", "TRANSPORTER_NOT_FOUND");
        }

        await EnsureEligibleAsync(request, ids, names, cancellationToken);

        var number = await NextNumberAsync(now, cancellationToken);
        var tenderEntities = new List<Tender>();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        for (var i = 0; i < ids.Count; i++)
        {
            var tender = new Tender
            {
                TenderNumber = number,
                LoadReference = request.LoadReference.Trim(),
                TransporterId = ids[i],
                TenderType = request.TenderType,
                OfferedRate = request.OfferedRate,
                VehicleTypeReference = request.VehicleTypeReference,
                PickupDateTime = request.PickupDateTime,
                DeliveryDateTime = request.DeliveryDateTime,
                ResponseDeadline = deadline,
                SequenceNumber = request.TenderType == TenderType.Sequential ? i + 1 : null,
                OriginLocationReference = request.OriginLocationReference,
                DestinationLocationReference = request.DestinationLocationReference,
                ServiceType = request.ServiceType.Trim().ToUpperInvariant(),
                WeightKg = request.WeightKg,
                VolumeM3 = request.VolumeM3,
                Currency = (request.Currency ?? "INR").Trim().ToUpperInvariant(),
                Notes = Mapping.Clean(request.Notes),
                Status = TenderStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = currentUser.UserId
            };
            tenders.Add(tender);
            tenderEntities.Add(tender);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var tender in tenderEntities)
        {
            events.Add(new TenderEvent { TenderId = tender.Id, EventType = "Created", EventAt = now, PerformedBy = currentUser.UserId });
        }

        await audit.RecordAsync(new AuditEntry("Tender", number, "TenderCreated",
            NewValueJson: AuditJson.Serialize(new { number, request.TenderType, invitations = ids.Count, request.LoadReference })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Tender {TenderNumber} created as {TenderType} for {Count} transporter(s)", number, request.TenderType, ids.Count);

        return tenderEntities.Select(t => ToDto(t, names[t.TransporterId].LegalName)).ToList();
    }

    public async Task<TenderDetailDto> SendAsync(long invitationId, CancellationToken cancellationToken = default)
    {
        var tender = await LoadAsync(invitationId, cancellationToken);
        var group = await tenders.ListAsync(t => t.TenderNumber == tender.TenderNumber, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        var toSend = tender.TenderType == TenderType.Sequential
            ? group.Where(g => g.SequenceNumber == group.Where(x => x.Status == TenderStatus.Draft).Min(x => x.SequenceNumber)).ToList()
            : group.Where(g => g.Status == TenderStatus.Draft).ToList();

        if (toSend.Count == 0)
        {
            throw new BusinessRuleException($"Tender {tender.TenderNumber} has no invitations waiting to be sent.", "ILLEGAL_TRANSITION");
        }

        if (toSend.Any(t => t.ResponseDeadline <= now))
        {
            throw new BusinessRuleException("The response deadline has passed. Create a new tender with a later deadline.", "TENDER_DEADLINE_INVALID");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        foreach (var invitation in toSend)
        {
            invitation.Status = TenderStatus.Sent;
            invitation.SentAt = now;
            invitation.UpdatedAt = now;
            events.Add(new TenderEvent { TenderId = invitation.Id, EventType = "Sent", EventAt = now, PerformedBy = currentUser.UserId });
        }

        await audit.RecordAsync(new AuditEntry("Tender", tender.TenderNumber, "TenderSent",
            NewValueJson: AuditJson.Serialize(new { invitations = toSend.Select(t => t.Id) })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var invitation in toSend)
        {
            await notifications.SendAsync(new NotificationRequest(NotificationChannel.InApp, $"transporter:{invitation.TransporterId}",
                $"New tender {tender.TenderNumber}", $"Load {invitation.LoadReference}: respond by {invitation.ResponseDeadline:yyyy-MM-dd HH:mm} UTC.",
                "Tender", invitation.Id.ToString()), cancellationToken);
        }

        return await queries.GetDetailAsync(invitationId, null, cancellationToken)
            ?? throw new NotFoundException($"Tender {invitationId} was not found.", "TENDER_NOT_FOUND");
    }

    public async Task<TenderDetailDto> AwardAsync(long invitationId, CommentsRequest request, CancellationToken cancellationToken = default)
    {
        var tender = await LoadAsync(invitationId, cancellationToken);
        if (tender.Status != TenderStatus.Accepted)
        {
            throw new BusinessRuleException("Only an accepted invitation can be awarded.", "ILLEGAL_TRANSITION");
        }

        var group = await tenders.ListAsync(t => t.TenderNumber == tender.TenderNumber, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        tender.Status = TenderStatus.Awarded;
        tender.UpdatedAt = now;
        events.Add(new TenderEvent { TenderId = tender.Id, EventType = "Awarded", EventAt = now, PerformedBy = currentUser.UserId, Comments = Mapping.Clean(request.Comments) });

        foreach (var other in group.Where(g => g.Id != tender.Id && TenderRules.IsOpen(g.Status)))
        {
            other.Status = TenderStatus.Cancelled;
            other.UpdatedAt = now;
            events.Add(new TenderEvent { TenderId = other.Id, EventType = "Cancelled", EventAt = now, PerformedBy = currentUser.UserId, Comments = "Another transporter was awarded." });
        }

        await audit.RecordAsync(new AuditEntry("Tender", tender.TenderNumber, "TenderAwarded",
            NewValueJson: AuditJson.Serialize(new { awarded = tender.TransporterId })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetDetailAsync(invitationId, null, cancellationToken)
            ?? throw new NotFoundException($"Tender {invitationId} was not found.", "TENDER_NOT_FOUND");
    }

    public async Task<TenderDetailDto> CancelAsync(long invitationId, CommentsRequest request, CancellationToken cancellationToken = default)
    {
        var tender = await LoadAsync(invitationId, cancellationToken);
        var group = await tenders.ListAsync(t => t.TenderNumber == tender.TenderNumber, cancellationToken);
        if (group.Any(g => g.Status == TenderStatus.Awarded))
        {
            throw new BusinessRuleException("An awarded tender cannot be cancelled.", "TENDER_AWARDED");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var open = group.Where(g => TenderRules.IsOpen(g.Status)).ToList();
        if (open.Count == 0)
        {
            throw new BusinessRuleException("The tender has no open invitations to cancel.", "ILLEGAL_TRANSITION");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        foreach (var invitation in open)
        {
            invitation.Status = TenderStatus.Cancelled;
            invitation.UpdatedAt = now;
            events.Add(new TenderEvent { TenderId = invitation.Id, EventType = "Cancelled", EventAt = now, PerformedBy = currentUser.UserId, Comments = Mapping.Clean(request.Comments) });
        }

        await audit.RecordAsync(new AuditEntry("Tender", tender.TenderNumber, "TenderCancelled", Reason: Mapping.Clean(request.Comments)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetDetailAsync(invitationId, null, cancellationToken)
            ?? throw new NotFoundException($"Tender {invitationId} was not found.", "TENDER_NOT_FOUND");
    }

    /// <summary>Direct tenders have one transporter, sequential and broadcast tenders have several.</summary>
    private static void EnsureCardinality(TenderType type, int count)
    {
        var valid = type switch
        {
            TenderType.Direct => count == 1,
            TenderType.Sequential => count >= 2,
            TenderType.Broadcast => count >= 2,
            _ => false
        };

        if (!valid)
        {
            var rule = type == TenderType.Direct ? "exactly one transporter" : "at least two transporters";
            throw new BusinessRuleException($"A {type} tender needs {rule}.", "TENDER_TRANSPORTER_COUNT");
        }
    }

    /// <summary>Every invited transporter must be eligible for the load, so suspended or blacklisted carriers cannot be tendered.</summary>
    private async Task EnsureEligibleAsync(CreateTenderRequest request, IReadOnlyList<long> ids, IReadOnlyDictionary<long, Transporter> names, CancellationToken cancellationToken)
    {
        var selection = new TransporterSelectionRequest(
            request.OriginLocationReference, request.DestinationLocationReference, request.VehicleTypeReference,
            request.ServiceType, request.WeightKg, request.VolumeM3, DateOnly.FromDateTime(request.PickupDateTime),
            [], false, null, request.PickupDateTime, request.DeliveryDateTime);

        var candidates = await eligibility.CheckAsync(selection, cancellationToken);
        var rejected = candidates
            .Where(c => ids.Contains(c.TransporterId) && !c.Eligible)
            .Select(c => new Shared.Errors.ApiErrorDetail(names[c.TransporterId].TransporterCode, string.Join(" ", c.Reasons)))
            .ToList();

        if (rejected.Count > 0)
        {
            throw new BusinessRuleException("One or more transporters are not eligible for this load.", "TENDER_TRANSPORTER_INELIGIBLE", rejected);
        }
    }

    private async Task<string> NextNumberAsync(DateTime now, CancellationToken cancellationToken)
    {
        var prefix = $"TND-{now:yyyyMMdd}-";
        var existing = await tenders.ListAsync(t => t.TenderNumber.StartsWith(prefix), cancellationToken);
        var next = existing.Select(t => t.TenderNumber).Distinct().Count() + 1;
        return $"{prefix}{next:000}";
    }

    private async Task<Tender> LoadAsync(long invitationId, CancellationToken cancellationToken) =>
        await tenders.FindAsync(invitationId, cancellationToken)
        ?? throw new NotFoundException($"Tender {invitationId} was not found.", "TENDER_NOT_FOUND");

    private static TenderInvitationDto ToDto(Tender t, string transporterName) =>
        new(t.Id, t.TenderNumber, t.TenderType, t.TransporterId, transporterName, t.Status, t.SequenceNumber, t.LoadReference,
            t.OriginLocationReference, t.DestinationLocationReference, t.ServiceType, t.VehicleTypeReference, t.WeightKg, t.VolumeM3,
            t.OfferedRate, t.Currency, t.PickupDateTime, t.DeliveryDateTime, t.ResponseDeadline, t.SentAt, t.Notes, t.CreatedAt);
}
