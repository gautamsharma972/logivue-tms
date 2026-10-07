using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>Paused by the buyer: its rates do not apply from the day it was suspended until it is resumed.</summary>
    Suspended = 8,

    /// <summary>A draft that was abandoned before it was approved.</summary>
    Cancelled = 9,
}

/// <summary>Why a revision exists: a change inside the contract's term, or the next term.</summary>
public enum RevisionKind
{
    Original = 1,
    Amendment = 2,
    Renewal = 3,
}

/// <summary>A period when an active contract was paused. The end is empty while it is still paused.</summary>
public sealed record Suspension(DateOnly From, DateOnly? To, string Reason)
{
    public bool Covers(DateOnly date) => date >= From && (To is null || date <= To);
}

/// <summary>The header fields beyond the original set. All optional on the wire, so older callers keep working.</summary>
public sealed record ContractExtras(
    string Currency = "INR",
    string? BusinessUnit = null,
    string? PrimaryContact = null,
    int RenewalNoticeDays = 60,
    bool AutoRenewal = false,
    IReadOnlyList<ContractType>? Services = null)
{
    public static ContractExtras Default { get; } = new();
}

/// <summary>
/// A freight agreement with one transporter: header, commercial terms, an optional diesel clause and the rate cards.
/// Once approved it never changes; a revision (new draft linked to it) replaces it when that is approved.
/// </summary>
public sealed class Contract : AggregateRoot, ITenantScoped
{
    public const int MaxRateCards = 5000;

    private readonly List<RateCard> _rateCards = [];
    private readonly List<DphRule> _dphRules = [];
    private readonly List<ContractAccessorial> _accessorials = [];
    private readonly List<ContractCapacity> _capacities = [];
    private readonly List<ContractSla> _slas = [];

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

    public IReadOnlyCollection<DphRule> DphRules => _dphRules;

    public IReadOnlyCollection<ContractAccessorial> Accessorials => _accessorials;

    public IReadOnlyCollection<ContractCapacity> Capacities => _capacities;

    public IReadOnlyCollection<ContractSla> Slas => _slas;

    public string Currency { get; private set; } = "INR";

    public string? BusinessUnit { get; private set; }

    public string? PrimaryContact { get; private set; }

    /// <summary>How many days before the end a renewal is due.</summary>
    public int RenewalNoticeDays { get; private set; } = 60;

    /// <summary>When set, a renewal draft is prepared automatically at the notice date. It is never activated without approval.</summary>
    public bool AutoRenewal { get; private set; }

    /// <summary>Every service the contract covers (FTL, PTL, dedicated). Empty on contracts written before this existed: they cover <see cref="Type"/> only.</summary>
    public IReadOnlyList<ContractType> Services { get; private set; } = [];

    /// <summary>The version of the rating logic this contract was agreed under.</summary>
    public string CalculationVersion { get; private set; } = "1.0";

    public RevisionKind RevisionKind { get; private set; } = RevisionKind.Original;

    public IReadOnlyList<Suspension> Suspensions { get; private set; } = [];

    public IReadOnlyList<ContractType> EffectiveServices => Services.Count > 0 ? Services : [Type];

    public bool IsSuspended => Status == ContractStatus.Suspended;

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

