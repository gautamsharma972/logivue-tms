using FluentValidation;
using Tms.Modules.Contracts.Domain;

namespace Tms.Modules.Contracts.Application;

// ---- rating

/// <param name="ShipmentDate">Defaults to today (India).</param>
/// <param name="Commit">Keep the result as the shipment's freight. Needs a <paramref name="ShipmentReference"/>.</param>
/// <param name="ContractId">Rate against one contract only; with <paramref name="Preview"/> it works on drafts too.</param>
/// <param name="UseActuals">Fill the accessorial quantities from what happened on the shipment, where something can say.</param>
public sealed record RatingRequestDto(
    DateOnly? ShipmentDate,
    Location Origin,
    Location Destination,
    ContractType Service,
    Guid? TransporterId = null,
    Guid? VehicleTypeId = null,
    decimal? WeightKg = null,
    decimal? VolumeCbm = null,
    decimal? DistanceKm = null,
    int StopCount = 1,
    IReadOnlyList<string>? RequiredCapabilities = null,
    IReadOnlyDictionary<string, decimal>? AccessorialInputs = null,
    string? ShipmentReference = null,
    bool Commit = false,
    Guid? ContractId = null,
    bool Preview = false,
    bool UseActuals = false);

public sealed record RatingLineDto(int Sequence, string Type, string Description, decimal? Quantity, string? Unit, decimal? Rate, decimal Amount, string? Reference);

public sealed record RatingExclusionDto(string ContractReference, string? RateReference, string ReasonCode, string Reason);

public sealed record TraceStepDto(string Stage, string Text, bool Ok);

public sealed record RatingRateDto(Guid Id, string Code, int Version, string Lane, int Priority, string Service);

public sealed record RatingOptionDto(
    Guid ContractId,
    string ContractReference,
    int ContractRevision,
    Guid TransporterId,
    string TransporterName,
    RatingRateDto Rate,
    string? DphRule,
    int? DphVersion,
    decimal BaseFreight,
    decimal DphAdjustment,
    decimal AccessorialAmount,
    decimal DiscountAmount,
    decimal TotalFreight,
    string Currency,
    int? TransitSlaMinutes,
    DateOnly ContractValidFrom,
    DateOnly ContractValidTo,
    IReadOnlyList<RatingLineDto> Lines,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Notes,
    decimal? ChargeableWeightKg);

public sealed record RatingResultDto(
    bool Qualified,
    string? ErrorCode,
    string? Message,
    IReadOnlyList<string> Advice,
    Guid? RatingId,
    string? RatingReference,
    bool Committed,
    string CalculationVersion,
    DateOnly ShipmentDate,
    RatingOptionDto? Selected,
    IReadOnlyList<RatingOptionDto> Options,
    IReadOnlyList<RatingExclusionDto> Exclusions,
    IReadOnlyList<TraceStepDto> Trace);

public sealed record RatingSummaryDto(
    Guid Id,
    string Reference,
    string? ShipmentReference,
    bool Committed,
    bool Qualified,
    string? ErrorCode,
    string Lane,
    ContractType Service,
    DateOnly ShipmentDate,
    Guid? TransporterId,
    string? TransporterName,
    string? ContractReference,
    int? ContractRevision,
    string? RateCode,
    int? RateVersion,
    decimal TotalFreight,
    decimal? OverrideAmount,
    string Currency,
    string CalculationVersion,
    DateTimeOffset CalculatedAt);

public sealed record RatingDetailDto(
    RatingSummaryDto Summary,
    RatingResultDto Result,
    RatingRequestDto? Request,
    decimal? OverrideAmount,
    string? OverrideReason,
    string? OverrideApprovedBy,
    DateTimeOffset? OverriddenAt,
    string? DphRule,
    int? DphVersion);

public sealed record ListRatingsQuery(
    string? ShipmentReference = null, Guid? TransporterId = null, Guid? ContractId = null, bool? Qualified = null, bool? Committed = null, DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 25);

