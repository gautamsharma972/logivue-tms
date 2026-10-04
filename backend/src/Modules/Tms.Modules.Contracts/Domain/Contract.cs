using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

public enum ContractStatus
{
    /// <summary>Being drafted; fully editable.</summary>
    Draft = 1,

    /// <summary>Locked while the approval runs.</summary>
    PendingApproval = 2,

    /// <summary>Approved and in force within its dates. Immutable: change it by revising.</summary>
    Active = 3,

    Rejected = 4,

    /// <summary>Ended early by the buyer.</summary>
    Terminated = 5,

    /// <summary>Replaced by a newer approved revision.</summary>
    Superseded = 6,

    /// <summary>Ran past its end date.</summary>
    Expired = 7,
}

/// <summary>
/// A freight agreement with one transporter: header, commercial terms, an optional diesel clause and the rate cards.
/// Once approved it never changes; a revision (new draft linked to it) replaces it when that is approved.
/// </summary>
public sealed class Contract : AggregateRoot, ITenantScoped
{
    public const int MaxRateCards = 5000;

    private readonly List<RateCard> _rateCards = [];

    private Contract()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Tenant-unique reference such as <c>CN-00012</c>; revisions share it and differ by <see cref="Revision"/>.</summary>
    public string Number { get; private set; } = null!;

    public int Revision { get; private set; }

    public Guid? RevisionOfId { get; private set; }

    public Guid TransporterId { get; private set; }

    public ContractType Type { get; private set; }

    public string Title { get; private set; } = null!;