    /// <summary>Sets the commercial header fields beyond the original set. The contract's own type is always one of its services.</summary>
    public Result ApplyExtras(ContractExtras extras)
    {
        if (EnsureEditable() is { } locked)
        {
            return locked;
        }

        static Result Fail(string field, string message) =>
            Error.Validation("contracts.extras_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { [field] = [message] } };

        var currency = (extras.Currency ?? "INR").Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
        {
            return Fail("currency", "Use a three-letter currency code such as INR.");
        }

        if (extras.RenewalNoticeDays is < 0 or > 365)
        {
            return Fail("renewalNoticeDays", "The renewal notice must be between 0 and 365 days.");
        }

        if (extras.BusinessUnit?.Length > 100 || extras.PrimaryContact?.Length > 200)
        {
            return Fail("businessUnit", "The business unit can be 100 characters and the contact 200.");
        }

        var services = (extras.Services ?? []).Append(Type).Distinct().OrderBy(x => x).ToList();
        if (services.Any(x => !Enum.IsDefined(x)))
        {
            return Fail("services", "One of the services is not known.");
        }

        // A service cannot be dropped while a rate still prices it.
        if (_rateCards.FirstOrDefault(r => !services.Contains(r.Pricing.ServiceType())) is { } orphan)
        {
            return Fail("services", $"A {orphan.Pricing.ServiceType()} rate exists, so {orphan.Pricing.ServiceType()} must stay a service of this contract.");
        }

        Currency = currency;
        BusinessUnit = string.IsNullOrWhiteSpace(extras.BusinessUnit) ? null : extras.BusinessUnit.Trim();
        PrimaryContact = string.IsNullOrWhiteSpace(extras.PrimaryContact) ? null : extras.PrimaryContact.Trim();
        RenewalNoticeDays = extras.RenewalNoticeDays;
        AutoRenewal = extras.AutoRenewal;
        Services = services;
        return Result.Success();
    }

    public ContractExtras ToExtras() => new(Currency, BusinessUnit, PrimaryContact, RenewalNoticeDays, AutoRenewal, EffectiveServices);

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

    /// <summary>
    /// Replaces the whole rate set. Every row is validated against the contract's services; the first problem is reported with its row number.
    /// <paramref name="previous"/> are the rates of the revision this one replaces (by code): a rate whose terms are unchanged keeps its version, a changed one moves up by one.
    /// </summary>
    public Result ReplaceRates(IReadOnlyList<RateCardSpec> specs, IReadOnlyDictionary<string, RateCard>? previous = null)
    {
        if (EnsureEditable() is { } locked)
        {
            return locked;
        }

        if (specs.Count > MaxRateCards)
        {
            return RatesError($"A contract can hold at most {MaxRateCards} rates.");
        }

        var services = EffectiveServices;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var row = $"Rate {i + 1}";
            var service = spec.Pricing.ServiceType();

            if (spec.Pricing.Validate() is { IsFailure: true } priced)
            {
                return RatesError($"{row}: {priced.Error.Description}");
            }

            if (!services.Contains(service))
            {
                return RatesError(services.Count == 1 ? $"{row}: this pricing does not suit a {Type} contract." : $"{row}: this pricing is for {service}, which this contract does not cover.");
            }

            if (service is ContractType.Ftl or ContractType.Dedicated && spec.VehicleTypeId is null)
            {
                return RatesError($"{row}: choose a vehicle type.");
            }

            if (service == ContractType.Ptl && spec.VehicleTypeId is not null)
            {
                return RatesError($"{row}: part-load rates are not tied to a vehicle type.");
            }

            if (spec.MinDistanceKm is < 0 || spec.MaxDistanceKm is < 0 ||
                (spec.MinDistanceKm is { } lo && spec.MaxDistanceKm is { } hi && hi < lo))
            {
                return RatesError($"{row}: the distance band is invalid.");
            }

            if ((spec.Extras ?? RateExtras.None).Validate() is { IsFailure: true } extras)
            {
                return RatesError($"{row}: {extras.Error.Description}");
            }

            if (spec.Origin.Kind == PlaceKind.Any && spec.Destination.Kind == PlaceKind.Any && spec.MinDistanceKm is null && spec.MaxDistanceKm is null)
            {
                return RatesError($"{row}: a rate for anywhere-to-anywhere needs a distance band.");
            }

            if (!seen.Add(RateCard.KeyOf(spec)))
            {
                return RatesError($"{row}: duplicates an earlier rate for {spec.Origin} → {spec.Destination}.");
            }

            if (spec.Extras?.DphRuleCode is { Length: > 0 } dph && !_dphRules.Any(r => string.Equals(r.Code, dph.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return RatesError($"{row}: DPH rule {dph.Trim().ToUpperInvariant()} is not defined on this contract. Add the rule first.");
            }
        }

        var cards = new List<RateCard>(specs.Count);
        foreach (var spec in specs)
        {
            var card = RateCard.Create(TenantId, Id, spec);
            if (!codes.Add(card.Code))
            {
                return RatesError($"Two rates have the code {card.Code}. Give each rate its own code.");
            }

            if (previous is not null && previous.TryGetValue(card.Code, out var before))
            {
                card = RateCard.Create(TenantId, Id, spec, SameTerms(before.ToSpec(), card.ToSpec()) ? before.Version : before.Version + 1);
            }

            cards.Add(card);
        }

        _rateCards.Clear();
        _rateCards.AddRange(cards);
        RateCount = _rateCards.Count;
        RatesRevision++;
        return Result.Success();

        static Result RatesError(string message) =>
            Error.Validation("contracts.rates_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { ["rates"] = [message] } };
    }

    /// <summary>Replaces the DPH rules. Versions of one code are numbered by their start date and must not overlap; a rate may name only a rule that exists.</summary>
    public Result ReplaceDphRules(IReadOnlyList<DphRuleSpec> specs)
    {
        if (EnsureEditable() is { } locked)
        {
            return locked;
        }

        static Result Fail(string message) =>
            Error.Validation("contracts.dph_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { ["dphRules"] = [message] } };

        if (specs.Count > 100)
        {
            return Fail("A contract can hold at most 100 DPH rule versions.");
        }

        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i].Validate() is { IsFailure: true } bad)
            {
                return Fail($"DPH rule {i + 1}: {bad.Error.Description}");
            }
        }

        var window = (DphRuleSpec r) => (From: r.EffectiveFrom ?? EffectiveFrom, To: r.EffectiveTo ?? EffectiveTo);
        foreach (var group in specs.GroupBy(r => r.Code.Trim().ToUpperInvariant()))
        {
            var ordered = group.OrderBy(r => window(r).From).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (window(ordered[i]).From <= window(ordered[i - 1]).To)
                {
                    return Fail($"Versions of DPH rule {group.Key} overlap in time ({window(ordered[i - 1]).From:dd MMM yyyy}–{window(ordered[i - 1]).To:dd MMM yyyy} and {window(ordered[i]).From:dd MMM yyyy}–{window(ordered[i]).To:dd MMM yyyy}).");
                }
            }
        }

        var defaults = specs.Where(r => r.IsDefault).ToList();
        for (var i = 0; i < defaults.Count; i++)
        {
            for (var j = i + 1; j < defaults.Count; j++)
            {
                var (a, b) = (window(defaults[i]), window(defaults[j]));
                if (!string.Equals(defaults[i].Code, defaults[j].Code, StringComparison.OrdinalIgnoreCase) && a.From <= b.To && b.From <= a.To)
                {
                    return Fail($"{defaults[i].Code} and {defaults[j].Code} are both the default rule at the same time. Only one default can apply on a day.");
                }
            }
        }

        var codes = specs.Select(r => r.Code.Trim().ToUpperInvariant()).ToHashSet();
        if (_rateCards.FirstOrDefault(c => c.DphRuleCode is { } code && !codes.Contains(code)) is { } dangling)
        {
            return Fail($"Rate {dangling.Code} uses DPH rule {dangling.DphRuleCode}, which is not in this list.");
        }

        var rules = new List<DphRule>();
        foreach (var group in specs.GroupBy(r => r.Code.Trim().ToUpperInvariant()))
        {
            var version = 1;
            foreach (var spec in group.OrderBy(r => window(r).From))
            {
                rules.Add(DphRule.Create(TenantId, Id, spec, version++));
            }
        }

        _dphRules.Clear();
        _dphRules.AddRange(rules);
        return Result.Success();
    }

