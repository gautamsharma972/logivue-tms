using System.Text.Json;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

/// <summary>One line of a stored rating, in the order shown.</summary>
[AuditIgnore]
public sealed class RatingComponent : Entity, ITenantScoped
{
    private RatingComponent()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid RatingId { get; private set; }

    public int Sequence { get; private set; }

    public string Type { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public decimal? Quantity { get; private set; }

    public string? Unit { get; private set; }

    public decimal? Rate { get; private set; }

    public decimal Amount { get; private set; }

    public string? Reference { get; private set; }

    internal static RatingComponent From(Guid tenantId, Guid ratingId, int sequence, RatingLine line) =>
        new()
        {
            TenantId = tenantId, RatingId = ratingId, Sequence = sequence, Type = line.Type, Description = line.Description.Length > 500 ? line.Description[..500] : line.Description,
            Quantity = line.Quantity, Unit = line.Unit, Rate = line.Rate, Amount = line.Amount, Reference = line.Reference,
        };
}

/// <summary>A contract or rate that was looked at and not used, with why: what makes a rating explainable to someone who expected a different answer.</summary>
[AuditIgnore]
public sealed class RatingExclusion : Entity, ITenantScoped
{
    private RatingExclusion()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid RatingId { get; private set; }

    public string ContractReference { get; private set; } = null!;

    public string? RateReference { get; private set; }

    public string ReasonCode { get; private set; } = null!;

    public string Reason { get; private set; } = null!;

    internal static RatingExclusion From(Guid tenantId, Guid ratingId, RatingExclusionInfo info) =>
        new()
        {
            TenantId = tenantId, RatingId = ratingId, ContractReference = info.ContractReference, RateReference = info.RateCode, ReasonCode = info.Code,
            Reason = info.Reason.Length > 500 ? info.Reason[..500] : info.Reason,
        };
}

/// <summary>
/// A rating that was kept: the freight a shipment is contractually owed. It records the request, the contract, rate and DPH versions used, every line and every
/// rejected alternative, and the version of the calculation, so it can be explained and reproduced years later whatever has happened to the contracts since.
/// Nothing about the result can be edited; a commercial override sits beside it and never replaces it.
/// </summary>
public sealed class FreightRating : AggregateRoot, ITenantScoped
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly List<RatingComponent> _components = [];
    private readonly List<RatingExclusion> _exclusions = [];

    private FreightRating()
    {
    }

    public Guid TenantId { get; private set; }

    public string Reference { get; private set; } = null!;

    public string? ShipmentReference { get; private set; }

    /// <summary>True when the rating was asked to be kept as the shipment's freight; false for a failed attempt kept so uncovered loads can be counted.</summary>
    public bool Committed { get; private set; }