public sealed record OverrideRatingRequest(decimal Amount, string Reason, string? ApprovedBy);

public sealed record ReproduceDto(
    bool Matches,
    bool Recalculated,
    string StoredCalculationVersion,
    string EngineVersion,
    decimal StoredTotal,
    decimal? RecalculatedTotal,
    IReadOnlyList<string> Differences,
    string Message);

/// <summary>One change to ask "what if" about: anything left out stays as in the base request.</summary>
public sealed record WhatIfVariation(string Label, decimal? WeightKg = null, decimal? VolumeCbm = null, decimal? DistanceKm = null, Guid? VehicleTypeId = null, int? StopCount = null, DateOnly? ShipmentDate = null, Guid? TransporterId = null);

public sealed record WhatIfRequest(RatingRequestDto Base, IReadOnlyList<WhatIfVariation> Variations);

public sealed record WhatIfRowDto(string Label, bool Qualified, decimal? TotalFreight, decimal? DifferenceFromBase, string? Message, RatingOptionDto? Selected);

public sealed record WhatIfResultDto(WhatIfRowDto Base, IReadOnlyList<WhatIfRowDto> Variations);

public sealed record CompareRequest(RatingRequestDto Request, IReadOnlyList<Guid> TransporterIds);

public sealed record CompareRowDto(Guid TransporterId, string TransporterName, bool Qualified, string? Message, RatingOptionDto? Option);

// ---- rates

public sealed record RateRowDto(
    Guid Id,
    Guid ContractId,
    string ContractNumber,
    int ContractRevision,
    ContractStatus ContractStatus,
    Guid TransporterId,
    string TransporterName,
    string Code,
    int Version,
    string Service,
    string Lane,
    PlaceDto Origin,
    PlaceDto Destination,
    Guid? VehicleTypeId,
    string? VehicleTypeName,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    decimal? MinDistanceKm,
    decimal? MaxDistanceKm,
    decimal? MinVolumeCbm,
    decimal? MaxVolumeCbm,
    string PricingKind,
    string RateSummary,
    decimal? MinimumCharge,
    decimal? MaximumCharge,
    string? DphRuleCode,
    int Priority,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    bool InForce,
    bool Expiring);

public sealed record ListRatesQuery(
    string? Search = null, Guid? TransporterId = null, Guid? ContractId = null, ContractType? Service = null, ContractStatus? Status = null, Guid? VehicleTypeId = null,
    string? Origin = null, string? Destination = null, bool? InForceOnly = null, int? ExpiringWithinDays = null, string? SortBy = null, bool Descending = false, int Page = 1, int PageSize = 50);

/// <param name="Rows">The rates to check. When <paramref name="ContractId"/> is given the contract's dates, services and DPH rules apply and its other rates are not compared (these rows are what would replace them).</param>
public sealed record ValidateRatesRequest(Guid? ContractId, IReadOnlyList<RateInputDto> Rows, DateOnly? ContractFrom = null, DateOnly? ContractTo = null, IReadOnlyList<ContractType>? Services = null, IReadOnlyList<string>? DphCodes = null);

public sealed record RateIssueDto(string Severity, int? Row, string Field, string Code, string Message);

public sealed record RateValidationDto(string Outcome, int Errors, int Warnings, IReadOnlyList<RateIssueDto> Issues);

public sealed record CreateRateRequest(Guid ContractId, RateInputDto Rate, long ContractVersion);

public sealed record CreateRateVersionRequest(RateInputDto Rate, DateOnly? EffectiveFrom, DateOnly? EffectiveTo);

// ---- DPH

public sealed record DphRuleDto(Guid Id, Guid ContractId, string ContractNumber, int ContractRevision, string Code, int Version, DphRuleSpec Spec, DateOnly EffectiveFrom, DateOnly EffectiveTo, bool InForce);

public sealed record SaveDphRulesRequest(IReadOnlyList<DphRuleSpec> Rules, long Version);

public sealed record SaveDphRuleRequest(Guid ContractId, DphRuleSpec Rule, long Version);