    public Result ReplaceAccessorials(IReadOnlyList<AccessorialSpec> specs)
    {
        if (EnsureEditable() is { } locked)
        {
            return locked;
        }

        static Result Fail(string message) =>
            Error.Validation("contracts.accessorial_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { ["accessorials"] = [message] } };

        if (specs.Count > 200)
        {
            return Fail("A contract can hold at most 200 extra charges.");
        }

        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i].Validate() is { IsFailure: true } bad)
            {
                return Fail($"Charge {i + 1}: {bad.Error.Description}");
            }
        }

        foreach (var group in specs.GroupBy(a => a.Code.Trim().ToUpperInvariant()))
        {
            var list = group.OrderBy(a => a.ValidFrom ?? EffectiveFrom).ToList();
            for (var i = 1; i < list.Count; i++)
            {
                if ((list[i].ValidFrom ?? EffectiveFrom) <= (list[i - 1].ValidTo ?? EffectiveTo))
                {
                    return Fail($"{group.Key} is defined twice for the same dates. Give each version its own period.");
                }
            }
        }

        _accessorials.Clear();
        _accessorials.AddRange(specs.Select(a => ContractAccessorial.Create(TenantId, Id, a)));
        return Result.Success();
    }

    public Result ReplaceCapacities(IReadOnlyList<CapacitySpec> specs)
    {
        if (EnsureEditable() is { } locked)
        {
            return locked;
        }

        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i].Validate() is { IsFailure: true } bad)
            {
                return Error.Validation("contracts.capacity_invalid", $"Commitment {i + 1}: {bad.Error.Description}") with { ValidationErrors = bad.Error.ValidationErrors };
            }
        }

        _capacities.Clear();
        _capacities.AddRange(specs.Select(c => ContractCapacity.Create(TenantId, Id, c)));
        return Result.Success();
    }

    public Result ReplaceSlas(IReadOnlyList<SlaSpec> specs)
    {
        if (EnsureEditable() is { } locked)
        {
            return locked;
        }

        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i].Validate() is { IsFailure: true } bad)
            {
                return Error.Validation("contracts.sla_invalid", $"Service level {i + 1}: {bad.Error.Description}") with { ValidationErrors = bad.Error.ValidationErrors };
            }

            if (!EffectiveServices.Contains(specs[i].Service))
            {
                return Error.Validation("contracts.sla_invalid", $"Service level {i + 1}: {specs[i].Service} is not a service of this contract.");
            }
        }

        _slas.Clear();
        _slas.AddRange(specs.Select(c => ContractSla.Create(TenantId, Id, c)));
        return Result.Success();
    }

    private static readonly JsonSerializerOptions Compare = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Whether two rates say the same thing. Compared by what they serialise to, because the pricing holds lists.</summary>
    private static bool SameTerms(RateCardSpec a, RateCardSpec b) => JsonSerializer.Serialize(a, Compare) == JsonSerializer.Serialize(b, Compare);

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
        if (Status is not (ContractStatus.Active or ContractStatus.Suspended))
        {
            return Error.Conflict("contracts.not_active", "Only an active or suspended contract can be terminated.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("contracts.reason_required", "Say why the contract is being terminated.");
        }

        CloseOpenSuspension(today);
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
        if (Status is not (ContractStatus.Active or ContractStatus.Suspended) || EffectiveTo >= today)
        {
            return false;
        }

        CloseOpenSuspension(EffectiveTo);
        Status = ContractStatus.Expired;
        Raise(new ContractExpired(TenantId, Id, Reference, EffectiveTo));
        if (RateCount > 0)
        {
            Raise(new RateExpired(TenantId, Id, Reference, RateCount));
        }

        return true;
    }

    /// <summary>Pauses an active contract from <paramref name="today"/>: its rates stop applying until it is resumed. Shipments before that day stay priceable.</summary>
    public Result Suspend(string reason, DateOnly today)
    {
        if (Status != ContractStatus.Active)
        {
            return Error.Conflict("contracts.not_active", "Only an active contract can be suspended.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("contracts.reason_required", "Say why the contract is being suspended.");
        }

        Suspensions = [.. Suspensions, new Suspension(today < EffectiveFrom ? EffectiveFrom : today, null, reason.Trim())];
        Status = ContractStatus.Suspended;
        Raise(new ContractSuspended(TenantId, Id, Reference, reason.Trim()));
        return Result.Success();
    }

    /// <summary>Puts a suspended contract back in force from <paramref name="today"/>.</summary>
    public Result Resume(DateOnly today)
    {
        if (Status != ContractStatus.Suspended)
        {
            return Error.Conflict("contracts.not_suspended", "Only a suspended contract can be resumed.");
        }

        CloseOpenSuspension(today.AddDays(-1));
        Status = ContractStatus.Active;
        return Result.Success();
    }

    /// <summary>Abandons a draft that will not be approved. A contract that was ever approved is terminated instead.</summary>
    public Result Cancel(string reason)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.Rejected))
        {
            return Error.Conflict("contracts.not_cancellable", "Only a draft or rejected contract can be cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("contracts.reason_required", "Say why the contract is being cancelled.");
        }

        Status = ContractStatus.Cancelled;
        TerminationReason = reason.Trim();
        return Result.Success();
    }

    private void CloseOpenSuspension(DateOnly lastDay)
    {
        if (Suspensions.Count > 0 && Suspensions[^1].To is null)
        {
            var open = Suspensions[^1];
            Suspensions = [.. Suspensions.Take(Suspensions.Count - 1), open with { To = lastDay < open.From ? open.From : lastDay }];
        }
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
        Raise(new RateSuperseded(TenantId, Id, Reference, lastDay));
        if (lastDay < EffectiveTo)
        {
            EffectiveTo = lastDay < EffectiveFrom ? EffectiveFrom : lastDay;
        }
    }

    /// <summary>A new draft copy of this contract (header, terms, fuel clause and every rate) to edit and approve as the next revision.</summary>
    public Result<Contract> CreateRevision(DateOnly effectiveFrom, DateOnly effectiveTo, Guid? ownerUserId, RevisionKind kind = RevisionKind.Amendment)
    {
        if (Status is not (ContractStatus.Active or ContractStatus.Expired or ContractStatus.Suspended))
        {
            return Error.Conflict("contracts.not_revisable", "Only an active or expired contract can be revised.");
        }

        var revision = new Contract
        {
            TenantId = TenantId,
            Number = Number,
            Revision = Revision + 1,
            RevisionOfId = Id,
            RevisionKind = kind,
            TransporterId = TransporterId,
            Type = Type,
            Status = ContractStatus.Draft,
            CalculationVersion = CalculationVersion,
        };

        var set = revision.SetHeader(Title, effectiveFrom, effectiveTo, PaymentTermsDays, EstimatedAnnualSpend, ownerUserId ?? OwnerUserId, Terms, Fuel);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var extras = revision.ApplyExtras(ToExtras());
        if (extras.IsFailure)
        {
            return extras.Error;
        }

        foreach (var step in new Func<Result>[]
        {
            () => revision.ReplaceDphRules(_dphRules.Select(r => r.Spec).ToList()),
            () => revision.ReplaceRates(_rateCards.Select(c => c.ToSpec()).ToList(), _rateCards.ToDictionary(c => c.Code)),
            () => revision.ReplaceAccessorials(_accessorials.Select(a => a.Spec).ToList()),
            () => revision.ReplaceCapacities(_capacities.Select(c => c.Spec).ToList()),
            () => revision.ReplaceSlas(_slas.Select(c => c.Spec).ToList()),
        })
        {
            if (step() is { IsFailure: true } failed)
            {
                return failed.Error;
            }
        }

        return revision;
    }

    /// <summary>Has been approved at some point (so its rates were once binding), whatever happened to it afterwards.</summary>
    public bool WasApproved => Status is ContractStatus.Active or ContractStatus.Terminated or ContractStatus.Superseded or ContractStatus.Expired or ContractStatus.Suspended;

    /// <summary>
    /// Whether the contract's rates govern a shipment on <paramref name="date"/>. Deliberately independent of what
    /// happened later: a freight bill for last month's shipment must still be checked against the contract that was in
    /// force then, even if it has since expired, been terminated or been replaced by a revision.
    /// </summary>
    public bool IsInForce(DateOnly date) => WasApproved && EffectiveFrom <= date && date <= EffectiveTo && !Suspensions.Any(x => x.Covers(date));

    /// <summary>What to show people: an "Active" contract past its end date is effectively expired even before the nightly job runs.</summary>
    public ContractStatus EffectiveStatus(DateOnly today) =>
        Status == ContractStatus.Active && EffectiveTo < today ? ContractStatus.Expired : Status;

    public int? DaysUntilExpiry(DateOnly today) =>
        Status is ContractStatus.Active or ContractStatus.Suspended ? EffectiveTo.DayNumber - today.DayNumber : null;

    private void Activate(DateTimeOffset now)
    {
        Status = ContractStatus.Active;
        ActivatedAt = now;
        Raise(new ContractActivated(TenantId, Id, Reference, TransporterId, string.Join(",", EffectiveServices), EffectiveFrom, EffectiveTo, RateCount));
        Raise(new RateActivated(TenantId, Id, Reference, RateCount));
    }

    private Error? EnsureEditable() => Status switch
    {
        ContractStatus.Draft or ContractStatus.Rejected => null,
        ContractStatus.PendingApproval => Error.Conflict("contracts.locked", "This contract is awaiting approval and cannot be edited."),
        _ => Error.Conflict("contracts.immutable", "An approved contract cannot be edited. Create a revision instead."),
    };
}
