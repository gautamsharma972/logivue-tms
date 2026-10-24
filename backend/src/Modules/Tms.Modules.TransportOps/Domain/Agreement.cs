using Tms.SharedKernel.Domain;

namespace Tms.Modules.TransportOps.Domain;

/// <summary>One weight band of a slab rate: above <see cref="FromKg"/> and up to <see cref="ToKg"/> (open when null), at <see cref="RatePerKg"/>.</summary>
public sealed record WeightSlab(decimal FromKg, decimal? ToKg, decimal RatePerKg);

/// <summary>A fixed extra charge a customer has agreed (for example unloading), added to each consignment.</summary>
public sealed record ExtraCharge(string Code, string Name, decimal Amount);

/// <summary>
/// The customer's detention rule. Free time is either a duration after arrival, or until the end of the arrival day (IST) plus whole free days. Beyond it, each started (or
/// rounded) unit is charged at <see cref="Rate"/>, up to <see cref="MaxCharge"/>.
/// </summary>
public sealed record DetentionPolicy(
    bool Applicable,
    FreeTimeKind FreeTime,
    decimal FreeAmount,
    DetentionUnit FreeUnit,
    DetentionUnit ChargeUnit,
    decimal Rate,
    PartUnitRounding Rounding,
    decimal? MaxCharge,
    string? Note)
{
    public static DetentionPolicy None { get; } = new(false, FreeTimeKind.Duration, 0, DetentionUnit.Hour, DetentionUnit.Hour, 0, PartUnitRounding.RoundUp, null, null);
}

/// <summary>Tax on the invoice. Nothing is assumed: the rate and SAC come from the customer's agreement.</summary>
public sealed record TaxSettings(TaxMode Mode, decimal RatePercent, string? Sac, string? Note)
{
    public static TaxSettings None { get; } = new(TaxMode.None, 0, null, null);
}

/// <summary>When a delivery may be billed. A POD is normally required; billing without one has to be agreed, and is recorded when used.</summary>
public sealed record BillingRules(bool RequireAcceptedPod, bool AllowBillingWithoutPod, string? RequiredDocuments)
{
    public static BillingRules Standard { get; } = new(true, false, null);
}

/// <summary>One lane/material rate on an agreement. Origin and destination can each be a specific location, a city, a state or anywhere.</summary>
public sealed class AgreementRate : Entity, ITenantScoped
{
    private AgreementRate()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid AgreementId { get; private set; }

    public Guid? OriginLocationId { get; private set; }

    public string? OriginCity { get; private set; }

    public string? OriginState { get; private set; }

    public Guid? DestinationLocationId { get; private set; }

    public string? DestinationCity { get; private set; }

    public string? DestinationState { get; private set; }

    public string? Material { get; private set; }

    public RateMethod Method { get; private set; }

    /// <summary>Per kg, per tonne, or the flat amount per consignment (unused for slabs).</summary>
    public decimal Rate { get; private set; }

    public List<WeightSlab> Slabs { get; private set; } = [];

    /// <summary>Smallest freight for this rate, applied after the rate is worked out.</summary>
    public decimal? MinimumCharge { get; private set; }

    public static AgreementRate Create(Guid tenantId, Guid agreementId, Guid? originLocationId, string? originCity, string? originState, Guid? destinationLocationId, string? destinationCity,
        string? destinationState, string? material, RateMethod method, decimal rate, IEnumerable<WeightSlab>? slabs, decimal? minimumCharge) => new()
    {
        TenantId = tenantId, AgreementId = agreementId, OriginLocationId = originLocationId, OriginCity = Norm(originCity), OriginState = Norm(originState),
        DestinationLocationId = destinationLocationId, DestinationCity = Norm(destinationCity), DestinationState = Norm(destinationState), Material = Norm(material),
        Method = method, Rate = rate, Slabs = slabs?.OrderBy(s => s.FromKg).ToList() ?? [], MinimumCharge = minimumCharge,
    };

    public static string? Norm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

/// <summary>
/// The commercial terms agreed with one customer for a period: freight rates, the weight basis, detention and tax. An active agreement is never edited; a change is a new
/// revision, so a delivery is always priced with the terms that were in force on its date and an old calculation can be explained.
/// </summary>
public sealed class CustomerAgreement : AggregateRoot, ITenantScoped
{
    private CustomerAgreement()
    {
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; } = null!;

