using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Domain;

/// <summary>How a load is offered to several transporters. A single transporter is the plain tender on the shipment itself.</summary>
public enum TenderMode
{
    /// <summary>One transporter at a time, in the order chosen. A refusal or a missed deadline moves the load to the next.</summary>
    Sequential = 1,

    /// <summary>Every transporter is invited at once; each may bid with a vehicle and driver, and a planner awards one.</summary>
    Broadcast = 2,
}

public enum TenderStatus
{
    Open = 1,
    Awarded = 2,
    Cancelled = 3,

    /// <summary>Every transporter refused or let the deadline pass.</summary>
    Exhausted = 4,
}

public enum InviteeStatus
{
    /// <summary>Sequential only: not contacted yet.</summary>
    Waiting = 1,

    /// <summary>The transporter can answer.</summary>
    Sent = 2,

    /// <summary>Broadcast only: the transporter bid with a vehicle and driver and waits to be awarded.</summary>
    Bid = 3,

    Accepted = 4,
    Rejected = 5,
    Expired = 6,

    /// <summary>Another transporter was awarded.</summary>
    Superseded = 7,

    Cancelled = 8,
}

public enum CounterStatus
{
    None = 0,
    Pending = 1,
    Agreed = 2,
    Declined = 3,
}

/// <summary>One transporter's place in a tender, with their answer and any counter-offer.</summary>
public sealed class TenderInvitee : Entity, ITenantScoped
{
    private TenderInvitee()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TenderId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public Guid TransporterId { get; private set; }

    public Guid ContractId { get; private set; }

    public string ContractReference { get; private set; } = null!;

    /// <summary>What the contract priced the load at when the tender started. Staff only.</summary>
    public decimal QuotedTotal { get; private set; }

    public int Sequence { get; private set; }

    public InviteeStatus Status { get; internal set; }

    public DateTimeOffset? SentAt { get; internal set; }

    public DateTimeOffset? Deadline { get; internal set; }

    public DateTimeOffset? RespondedAt { get; internal set; }

    public string? Reason { get; internal set; }

    /// <summary>The rate the transporter proposes instead of the contract's. The transporter never sees the contract price.</summary>
    public decimal? CounterRate { get; internal set; }

    public string? CounterComment { get; internal set; }

    public CounterStatus CounterStatus { get; internal set; }

    public decimal? AgreedRate { get; internal set; }

    public Guid? BidVehicleId { get; internal set; }

    public Guid? BidDriverId { get; internal set; }

    public string? BidVehicleRegistration { get; internal set; }

    public string? BidDriverName { get; internal set; }

    public bool IsLive => Status is InviteeStatus.Sent or InviteeStatus.Bid;

    internal static TenderInvitee Create(Guid tenantId, Guid tenderId, Guid shipmentId, int sequence, FreightQuoteResult quote) => new()
    {
        TenantId = tenantId, TenderId = tenderId, ShipmentId = shipmentId, Sequence = sequence, TransporterId = quote.TransporterId,
        ContractId = quote.ContractId, ContractReference = quote.ContractReference, QuotedTotal = quote.Total, Status = InviteeStatus.Waiting,
    };
}

/// <summary>What happened in a tender, for the timeline.</summary>
public sealed class TenderEvent : Entity, ITenantScoped
{
    private TenderEvent()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid TenderId { get; private set; }

    /// <summary>The transporter this concerns; null for events about the whole tender.</summary>
    public Guid? InviteeId { get; private set; }

    public string Type { get; private set; } = null!;

    public DateTimeOffset At { get; private set; }

    public string? Comments { get; private set; }

    internal static TenderEvent Create(Guid tenantId, Guid tenderId, Guid? inviteeId, string type, DateTimeOffset at, string? comments) => new()
    {
        TenantId = tenantId, TenderId = tenderId, InviteeId = inviteeId, Type = type, At = at, Comments = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim(),
    };
}

