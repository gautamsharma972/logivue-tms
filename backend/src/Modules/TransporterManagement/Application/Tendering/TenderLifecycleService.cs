using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Tendering;

/// <summary>
/// Time-driven and end-of-invitation tender transitions: expiry of unanswered invitations, and moving a
/// sequential tender on to the next transporter.
/// </summary>
public interface ITenderLifecycleService
{
    /// <summary>Expires open invitations past their deadline and advances sequential tenders. Returns the number expired.</summary>
    Task<int> ExpireDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves a sequential tender on after a rejection. The caller owns the transaction and must save.</summary>
    Task AdvanceAfterRejectionAsync(Tender ended, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Expires one invitation. The caller owns the transaction and must save.</summary>
    Task ExpireInvitationAsync(Tender tender, DateTime now, CancellationToken cancellationToken = default);
}

public sealed class TenderLifecycleService(
    IRepository<Tender> tenders,
    IRepository<TenderEvent> events,
    IRepository<TransporterAlert> alerts,
    ITransporterSettings settings,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    TimeProvider clock,
    ILogger<TenderLifecycleService> logger) : ITenderLifecycleService
{
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await tenders.ListAsync(t => (t.Status == TenderStatus.Sent || t.Status == TenderStatus.Viewed) && t.ResponseDeadline < now, cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        foreach (var tender in due)
        {
            await ExpireInvitationAsync(tender, now, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Expired {Count} tender invitation(s) past their response deadline", due.Count);
        return due.Count;
    }

    /// <summary>
    /// Marks one invitation expired and records the event and alert. The caller owns the transaction and must save.
    /// Sequential tenders then advance to the next waiting transporter.
    /// </summary>
    public async Task ExpireInvitationAsync(Tender tender, DateTime now, CancellationToken cancellationToken)
    {
        tender.Status = TenderStatus.Expired;
        tender.UpdatedAt = now;
        events.Add(Event(tender, "Expired", now, "system", "Response deadline passed."));
        alerts.Add(new TransporterAlert
        {
            AlertType = "TENDER_EXPIRED",
            Severity = Severity.High,
            TransporterId = tender.TransporterId,
            LoadReference = tender.LoadReference,
            EntityType = "Tender",
            EntityId = tender.Id.ToString(),
            Message = $"Tender {tender.TenderNumber} expired without a response from the transporter.",
            CreatedAt = now,
            Status = AlertStatus.Open
        });

        await AdvanceSequenceAsync(tender, now, cancellationToken);
    }

    public Task AdvanceAfterRejectionAsync(Tender ended, DateTime now, CancellationToken cancellationToken = default) =>
        AdvanceSequenceAsync(ended, now, cancellationToken);

    /// <summary>Starts the next waiting invitation of a sequential tender, with a fresh response window.</summary>
    private async Task AdvanceSequenceAsync(Tender ended, DateTime now, CancellationToken cancellationToken)
    {
        if (ended.TenderType != TenderType.Sequential || ended.SequenceNumber is null)
        {
            return;
        }

        var group = await tenders.ListAsync(t => t.TenderNumber == ended.TenderNumber, cancellationToken);
        var nextSeq = TenderRules.NextSequence(group.Select(g => (g.SequenceNumber, g.Status)), ended.SequenceNumber);
        if (nextSeq is null)
        {
            return;
        }

        var next = group.Single(g => g.SequenceNumber == nextSeq);
        var slaMinutes = await settings.GetAsync<int>(Configuration.SettingKeys.TenderResponseSlaMinutes, cancellationToken);
        var deadline = now.AddMinutes(slaMinutes);
        if (deadline > next.PickupDateTime)
        {
            deadline = next.PickupDateTime;
        }

        next.Status = TenderStatus.Sent;
        next.SentAt = now;
        next.ResponseDeadline = deadline;
        next.UpdatedAt = now;
        events.Add(Event(next, "Sent", now, "system", $"Advanced from sequence {ended.SequenceNumber}."));
    }

    private TenderEvent Event(Tender tender, string type, DateTime at, string actor, string? comments) => new()
    {
        TenderId = tender.Id,
        EventType = type,
        EventAt = at,
        PerformedBy = actor,
        Comments = comments
    };
}