    public int Revision { get; private set; } = 1;

    public Guid? RevisionOfId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string Title { get; private set; } = null!;

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public AgreementStatus Status { get; private set; } = AgreementStatus.Draft;

    public WeightBasis WeightBasis { get; private set; } = WeightBasis.Actual;

    /// <summary>Kilograms per cubic metre when volumetric weight applies.</summary>
    public decimal? VolumetricKgPerCbm { get; private set; }

    /// <summary>Smallest total freight per consignment.</summary>
    public decimal? MinimumFreight { get; private set; }

    public DetentionPolicy Detention { get; private set; } = DetentionPolicy.None;

    public TaxSettings Tax { get; private set; } = TaxSettings.None;

    public BillingRules Billing { get; private set; } = BillingRules.Standard;

    public List<ExtraCharge> ExtraCharges { get; private set; } = [];

    public List<AgreementRate> Rates { get; private set; } = [];

    public DateTimeOffset? ActivatedAt { get; private set; }

    public Guid? ActivatedBy { get; private set; }

    public static CustomerAgreement Create(Guid tenantId, string number, Guid customerId, string title, DateOnly effectiveFrom, DateOnly? effectiveTo) =>
        new() { TenantId = tenantId, Number = number, CustomerId = customerId, Title = title.Trim(), EffectiveFrom = effectiveFrom, EffectiveTo = effectiveTo };

    public bool IsEditable => Status == AgreementStatus.Draft;

    /// <summary>In force on a business date. Judged by dates and whether it was ever activated, so a superseded revision still prices the deliveries of its own period.</summary>
    public bool IsInForce(DateOnly date) =>
        (Status is AgreementStatus.Active or AgreementStatus.Superseded) && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);

    public void Edit(string title, DateOnly effectiveFrom, DateOnly? effectiveTo, WeightBasis basis, decimal? volumetricKgPerCbm, decimal? minimumFreight, DetentionPolicy detention, TaxSettings tax,
        BillingRules billing, IEnumerable<ExtraCharge> extras)
    {
        Title = title.Trim();
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        WeightBasis = basis;
        VolumetricKgPerCbm = volumetricKgPerCbm;
        MinimumFreight = minimumFreight;
        Detention = detention;
        Tax = tax;
        Billing = billing;
        ExtraCharges = extras.ToList();
    }

    public void SetRates(IEnumerable<AgreementRate> rates)
    {
        Rates.Clear();
        Rates.AddRange(rates);
    }

    public void Activate(Guid? by, DateTimeOffset at)
    {
        Status = AgreementStatus.Active;
        ActivatedAt = at;
        ActivatedBy = by;
    }

    /// <summary>A newer revision took over: this one ends the day before it starts (when it would otherwise overlap).</summary>
    public void SupersedeBy(DateOnly newFrom)
    {
        Status = AgreementStatus.Superseded;
        var last = newFrom.AddDays(-1);
        if (EffectiveTo is null || EffectiveTo > last)
        {
            EffectiveTo = last;
        }
    }

    public void Cancel() => Status = AgreementStatus.Cancelled;

    /// <summary>A draft copy for a change. The terms and rates are copied; the caller gives the new revision its effective date and number (cancelled revisions keep theirs).</summary>
    public CustomerAgreement CreateRevision(DateOnly effectiveFrom, int revision)
    {
        var copy = new CustomerAgreement
        {
            TenantId = TenantId, Number = Number, Revision = revision, RevisionOfId = Id, CustomerId = CustomerId, Title = Title, EffectiveFrom = effectiveFrom, EffectiveTo = null,
            WeightBasis = WeightBasis, VolumetricKgPerCbm = VolumetricKgPerCbm, MinimumFreight = MinimumFreight, Detention = Detention, Tax = Tax, Billing = Billing, ExtraCharges = [.. ExtraCharges],
        };
        copy.Rates.AddRange(Rates.Select(r => AgreementRate.Create(TenantId, copy.Id, r.OriginLocationId, r.OriginCity, r.OriginState, r.DestinationLocationId, r.DestinationCity, r.DestinationState,
            r.Material, r.Method, r.Rate, r.Slabs, r.MinimumCharge)));
        return copy;
    }
}