/// <param name="Date">The shipment date to calculate for (default today).</param>
/// <param name="BaseFreight">The freight the adjustment is worked out on.</param>
/// <param name="Record">Keep the period's diesel price so this adjustment can be reproduced later.</param>
public sealed record DphCalculateRequest(DateOnly? Date, decimal BaseFreight, decimal? DistanceKm, bool Record = false, decimal? OverridePrice = null);

public sealed record DphCalculationDto(
    Guid RuleId, string RuleCode, int RuleVersion, DateOnly ReferenceDate, decimal? DieselPrice, decimal BaseDieselPrice, decimal VariationPercent, decimal AdjustmentPercent, decimal Amount,
    bool Applied, string Explanation, bool Recorded);

public sealed record DphOverviewDto(
    DphRuleDto Rule, decimal? CurrentPrice, decimal? VariationPercent, decimal? AdjustmentPercent, DateOnly? PriceDate, bool RevisionDue);

public sealed record PriceIndexDto(Guid Id, string Region, DateOnly ReferenceDate, decimal Price, string Currency, string Unit, string? Source);

public sealed record AddPriceIndexRequest(string Region, DateOnly ReferenceDate, decimal Price, string? Source);

// ---- accessorials, capacity, SLA

public sealed record AccessorialTypeDto(Guid Id, string Code, string Name, string? Description, AccessorialCalc Calc, string Unit, bool IsActive);

public sealed record SaveAccessorialTypeRequest(string Code, string Name, string? Description, AccessorialCalc Calc, string Unit, bool IsActive = true);

public sealed record SaveAccessorialsRequest(IReadOnlyList<AccessorialSpec> Charges, long Version);

public sealed record SaveCapacityRequest(IReadOnlyList<CapacitySpec> Commitments, long Version);

public sealed record SaveSlaRequest(IReadOnlyList<SlaSpec> Levels, long Version);

public sealed record CapacityDto(Guid Id, CapacitySpec Spec, string? VehicleTypeName);

// ---- import

public sealed record ImportRowDto(int RowNumber, IReadOnlyDictionary<string, string?> Values, string Status, IReadOnlyList<RateIssueDto> Issues);

public sealed record ImportBatchDto(
    Guid Id, string Reference, string FileName, ImportMode Mode, ImportStatus Status, string? ContractNumber, Guid? ContractId, Guid? AppliedContractId, int RowCount, int ErrorRows, int WarningRows,
    DateTimeOffset CreatedAt, DateTimeOffset? AppliedAt, IReadOnlyList<ImportRowDto>? Rows = null);

public sealed record CorrectImportRowRequest(IReadOnlyDictionary<string, string?> Values);

public sealed record ApplyImportRequest(bool SkipInvalidRows = false, DateOnly? EffectiveFrom = null, DateOnly? EffectiveTo = null);

// ---- analytics

public sealed record ContractDashboardDto(
    int TotalContracts, int Active, int Draft, int PendingApproval, int ExpiringSoon, int Expired, int Suspended,
    int ActiveRates, int RatesExpiring, int DphRules, int DphRevisionsDue, int UncoveredLanes, int ValidationErrors, int FailedRatings30Days, DateTimeOffset AsOf);

public sealed record ExpiryItemDto(string Kind, string Reference, string Title, Guid ContractId, string ContractNumber, DateOnly ExpiresOn, int DaysLeft, string Band);

public sealed record ExpiryDto(IReadOnlyList<int> Bands, IReadOnlyList<ExpiryItemDto> Items);

public sealed record LaneCoverageDto(string Lane, string Service, string State, int Requests, int Failed, string? Reason);

public sealed record RateCoverageDto(
    int RequiredLanes, int CoveredLanes, int UncoveredLanes, int FallbackCovered, int ActiveContracts, int ActiveRates, int RatesExpiring, int DuplicateRates, int OverlappingRates,
    int LoadsWithoutRate, IReadOnlyList<LaneCoverageDto> Uncovered);

