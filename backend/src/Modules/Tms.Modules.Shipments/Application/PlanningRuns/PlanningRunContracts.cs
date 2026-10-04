using FluentValidation;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Application.PlanningRuns;

/// <summary>
/// The planner's decision on the full-truck / part-load comparison. Accepting follows the recommendation; overriding takes another
/// mode and needs a reason, which is kept with the plan.
/// </summary>
/// <param name="Mode">The mode to plan with: the other mode is switched off for this run.</param>
/// <param name="Override">True when this differs from the recommendation.</param>
public sealed record ModeChoice(FreightMode Mode, bool Override, string? Reason = null);

/// <param name="Background">Calculate in the background: the run is returned at once as Running and finishes later.</param>
public sealed record CreateRunRequest(DateOnly PlanningDate, IReadOnlyList<Guid> OrderIds, PlanOptions? Options, bool Background = false, ModeChoice? ModeChoice = null);

public sealed record ReoptimizeRequest(string Reason, PlanOptions? Options);

public enum PlanEditKind
{
    MoveOrder = 1,
    RemoveOrder = 2,
    AddOrder = 3,
    ReorderStops = 4,
    ChangeVehicleType = 5,
}

/// <summary>One manual change to a plan. Which fields are needed depends on <see cref="Kind"/>.</summary>
public sealed record EditPlanRequest(
    PlanEditKind Kind,
    Guid? OrderId = null,
    Guid? VehicleKey = null,
    Guid? ToVehicleKey = null,
    IReadOnlyList<Guid>? OrderIds = null,
    Guid? VehicleTypeId = null,
    string? Comment = null);

/// <param name="Kind">What to lock; the whole vehicle when omitted.</param>
/// <param name="OrderId">With <see cref="LockKind.Order"/>: the order to keep on its vehicle.</param>
public sealed record LockRequest(Guid VehicleKey, bool Locked, LockKind Kind = LockKind.Vehicle, Guid? OrderId = null);

public sealed record CompareRequest(IReadOnlyList<Guid> OrderIds, DateOnly? Date, PlanOptions? Options);

public sealed record ListRunsQuery(PlanStatus? Status = null, int Page = 1, int PageSize = 25);

public sealed record RunDto(
    Guid Id,
    Guid RunGroupId,
    string Number,
    int PlanVersion,
    bool IsLatest,
    DateOnly PlanningDate,
    PlanStatus Status,
    PlanOptions Options,
    IReadOnlyList<Guid> OrderIds,
    string? Reason,
    PlanSnapshot Plan,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? CommittedAt,
    string? CancelReason,
    long Version,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    IReadOnlyList<PlanLogEntry>? Log = null);

public sealed record RunSummaryDto(
    Guid Id, string Number, int PlanVersion, DateOnly PlanningDate, PlanStatus Status, SolverStatus SolverStatus, PlanSummary Summary, DateTimeOffset CreatedAt);

public sealed record VersionDto(Guid Id, int PlanVersion, PlanStatus Status, string? Reason, DateTimeOffset CreatedAt, PlanSummary Summary);

public sealed record ComparisonDto(
    IReadOnlyList<PlanAlternative> Alternatives, PlanAlternative? Recommended, string Reason, IReadOnlyList<VehicleTypeEvaluation> Sizing,
    decimal? FtlTotal, decimal? PtlTotal, decimal? Saving, FreightMode? RecommendedMode);

public sealed record RecommendationQuery(decimal WeightKg, decimal? VolumeCbm);

internal static class OptionRules
{
    /// <summary>Server-side limits: the client's validation is advisory only.</summary>
    public static string? Check(PlanOptions o) =>
        !Enum.IsDefined(o.Objective) ? "Choose a valid objective."
        : !o.AllowFtl && !o.AllowPtl ? "Allow at least one of full truck or part load."
        : o.MaxStops is < 1 or > 50 ? "Maximum stops must be between 1 and 50."
        : o.TimeBudgetSeconds is < 1 or > 120 ? "The time limit must be between 1 and 120 seconds."
        : o.PtlExtraTransitHours is < 0 or > 240 ? "Extra part-load transit must be between 0 and 240 hours."
        : o.StopServiceMinutes is < 0 or > 480 ? "Time at each stop must be between 0 and 480 minutes."
        : o.DepartureHour is < 0 or > 23 ? "Departure hour must be between 0 and 23."
        : o.BackhaulChargePercent is < 0 or > 100 ? "The return-load charge must be between 0 and 100 percent."
        : o.MaxBackhaulExtraKm is < 0 or > 1000 ? "The maximum return detour must be between 0 and 1000 km."
        : o.MaxPtlWeightKg is < 100 or > 50_000 ? "The heaviest shipment allowed as part load must be between 100 and 50,000 kg."
        : null;
}

internal sealed class CreateRunRequestValidator : AbstractValidator<CreateRunRequest>
{
    public CreateRunRequestValidator()
    {
        RuleFor(x => x.OrderIds).NotNull().Must(ids => ids is { Count: > 0 and <= 500 }).WithMessage("Choose between 1 and 500 orders.");
        RuleFor(x => x.Options).Must(o => o is null || OptionRules.Check(o) is null).WithMessage("The planning options are not valid.");
        When(x => x.ModeChoice is not null, () =>
        {
            RuleFor(x => x.ModeChoice!.Mode).IsInEnum();
            RuleFor(x => x.ModeChoice!.Reason).NotEmpty().WithMessage("Say why you are overriding the recommendation.").When(x => x.ModeChoice!.Override);
            RuleFor(x => x.ModeChoice!.Reason).MaximumLength(300);
        });
    }
}

internal sealed class ReoptimizeRequestValidator : AbstractValidator<ReoptimizeRequest>
{
    public ReoptimizeRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Options).Must(o => o is null || OptionRules.Check(o) is null).WithMessage("The planning options are not valid.");
    }
}

internal sealed class CompareRequestValidator : AbstractValidator<CompareRequest>
{
    public CompareRequestValidator() =>
        RuleFor(x => x.OrderIds).NotNull().Must(ids => ids is { Count: > 0 and <= 100 }).WithMessage("Choose between 1 and 100 orders.");
}

internal sealed class EditPlanRequestValidator : AbstractValidator<EditPlanRequest>
{
    public EditPlanRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Comment).MaximumLength(300);

        // Moving, adding, removing and re-typing are overrides of what the system recommended: they must be explained for the record.
        RuleFor(x => x.Comment).NotEmpty().WithMessage("Say why you are overriding the plan.").When(x => x.Kind != PlanEditKind.ReorderStops);
    }
}