    public bool Qualified { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? Message { get; private set; }

    public Guid? TransporterId { get; private set; }

    public Guid? ContractId { get; private set; }

    public string? ContractReference { get; private set; }

    public int? ContractRevision { get; private set; }

    public Guid? RateCardId { get; private set; }

    public string? RateCode { get; private set; }

    public int? RateVersion { get; private set; }

    public Guid? DphRuleId { get; private set; }

    public string? DphRuleCode { get; private set; }

    public int? DphVersion { get; private set; }

    public string Lane { get; private set; } = null!;

    public ContractType Service { get; private set; }

    public Guid? VehicleTypeId { get; private set; }

    public decimal? WeightKg { get; private set; }

    public decimal? VolumeCbm { get; private set; }

    public decimal? DistanceKm { get; private set; }

    public int StopCount { get; private set; }

    public DateOnly ShipmentDate { get; private set; }

    public decimal BaseFreight { get; private set; }

    public decimal DphAdjustment { get; private set; }

    public decimal AccessorialAmount { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public decimal TotalFreight { get; private set; }

    public string Currency { get; private set; } = "INR";

    public string CalculationVersion { get; private set; } = null!;

    public DateTimeOffset CalculatedAt { get; private set; }

    /// <summary>The request as made (JSON), enough to rate again.</summary>
    public string InputJson { get; private set; } = null!;

    /// <summary>The steps the engine took, for the calculation trace.</summary>
    public string TraceJson { get; private set; } = "[]";

    public string ReasonsJson { get; private set; } = "[]";

    public decimal? OverrideAmount { get; private set; }

    public string? OverrideReason { get; private set; }

    public string? OverrideApprovedBy { get; private set; }

    public Guid? OverriddenBy { get; private set; }

    public DateTimeOffset? OverriddenAt { get; private set; }

    public IReadOnlyCollection<RatingComponent> Components => _components;

    public IReadOnlyCollection<RatingExclusion> Exclusions => _exclusions;

    /// <summary>What the shipment is owed: the override when there is one, otherwise the calculated freight.</summary>
    public decimal EffectiveFreight => OverrideAmount ?? TotalFreight;

    public static FreightRating Create(Guid tenantId, string reference, RatingInput input, RatingOutcome outcome, bool committed, DateTimeOffset now)
    {
        var best = outcome.Selected;
        var rating = new FreightRating
        {
            TenantId = tenantId,
            Reference = reference,
            ShipmentReference = input.ShipmentReference,
            Committed = committed,
            Qualified = outcome.Qualified,
            ErrorCode = outcome.ErrorCode,
            Message = outcome.Message,
            TransporterId = best?.Contract.TransporterId ?? input.TransporterId,
            ContractId = best?.Contract.Id,
            ContractReference = best?.Contract.Number,
            ContractRevision = best?.Contract.Revision,
            RateCardId = best?.Card.Id,
            RateCode = best?.Card.Code,
            RateVersion = best?.Card.Version,
            DphRuleId = best?.DphRule?.Id,
            DphRuleCode = best?.DphRule?.Code,
            DphVersion = best?.DphRule?.Version,
            Lane = $"{input.Origin.City ?? input.Origin.State} → {input.Destination.City ?? input.Destination.State}",
            Service = input.Service,
            VehicleTypeId = input.VehicleTypeId,
            WeightKg = input.WeightKg,
            VolumeCbm = input.VolumeCbm,
            DistanceKm = input.DistanceKm,
            StopCount = input.StopCount,
            ShipmentDate = input.Date,
            BaseFreight = best?.BaseFreight ?? 0,
            DphAdjustment = best?.Dph ?? 0,
            AccessorialAmount = best?.Accessorials ?? 0,
            DiscountAmount = best?.Discount ?? 0,
            TotalFreight = best?.Total ?? 0,
            Currency = best?.Currency ?? "INR",
            CalculationVersion = RatingEngine.Version,
            CalculatedAt = now,
            InputJson = JsonSerializer.Serialize(input, Json),
            TraceJson = JsonSerializer.Serialize(outcome.Trace, Json),
            ReasonsJson = JsonSerializer.Serialize(best?.Reasons ?? [], Json),
        };

        var sequence = 0;
        foreach (var line in best?.Lines ?? [])
        {
            rating._components.Add(RatingComponent.From(tenantId, rating.Id, ++sequence, line));
        }

        foreach (var info in outcome.Exclusions.Take(500))
        {
            rating._exclusions.Add(RatingExclusion.From(tenantId, rating.Id, info));
        }

        if (committed || !outcome.Qualified)
        {
            if (outcome.Qualified)
            {
                rating.Raise(new RatingCalculated(tenantId, rating.Id, reference, input.ShipmentReference, rating.ContractReference!, rating.TotalFreight, rating.Currency, rating.CalculationVersion));
            }
            else if (outcome.ErrorCode == "FREIGHT_RATE_CONFLICT")
            {
                rating.Raise(new RateConflictDetected(tenantId, rating.Id, reference, input.ShipmentReference, outcome.Message ?? "Rate conflict"));
            }
            else
            {
                rating.Raise(new RatingFailed(tenantId, rating.Id, reference, input.ShipmentReference, outcome.ErrorCode ?? "FREIGHT_RATE_NOT_FOUND", outcome.Message ?? "No rate"));
            }
        }

        return rating;
    }

    /// <summary>Tells other modules that a DPH period's diesel price was fixed by this rating, so the adjustment can always be reproduced.</summary>
    public void AnnounceDphRevision(Guid contractId, string contractReference, string ruleCode, DateOnly periodStart, decimal referencePrice, decimal adjustmentPercent) =>
        Raise(new DphRevisionApplied(TenantId, contractId, contractReference, ruleCode, periodStart, referencePrice, adjustmentPercent));

    public RatingInput? ReadInput() => JsonSerializer.Deserialize<RatingInput>(InputJson, Json);

    public IReadOnlyList<TraceStep> ReadTrace() => JsonSerializer.Deserialize<List<TraceStep>>(TraceJson, Json) ?? [];

    public IReadOnlyList<string> ReadReasons() => JsonSerializer.Deserialize<List<string>>(ReasonsJson, Json) ?? [];

    /// <summary>
    /// Records that the commercial team agreed a different freight. The calculated freight stays exactly as it was, and who agreed, why and when are kept beside it.
    /// </summary>
    public Result ApplyOverride(decimal amount, string reason, string? approvedBy, Guid? userId, DateTimeOffset now)
    {
        if (!Qualified || !Committed)
        {
            return Error.Conflict("rating.not_overridable", "Only a kept rating that found a rate can be overridden.");
        }

        if (amount <= 0 || amount > 1_000_000_000m)
        {
            return Error.Validation("rating.override_invalid", "Enter the agreed freight amount.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
        {
            return Error.Validation("rating.override_reason", "Say why the freight differs from the contract (up to 500 characters).");
        }

        OverrideAmount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        OverrideReason = reason.Trim();
        OverrideApprovedBy = string.IsNullOrWhiteSpace(approvedBy) ? null : approvedBy.Trim();
        OverriddenBy = userId;
        OverriddenAt = now;
        return Result.Success();
    }

    public Result ClearOverride()
    {
        if (OverrideAmount is null)
        {
            return Error.Conflict("rating.no_override", "There is no override to remove.");
        }

        OverrideAmount = null;
        OverrideReason = null;
        OverrideApprovedBy = null;
        OverriddenBy = null;
        OverriddenAt = null;
        return Result.Success();
    }
}