public sealed record ValidationOverviewDto(int ContractsChecked, int Errors, int Warnings, IReadOnlyList<ValidationContractDto> Contracts);

public sealed record ValidationContractDto(Guid ContractId, string Reference, ContractStatus Status, int Errors, int Warnings, IReadOnlyList<RateIssueDto> Issues);

public sealed record RateUsageDto(Guid ContractId, string ContractNumber, int ContractRevision, string RateCode, int RateVersion, string Lane, int Shipments, decimal TotalFreight, decimal AverageFreight);

public sealed record ImpactRowDto(string RateCode, string Lane, decimal CurrentRate, decimal ProposedRate, decimal ChangePercent, int Shipments, decimal CurrentFreight, decimal ProposedFreight, decimal Impact);

public sealed record ImpactDto(
    Guid CurrentContractId, string CurrentReference, Guid ProposedContractId, string ProposedReference, int ShipmentsConsidered, int Months, decimal CurrentSpend, decimal ProposedSpend, decimal Variance,
    decimal VariancePercent, decimal AnnualisedVariance, int RatesChanged, int RatesNotRated, IReadOnlyList<ImpactRowDto> Rows, string Basis);

public sealed record ReportsListDto(IReadOnlyList<string> Reports);

internal sealed class RatingRequestValidator : AbstractValidator<RatingRequestDto>
{
    public RatingRequestValidator()
    {
        RuleFor(x => x.Origin).NotNull();
        RuleFor(x => x.Destination).NotNull();
        RuleFor(x => x.Service).IsInEnum();
        RuleFor(x => x.StopCount).InclusiveBetween(1, 100);
        RuleFor(x => x.WeightKg).GreaterThanOrEqualTo(0).When(x => x.WeightKg is not null);
        RuleFor(x => x.VolumeCbm).GreaterThanOrEqualTo(0).When(x => x.VolumeCbm is not null);
        RuleFor(x => x.DistanceKm).GreaterThanOrEqualTo(0).When(x => x.DistanceKm is not null);
        RuleFor(x => x.ShipmentReference).MaximumLength(64);
        RuleFor(x => x).Must(x => !x.Commit || !string.IsNullOrWhiteSpace(x.ShipmentReference)).WithMessage("Say which shipment the rating is kept for.").OverridePropertyName("shipmentReference");
    }
}

internal sealed class OverrideRatingRequestValidator : AbstractValidator<OverrideRatingRequest>
{
    public OverrideRatingRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ApprovedBy).MaximumLength(200);
    }
}

internal sealed class WhatIfRequestValidator : AbstractValidator<WhatIfRequest>
{
    public WhatIfRequestValidator()
    {
        RuleFor(x => x.Base).NotNull().SetValidator(new RatingRequestValidator());
        RuleFor(x => x.Variations).NotNull().Must(v => v is { Count: > 0 and <= 20 }).WithMessage("Give between 1 and 20 variations.");
    }
}

internal sealed class CompareRequestValidator : AbstractValidator<CompareRequest>
{
    public CompareRequestValidator()
    {
        RuleFor(x => x.Request).NotNull().SetValidator(new RatingRequestValidator());
        RuleFor(x => x.TransporterIds).NotNull().Must(v => v is { Count: > 0 and <= 20 }).WithMessage("Choose between 1 and 20 transporters.");
    }
}

internal sealed class ContractReasonRequestValidator : AbstractValidator<ContractReasonRequest>
{
    public ContractReasonRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class SaveAccessorialTypeRequestValidator : AbstractValidator<SaveAccessorialTypeRequest>
{
    public SaveAccessorialTypeRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Calc).IsInEnum();
    }
}

internal sealed class AddPriceIndexRequestValidator : AbstractValidator<AddPriceIndexRequest>
{
    public AddPriceIndexRequestValidator()
    {
        RuleFor(x => x.Region).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(500);
        RuleFor(x => x.Source).MaximumLength(100);
    }
}