/// <summary>
/// Offers one shipment to several transporters (<see cref="TenderMode"/>). The shipment itself still has at most one transporter at a
/// time; this keeps the rest: who was invited, in which order, what each answered and why a load moved on.
/// </summary>
public sealed class TenderRound : AggregateRoot, ITenantScoped
{
    public const int MinResponseMinutes = 15;
    public const int MaxResponseMinutes = 4320;
    public const int DefaultResponseMinutes = 240;
    public const int MaxInvitees = 10;

    private readonly List<TenderInvitee> _invitees = [];
    private readonly List<TenderEvent> _events = [];

    private TenderRound()
    {
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; } = null!;

    public Guid ShipmentId { get; private set; }

    public string ShipmentNumber { get; private set; } = null!;

    public TenderMode Mode { get; private set; }

    public TenderStatus Status { get; private set; }

    public int ResponseMinutes { get; private set; }

    public string? Notes { get; private set; }

    public Guid? AwardedTransporterId { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public string? CloseReason { get; private set; }

    public IReadOnlyList<TenderInvitee> Invitees => _invitees;

    public IReadOnlyList<TenderEvent> Events => _events;

    public bool IsOpen => Status == TenderStatus.Open;

    /// <param name="quotes">Already priced and checked, in the order the planner wants them tried (sequential) or listed (broadcast).</param>
    public static Result<TenderRound> Start(
        Guid tenantId, string number, Guid shipmentId, string shipmentNumber, TenderMode mode, int? responseMinutes, string? notes,
        IReadOnlyList<FreightQuoteResult> quotes, DateTimeOffset now)
    {
        if (!Enum.IsDefined(mode))
        {
            return Error.Validation("tenders.mode_invalid", "Choose sequential or broadcast.");
        }

        if (quotes.Count < 2 || quotes.Count > MaxInvitees)
        {
            return Error.Validation("tenders.invitee_count", $"Choose between 2 and {MaxInvitees} transporters. To offer a load to one, tender it directly.");
        }

        if (quotes.Select(q => q.TransporterId).Distinct().Count() != quotes.Count)
        {
            return Error.Validation("tenders.duplicate_transporter", "Each transporter can be invited once.");
        }

        var minutes = responseMinutes ?? DefaultResponseMinutes;
        if (minutes is < MinResponseMinutes or > MaxResponseMinutes)
        {
            return Error.Validation("tenders.response_window", $"The response time must be between {MinResponseMinutes} minutes and {MaxResponseMinutes / 60} hours.");
        }

        var round = new TenderRound
        {
            TenantId = tenantId, Number = number, ShipmentId = shipmentId, ShipmentNumber = shipmentNumber, Mode = mode, Status = TenderStatus.Open,
            ResponseMinutes = minutes, Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        for (var i = 0; i < quotes.Count; i++)
        {
            round._invitees.Add(TenderInvitee.Create(tenantId, round.Id, shipmentId, i + 1, quotes[i]));
        }

        round.Log(null, "Started", now, $"{mode} tender to {quotes.Count} transporters.");
        if (mode == TenderMode.Broadcast)
        {
            round._invitees.ForEach(i => round.Send(i, now));
        }
        else
        {
            round.Send(round._invitees[0], now);
        }

        return round;
    }

    /// <summary>The transporter that currently holds a sequential tender.</summary>
    public TenderInvitee? Current => _invitees.FirstOrDefault(i => i.Status == InviteeStatus.Sent);

    public TenderInvitee? InviteeFor(Guid transporterId) => _invitees.FirstOrDefault(i => i.TransporterId == transporterId);

    /// <summary>The transporter answered "yes" to a sequential offer: the shipment took its vehicle and driver, so the tender is done.</summary>
    public void MarkAccepted(Guid transporterId, DateTimeOffset now)
    {
        var invitee = InviteeFor(transporterId);
        if (invitee is null || !IsOpen)
        {
            return;
        }

        invitee.Status = InviteeStatus.Accepted;
        invitee.RespondedAt = now;
        Close(TenderStatus.Awarded, now, null, transporterId);
        CancelWaiting(now, "Another transporter accepted.");
        Log(invitee.Id, "Accepted", now, null);
    }

    /// <summary>The transporter refused. In a sequential tender the next one should now be contacted (see <see cref="StartNext"/>).</summary>
    public Result Reject(Guid transporterId, string reason, DateTimeOffset now)
    {
        var invitee = Live(transporterId);
        if (invitee is null)
        {
            return Error.Conflict("tenders.not_invited", "This tender is not waiting for an answer from that transporter.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("tenders.reason_required", "Say why the load is being declined.");
        }

        invitee.Status = InviteeStatus.Rejected;
        invitee.RespondedAt = now;
        invitee.Reason = reason.Trim();
        Log(invitee.Id, "Rejected", now, invitee.Reason);
        if (Mode == TenderMode.Broadcast)
        {
            Raise(new ShipmentRejected(ShipmentId, TenantId, ShipmentNumber, transporterId, now, invitee.Reason));
        }

        return Result.Success();
    }

    /// <summary>Marks every live invitation past its deadline as expired and returns them. Bids that are waiting for an award do not expire.</summary>
    public IReadOnlyList<TenderInvitee> ExpireDue(DateTimeOffset now)
    {
        var due = _invitees.Where(i => i.Status == InviteeStatus.Sent && i.Deadline is { } d && d <= now).ToList();
        foreach (var invitee in due)
        {
            invitee.Status = InviteeStatus.Expired;
            invitee.RespondedAt = now;
            invitee.Reason = "No response before the deadline.";
            Log(invitee.Id, "Expired", now, null);
            if (Mode == TenderMode.Broadcast)
            {
                Raise(new ShipmentTenderExpired(ShipmentId, TenantId, ShipmentNumber, invitee.TransporterId, now));
            }
        }

        return due;
    }

    /// <summary>
    /// After a sequential invitation ended: contact the next waiting transporter. Returns it, or null when nobody is left (the tender is
    /// then exhausted). For a broadcast tender it only closes the tender once nobody can answer or be awarded.
    /// </summary>
    public TenderInvitee? StartNext(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return null;
        }

        if (Mode == TenderMode.Sequential)
        {
            var next = _invitees.Where(i => i.Status == InviteeStatus.Waiting).OrderBy(i => i.Sequence).FirstOrDefault();
            if (next is not null)
            {
                Send(next, now);
                return next;
            }
        }

        if (!_invitees.Any(i => i.IsLive))
        {
            Close(TenderStatus.Exhausted, now, "Every transporter refused or did not answer.", null);
            Log(null, "Exhausted", now, CloseReason);
        }

        return null;
    }

    /// <summary>Gives up on an invitation that cannot continue (e.g. its contract no longer prices the load) and tries the next.</summary>
    public void Skip(TenderInvitee invitee, string reason, DateTimeOffset now)
    {
        invitee.Status = InviteeStatus.Rejected;
        invitee.RespondedAt = now;
        invitee.Reason = reason;
        Log(invitee.Id, "Skipped", now, reason);
    }

    /// <summary>Broadcast: a transporter offers the load a vehicle and driver, optionally at its own rate.</summary>
    public Result Bid(Guid transporterId, FleetVehicle vehicle, FleetDriver driver, decimal? counterRate, string? comments, DateTimeOffset now)
    {
        if (Mode != TenderMode.Broadcast)
        {
            return Error.Conflict("tenders.not_broadcast", "Only a broadcast tender takes bids; answer a sequential offer by accepting it.");
        }

        var invitee = Live(transporterId);
        if (invitee is null || invitee.Status != InviteeStatus.Sent)
        {
            return Error.Conflict("tenders.not_invited", "This tender is not waiting for a bid from that transporter.");
        }

        if (counterRate is <= 0)
        {
            return Error.Validation("tenders.rate_invalid", "A proposed rate must be more than zero.");
        }

        invitee.Status = InviteeStatus.Bid;
        invitee.RespondedAt = now;
        invitee.BidVehicleId = vehicle.Id;
        invitee.BidVehicleRegistration = vehicle.RegistrationNumber;
        invitee.BidDriverId = driver.Id;
        invitee.BidDriverName = driver.FullName;
        if (counterRate is { } rate)
        {
            invitee.CounterRate = rate;
            invitee.CounterComment = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
            invitee.CounterStatus = CounterStatus.Pending;
        }

        Log(invitee.Id, "Bid", now, counterRate is null ? invitee.BidVehicleRegistration : $"{invitee.BidVehicleRegistration} at ₹{counterRate:0.00}");
        return Result.Success();
    }

    /// <summary>The transporter proposes a different rate instead of accepting the contract's. A planner agrees or declines.</summary>
    public Result Counter(Guid transporterId, decimal rate, string? comments, DateTimeOffset now)
    {
        var invitee = Live(transporterId);
        if (invitee is null)
        {
            return Error.Conflict("tenders.not_invited", "This tender is not waiting for an answer from that transporter.");
        }

        if (rate <= 0)
        {
            return Error.Validation("tenders.rate_invalid", "A proposed rate must be more than zero.");
        }

        if (invitee.CounterStatus == CounterStatus.Pending)
        {
            return Error.Conflict("tenders.counter_pending", "A counter-offer is already waiting for a decision.");
        }

        invitee.CounterRate = rate;
        invitee.CounterComment = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
        invitee.CounterStatus = CounterStatus.Pending;
        invitee.AgreedRate = null;
        Log(invitee.Id, "CounterOffered", now, $"₹{rate:0.00}");
        return Result.Success();
    }

    public Result<TenderInvitee> AgreeCounter(Guid inviteeId, string? comments, DateTimeOffset now)
    {
        var invitee = PendingCounter(inviteeId);
        if (invitee.IsFailure)
        {
            return invitee;
        }

        invitee.Value.CounterStatus = CounterStatus.Agreed;
        invitee.Value.AgreedRate = invitee.Value.CounterRate;
        Log(invitee.Value.Id, "CounterAgreed", now, comments);
        return invitee;
    }

    /// <summary>The planner refused the counter-offer; the transporter's invitation then ends as declined for a rate issue.</summary>
    public Result<TenderInvitee> DeclineCounter(Guid inviteeId, string? comments, DateTimeOffset now)
    {
        var invitee = PendingCounter(inviteeId);
        if (invitee.IsFailure)
        {
            return invitee;
        }

        invitee.Value.CounterStatus = CounterStatus.Declined;
        Log(invitee.Value.Id, "CounterDeclined", now, comments);
        var reason = string.IsNullOrWhiteSpace(comments) ? "Rate not agreed." : $"Rate not agreed: {comments.Trim()}";
        invitee.Value.Status = InviteeStatus.Rejected;
        invitee.Value.RespondedAt = now;
        invitee.Value.Reason = reason;
        if (Mode == TenderMode.Broadcast)
        {
            Raise(new ShipmentRejected(ShipmentId, TenantId, ShipmentNumber, invitee.Value.TransporterId, now, reason));
        }

        return invitee;
    }

    /// <summary>Broadcast: the planner picks one bid. Everyone else's offer is closed.</summary>
    public Result<TenderInvitee> Award(Guid inviteeId, DateTimeOffset now)
    {
        if (!IsOpen || Mode != TenderMode.Broadcast)
        {
            return Error.Conflict("tenders.not_awardable", "Only an open broadcast tender can be awarded.");
        }

        var winner = _invitees.FirstOrDefault(i => i.Id == inviteeId);
        if (winner is null)
        {
            return Error.NotFound("tenders.invitee_not_found", "That transporter is not on this tender.");
        }

        if (winner.Status != InviteeStatus.Bid)
        {
            return Error.Conflict("tenders.no_bid", "That transporter has not bid on this load.");
        }

        if (winner.CounterStatus == CounterStatus.Pending)
        {
            return Error.Conflict("tenders.counter_pending", "Agree or decline the counter-offer before awarding.");
        }

        winner.Status = InviteeStatus.Accepted;
        Close(TenderStatus.Awarded, now, null, winner.TransporterId);
        foreach (var other in _invitees.Where(i => i.Id != winner.Id && i.Status is InviteeStatus.Sent or InviteeStatus.Bid or InviteeStatus.Waiting))
        {
            WithdrawInvitee(other, InviteeStatus.Superseded, "Another transporter was awarded.", now);
        }

        Log(winner.Id, "Awarded", now, null);
        return winner;
    }

    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Error.Conflict("tenders.not_open", "This tender is already closed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("tenders.reason_required", "Say why the tender is being cancelled.");
        }

        foreach (var invitee in _invitees.Where(i => i.Status is InviteeStatus.Sent or InviteeStatus.Bid or InviteeStatus.Waiting))
        {
            WithdrawInvitee(invitee, InviteeStatus.Cancelled, reason.Trim(), now);
        }

        Close(TenderStatus.Cancelled, now, reason.Trim(), null);
        Log(null, "Cancelled", now, reason.Trim());
        return Result.Success();
    }

    private Result<TenderInvitee> PendingCounter(Guid inviteeId)
    {
        var invitee = _invitees.FirstOrDefault(i => i.Id == inviteeId);
        if (invitee is null)
        {
            return Error.NotFound("tenders.invitee_not_found", "That transporter is not on this tender.");
        }

        return IsOpen && invitee.IsLive && invitee.CounterStatus == CounterStatus.Pending
            ? invitee
            : Error.Conflict("tenders.no_counter", "There is no counter-offer waiting for a decision from that transporter.");
    }

    private TenderInvitee? Live(Guid transporterId) => IsOpen ? _invitees.FirstOrDefault(i => i.TransporterId == transporterId && i.IsLive) : null;

    private void Send(TenderInvitee invitee, DateTimeOffset now)
    {
        invitee.Status = InviteeStatus.Sent;
        invitee.SentAt = now;
        invitee.Deadline = now.AddMinutes(ResponseMinutes);
        Log(invitee.Id, "Sent", now, null);

        // A sequential invitation is raised as ShipmentTendered by the shipment itself when it is allocated; broadcast has no allocation.
        if (Mode == TenderMode.Broadcast)
        {
            Raise(new ShipmentTendered(ShipmentId, TenantId, ShipmentNumber, invitee.TransporterId, now));
        }
    }

    private void WithdrawInvitee(TenderInvitee invitee, InviteeStatus to, string reason, DateTimeOffset now)
    {
        var wasOffered = invitee.IsLive;
        invitee.Status = to;
        invitee.RespondedAt = now;
        invitee.Reason = reason;
        Log(invitee.Id, to.ToString(), now, reason);
        if (wasOffered)
        {
            Raise(new ShipmentTenderWithdrawn(ShipmentId, TenantId, ShipmentNumber, invitee.TransporterId, reason));
        }
    }

    private void CancelWaiting(DateTimeOffset now, string reason)
    {
        foreach (var invitee in _invitees.Where(i => i.Status == InviteeStatus.Waiting))
        {
            WithdrawInvitee(invitee, InviteeStatus.Cancelled, reason, now);
        }
    }

    private void Close(TenderStatus status, DateTimeOffset now, string? reason, Guid? awardedTo)
    {
        Status = status;
        ClosedAt = now;
        CloseReason = reason;
        AwardedTransporterId = awardedTo;
    }

    private void Log(Guid? inviteeId, string type, DateTimeOffset at, string? comments) =>
        _events.Add(TenderEvent.Create(TenantId, Id, inviteeId, type, at, comments));
}