    public ContractStatus Status { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly EffectiveTo { get; private set; }

    public int PaymentTermsDays { get; private set; }

    /// <summary>Expected yearly freight value; the amount fed to the approval matrix so bigger contracts need more approvers.</summary>
    public decimal? EstimatedAnnualSpend { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public ContractTerms Terms { get; private set; } = ContractTerms.Default;

    public FuelClause? Fuel { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public string? TerminationReason { get; private set; }

    /// <summary>Bumped whenever the rate set is replaced, so the audit trail shows that rates changed without storing every row.</summary>
    public int RatesRevision { get; private set; }

    public int RateCount { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public IReadOnlyCollection<RateCard> RateCards => _rateCards;

    public string Reference => Revision > 1 ? $"{Number} · R{Revision}" : Number;

    public static Result<Contract> Create(
        Guid tenantId,
        string number,
        Guid transporterId,
        ContractType type,
        string title,
        DateOnly effectiveFrom,
        DateOnly effectiveTo,
        int paymentTermsDays,
        decimal? estimatedAnnualSpend,
        Guid? ownerUserId,
        ContractTerms terms,
        FuelClause? fuel)
    {
        var contract = new Contract
        {
            TenantId = tenantId,
            Number = number,
            Revision = 1,
            TransporterId = transporterId,
            Type = type,
            Status = ContractStatus.Draft,
            OwnerUserId = ownerUserId,
        };

        var set = contract.SetHeader(title, effectiveFrom, effectiveTo, paymentTermsDays, estimatedAnnualSpend, ownerUserId, terms, fuel);
        return set.IsFailure ? set.Error : contract;
    }

    public Result UpdateHeader(
        string title, DateOnly effectiveFrom, DateOnly effectiveTo, int paymentTermsDays, decimal? estimatedAnnualSpend,
        Guid? ownerUserId, ContractTerms terms, FuelClause? fuel) =>
        EnsureEditable() is { } locked
            ? locked
            : SetHeader(title, effectiveFrom, effectiveTo, paymentTermsDays, estimatedAnnualSpend, ownerUserId, terms, fuel);

    private Result SetHeader(
        string title, DateOnly effectiveFrom, DateOnly effectiveTo, int paymentTermsDays, decimal? estimatedAnnualSpend,
        Guid? ownerUserId, ContractTerms terms, FuelClause? fuel)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            return Error.Validation("contracts.title_invalid", "A title of up to 200 characters is required.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["title"] = ["A title of up to 200 characters is required."] },
            };
        }

        if (effectiveTo <= effectiveFrom)
        {
            return Error.Validation("contracts.period_invalid", "The contract must end after it starts.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["effectiveTo"] = ["The contract must end after it starts."] },
            };
        }

        if (effectiveTo > effectiveFrom.AddYears(10))
        {
            return Error.Validation("contracts.period_too_long", "A contract can run for at most 10 years.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["effectiveTo"] = ["A contract can run for at most 10 years."] },
            };
        }

        if (paymentTermsDays is < 0 or > 365)
        {
            return Error.Validation("contracts.payment_terms_invalid", "Payment terms must be between 0 and 365 days.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["paymentTermsDays"] = ["Payment terms must be between 0 and 365 days."] },
            };
        }

        if (estimatedAnnualSpend is < 0)
        {
            return Error.Validation("contracts.spend_invalid", "The estimated spend cannot be negative.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["estimatedAnnualSpend"] = ["The estimated spend cannot be negative."] },
            };
        }

        var termsCheck = terms.Validate();
        if (termsCheck.IsFailure)
        {
            return termsCheck.Error;
        }

        if (fuel is not null && fuel.Validate() is { IsFailure: true } fuelCheck)
        {
            return fuelCheck.Error;
        }

        Title = title.Trim();
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        PaymentTermsDays = paymentTermsDays;
        EstimatedAnnualSpend = estimatedAnnualSpend;
        OwnerUserId = ownerUserId;
        Terms = terms;
        Fuel = fuel is null ? null : fuel with { Region = Text.Normalise(fuel.Region) };
        return Result.Success();
    }

    /// <summary>Replaces the whole rate set. Every row is validated against the contract type; the first problem is reported with its row number.</summary>
    public Result ReplaceRates(IReadOnlyList<RateCardSpec> specs)
    {
        if (EnsureEditable() is { } locked)
        {
            return locked;
        }

        if (specs.Count > MaxRateCards)
        {
            return RatesError($"A contract can hold at most {MaxRateCards} rates.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var row = $"Rate {i + 1}";

            if (spec.Pricing.Validate() is { IsFailure: true } priced)
            {
                return RatesError($"{row}: {priced.Error.Description}");
            }

            if (!spec.Pricing.SuitsContractType(Type))
            {
                return RatesError($"{row}: this pricing does not suit a {Type} contract.");
            }

            if (Type is ContractType.Ftl or ContractType.Dedicated && spec.VehicleTypeId is null)
            {
                return RatesError($"{row}: choose a vehicle type.");
            }

            if (Type == ContractType.Ptl && spec.VehicleTypeId is not null)
            {
                return RatesError($"{row}: part-load rates are not tied to a vehicle type.");
            }

            if (spec.MinDistanceKm is < 0 || spec.MaxDistanceKm is < 0 ||
                (spec.MinDistanceKm is { } lo && spec.MaxDistanceKm is { } hi && hi < lo))
            {
                return RatesError($"{row}: the distance band is invalid.");
            }

            if (spec.Origin.Kind == PlaceKind.Any && spec.Destination.Kind == PlaceKind.Any && spec.MinDistanceKm is null && spec.MaxDistanceKm is null)
            {
                return RatesError($"{row}: a rate for anywhere-to-anywhere needs a distance band.");
            }

            if (!seen.Add(RateCard.KeyOf(spec)))
            {
                return RatesError($"{row}: duplicates an earlier rate for {spec.Origin} → {spec.Destination}.");
            }
        }

        _rateCards.Clear();
        _rateCards.AddRange(specs.Select(s => RateCard.Create(TenantId, Id, s)));
        RateCount = _rateCards.Count;
        RatesRevision++;
        return Result.Success();

        static Result RatesError(string message) =>
            Error.Validation("contracts.rates_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { ["rates"] = [message] } };
    }

    /// <summary>What is still wrong before this can be sent for approval. Empty means ready.</summary>
    public IReadOnlyList<string> MissingForSubmission(DateOnly today)
    {
        var missing = new List<string>();
        if (RateCount == 0)
        {
            missing.Add("At least one rate");
        }

        if (EffectiveTo < today)
        {
            missing.Add("The end date is already in the past");
        }

        return missing;
    }

    public Result MarkSubmitted(Guid requestId, ApprovalStatus status, DateTimeOffset now)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.Rejected))
        {
            return Error.Conflict("contracts.not_submittable", "Only a draft or rejected contract can be submitted for approval.");
        }

        ApprovalRequestId = requestId;
        if (status == ApprovalStatus.Approved)
        {
            Activate(now);
        }
        else
        {
            Status = ContractStatus.PendingApproval;
        }

        return Result.Success();
    }

    /// <summary>Applies the final decision of the attached approval; outcomes for any other request are ignored. Returns whether anything changed.</summary>
    public bool ApplyApprovalOutcome(Guid requestId, ApprovalStatus outcome, DateTimeOffset now)
    {
        if (Status != ContractStatus.PendingApproval || ApprovalRequestId != requestId)
        {
            return false;
        }

        switch (outcome)
        {
            case ApprovalStatus.Approved:
                Activate(now);
                break;
            case ApprovalStatus.Rejected:
                Status = ContractStatus.Rejected;
                break;
            case ApprovalStatus.Cancelled:
                Status = ContractStatus.Draft;
                break;
            default:
                return false;
        }

        return true;
    }

    public Result Terminate(string reason, DateOnly today)
    {
        if (Status != ContractStatus.Active)
        {
            return Error.Conflict("contracts.not_active", "Only an active contract can be terminated.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("contracts.reason_required", "Say why the contract is being terminated.");
        }

        Status = ContractStatus.Terminated;
        TerminationReason = reason.Trim();
        if (EffectiveTo > today)
        {
            EffectiveTo = today < EffectiveFrom ? EffectiveFrom : today;
        }

        return Result.Success();
    }

    /// <summary>Marks an active contract whose end date has passed as expired. Returns whether it changed.</summary>
    public bool ExpireIfPast(DateOnly today)
    {
        if (Status != ContractStatus.Active || EffectiveTo >= today)
        {
            return false;
        }

        Status = ContractStatus.Expired;
        return true;
    }

    /// <summary>Called when a newer revision becomes active: this contract ends the day before the newer one starts.</summary>
    public void SupersedeBy(DateOnly newerEffectiveFrom)
    {
        if (Status is not (ContractStatus.Active or ContractStatus.Expired))
        {
            return;
        }

        Status = ContractStatus.Superseded;
        var lastDay = newerEffectiveFrom.AddDays(-1);
        if (lastDay < EffectiveTo)
        {
            EffectiveTo = lastDay < EffectiveFrom ? EffectiveFrom : lastDay;
        }
    }

    /// <summary>A new draft copy of this contract (header, terms, fuel clause and every rate) to edit and approve as the next revision.</summary>
    public Result<Contract> CreateRevision(DateOnly effectiveFrom, DateOnly effectiveTo, Guid? ownerUserId)
    {
        if (Status is not (ContractStatus.Active or ContractStatus.Expired))
        {
            return Error.Conflict("contracts.not_revisable", "Only an active or expired contract can be revised.");
        }

        var revision = new Contract
        {
            TenantId = TenantId,
            Number = Number,
            Revision = Revision + 1,
            RevisionOfId = Id,
            TransporterId = TransporterId,
            Type = Type,
            Status = ContractStatus.Draft,
        };

        var set = revision.SetHeader(Title, effectiveFrom, effectiveTo, PaymentTermsDays, EstimatedAnnualSpend, ownerUserId ?? OwnerUserId, Terms, Fuel);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var copied = revision.ReplaceRates(_rateCards.Select(c => c.ToSpec()).ToList());
        return copied.IsFailure ? copied.Error : revision;
    }

    /// <summary>Has been approved at some point (so its rates were once binding), whatever happened to it afterwards.</summary>
    public bool WasApproved => Status is ContractStatus.Active or ContractStatus.Terminated or ContractStatus.Superseded or ContractStatus.Expired;

    /// <summary>
    /// Whether the contract's rates govern a shipment on <paramref name="date"/>. Deliberately independent of what
    /// happened later: a freight bill for last month's shipment must still be checked against the contract that was in
    /// force then, even if it has since expired, been terminated or been replaced by a revision.
    /// </summary>
    public bool IsInForce(DateOnly date) => WasApproved && EffectiveFrom <= date && date <= EffectiveTo;

    /// <summary>What to show people: an "Active" contract past its end date is effectively expired even before the nightly job runs.</summary>
    public ContractStatus EffectiveStatus(DateOnly today) =>
        Status == ContractStatus.Active && EffectiveTo < today ? ContractStatus.Expired : Status;

    public int? DaysUntilExpiry(DateOnly today) =>
        Status == ContractStatus.Active ? EffectiveTo.DayNumber - today.DayNumber : null;

    private void Activate(DateTimeOffset now)
    {
        Status = ContractStatus.Active;
        ActivatedAt = now;
    }

    private Error? EnsureEditable() => Status switch
    {
        ContractStatus.Draft or ContractStatus.Rejected => null,
        ContractStatus.PendingApproval => Error.Conflict("contracts.locked", "This contract is awaiting approval and cannot be edited."),
        _ => Error.Conflict("contracts.immutable", "An approved contract cannot be edited. Create a revision instead."),
    };
}
