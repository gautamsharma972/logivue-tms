using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;
using Tms.Modules.Contracts.Infrastructure.Persistence;

namespace Tms.Modules.Contracts.Application.Rating;

public enum RunMode
{
    /// <summary>Rate the shipment. A rating is kept only when asked to (<c>Commit</c>) or when no rate was found, so uncovered loads can be counted.</summary>
    Calculate = 1,

    /// <summary>Rate it and keep nothing. Anyone can try anything.</summary>
    Simulate = 2,
}

/// <summary>
/// Everything about rating, with no permission checks: the API handler and the integration contracts (planning, audit) both go through it and authorise their own callers.
/// </summary>
internal sealed class RatingService(
    ContractsDbContext db,
    RatingCandidateLoader loader,
    ITransporterDirectory transporters,
    IVehicleTypeDirectory vehicleTypes,
    IFreightActualsProvider actuals,
    NumberSequence sequence,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<(RatingResultDto Result, RatingOutcome Outcome)> RunAsync(RatingRequestDto request, RunMode mode, CancellationToken cancellationToken)
    {
        var input = await BuildInputAsync(request, cancellationToken);
        var candidates = await loader.LoadAsync(input, request.ContractId, request.Preview, cancellationToken);
        var outcome = RatingEngine.Rate(candidates.Contracts, input, candidates.Context);

        FreightRating? kept = null;
        if (mode == RunMode.Calculate && !request.Preview && currentUser.TenantId is { } tenant && (request.Commit || !outcome.Qualified))
        {
            var number = await sequence.NextAsync(tenant, "rating", cancellationToken);
            kept = FreightRating.Create(tenant, $"FR-{number:D6}", input, outcome, request.Commit && outcome.Qualified, clock.GetUtcNow());
            if (kept.Committed)
            {
                await RecordDphAsync(kept, outcome, candidates, cancellationToken);
            }

            db.Ratings.Add(kept);
            await db.SaveChangesAsync(cancellationToken);
        }

        var names = await transporters.GetAsync(outcome.Options.Select(o => o.Contract.TransporterId), cancellationToken);
        return (RatingMapper.ToResult(outcome, input, kept, names, request.Commit && kept is { Committed: true }, candidates.Context.Zones), outcome);
    }

    /// <summary>Rates once for each of several transporters from a single read of the rate book.</summary>
    public async Task<IReadOnlyList<(Guid TransporterId, RatingOutcome Outcome)>> RunForEachAsync(RatingRequestDto request, IReadOnlyList<Guid> transporterIds, CancellationToken cancellationToken)
    {
        var input = await BuildInputAsync(request with { TransporterId = null }, cancellationToken);
        var candidates = await loader.LoadAsync(input, null, preview: false, cancellationToken);
        return transporterIds.Select(t => (t, RatingEngine.Rate(candidates.Contracts, input with { TransporterId = t }, candidates.Context))).ToList();
    }

    /// <summary>The same shipment rated again against exactly the contract it was rated on, for checking a kept rating.</summary>
    public async Task<RatingOutcome?> RerateAsync(FreightRating rating, CancellationToken cancellationToken)
    {
        if (rating.ContractId is not { } contractId || rating.ReadInput() is not { } input)
        {
            return null;
        }

        var candidates = await loader.LoadAsync(input, contractId, preview: true, cancellationToken);
        return RatingEngine.Rate(candidates.Contracts, input with { TransporterId = rating.TransporterId }, candidates.Context);
    }

    private async Task<RatingInput> BuildInputAsync(RatingRequestDto request, CancellationToken cancellationToken)
    {
        var inputs = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in request.AccessorialInputs ?? new Dictionary<string, decimal>())
        {
            inputs[key.Trim().ToUpperInvariant()] = value;
        }

        var distance = request.DistanceKm;
        var stops = Math.Max(1, request.StopCount);
        if (request.UseActuals && !string.IsNullOrWhiteSpace(request.ShipmentReference) && await actuals.GetActualsAsync(request.ShipmentReference.Trim(), cancellationToken) is { } actual)
        {
            // What happened wins over a plan, but never over something the caller stated outright.
            distance ??= actual.ActualDistanceKm;
            stops = Math.Max(stops, actual.ActualStopCount ?? stops);
            void Fill(string code, decimal? value)
            {
                if (value is > 0 && !inputs.ContainsKey(code))
                {
                    inputs[code] = value.Value;
                }
            }

            Fill("DETENTION", actual.DetentionHours);
            Fill("WAITING", actual.WaitingHours);
            Fill("EXTRA_KM", actual.ExtraKm);
            foreach (var (code, value) in actual.Other ?? new Dictionary<string, decimal>())
            {
                Fill(code.ToUpperInvariant(), value);
            }
        }

        return new RatingInput(
            request.ShipmentDate ?? clock.TodayInIndia(), request.TransporterId, request.Origin, request.Destination, request.Service, request.VehicleTypeId,
            request.WeightKg, request.VolumeCbm, distance, stops, request.RequiredCapabilities ?? [], inputs, request.ShipmentReference?.Trim(), null);
    }

    /// <summary>A kept rating fixes the diesel price its DPH adjustment used, so the same adjustment can be produced when the index is later corrected.</summary>
    private async Task RecordDphAsync(FreightRating rating, RatingOutcome outcome, Candidates candidates, CancellationToken cancellationToken)
    {
        if (outcome.Selected is not { DphRule: { } rule, DphResult: { } dph } best || candidates.Context.Snapshots?.ContainsKey((rule.Id, dph.ReferenceDate)) == true)
        {
            return;
        }

        if (await db.DphSnapshots.AnyAsync(s => s.RuleId == rule.Id && s.PeriodStart == dph.ReferenceDate, cancellationToken))
        {
            return;
        }

        db.DphSnapshots.Add(DphPeriodSnapshot.Create(rating.TenantId, rule, dph.ReferenceDate, dph.ReferencePrice, dph.VariationPercent, dph.Applied ? dph.AdjustmentPercent : 0m, RatingEngine.Version));
        rating.AnnounceDphRevision(best.Contract.Id, best.Contract.Reference, rule.Code, dph.ReferenceDate, dph.ReferencePrice, dph.Applied ? dph.AdjustmentPercent : 0m);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        (await transporters.GetAsync(ids.Distinct(), cancellationToken)).ToDictionary(p => p.Key, p => p.Value.LegalName);

    public Task<IReadOnlyDictionary<Guid, VehicleTypeInfo>> VehicleTypesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) => vehicleTypes.GetAsync(ids.Distinct(), cancellationToken);
}

internal static class RatingMapper
{
    public static RatingResultDto ToResult(RatingOutcome outcome, RatingInput input, FreightRating? kept, IReadOnlyDictionary<Guid, TransporterInfo> names, bool committed, Func<string, Zone?> zones)
    {
        var options = outcome.Options.Select(o => ToOption(o, input, names.TryGetValue(o.Contract.TransporterId, out var t) ? t.LegalName : "Unknown transporter", zones)).ToList();
        return new RatingResultDto(
            outcome.Qualified, outcome.ErrorCode, outcome.Message, outcome.Advice, kept?.Id, kept?.Reference, committed, RatingEngine.Version, input.Date,
            options.FirstOrDefault(), options,
            outcome.Exclusions.Select(e => new RatingExclusionDto(e.ContractReference, e.RateCode, e.Code, e.Reason)).ToList(),
            outcome.Trace.Select(t => new TraceStepDto(t.Stage, t.Text, t.Ok)).ToList());
    }

    public static RatingOptionDto ToOption(RatingBreakdown b, RatingInput input, string transporterName, Func<string, Zone?> zones)
    {
        var sequence = 0;
        var transit = b.Contract.Slas.Select(s => s.Spec).Where(s => s.Service == input.Service && s.TransitSlaMinutes is not null)
            .OrderByDescending(s => (s.Origin is null ? 0 : 1) + (s.Destination is null ? 0 : 1)).FirstOrDefault(s => s.CoversLane(input.Origin, input.Destination, zones))?.TransitSlaMinutes;
        return new RatingOptionDto(
            b.Contract.Id, b.Contract.Number, b.Contract.Revision, b.Contract.TransporterId, transporterName,
            new RatingRateDto(b.Card.Id, b.Card.Code, b.Card.Version, $"{b.Card.Origin} → {b.Card.Destination}", b.Card.Priority, b.Card.Pricing.ServiceType().ToString()),
            b.DphRule?.Code, b.DphRule?.Version, b.BaseFreight, b.Dph, b.Accessorials, b.Discount, b.Total, b.Currency, transit, b.Contract.EffectiveFrom, b.Contract.EffectiveTo,
            b.Lines.Select(l => new RatingLineDto(++sequence, l.Type, l.Description, l.Quantity, l.Unit, l.Rate, l.Amount, l.Reference)).ToList(), b.Reasons, b.Notes, b.ChargeableWeightKg);
    }

    public static FreightRatingResult ToFact(RatingResultDto r, Guid? transporterId)
    {
        var s = r.Selected;
        return new FreightRatingResult(
            r.Qualified, r.ErrorCode, r.Message, r.RatingId, r.RatingReference, s?.ContractId, s?.ContractReference, s?.ContractRevision, s?.TransporterId ?? transporterId, s is null ? null : $"{s.Rate.Code}",
            s?.Rate.Version, s?.DphRule, s?.DphVersion, s?.BaseFreight ?? 0, s?.DphAdjustment ?? 0, s?.AccessorialAmount ?? 0, s?.DiscountAmount ?? 0, s?.TotalFreight ?? 0, s?.Currency ?? "INR", r.CalculationVersion,
            s?.Lines.Select(l => new RatingComponentFact(l.Type, l.Description, l.Quantity, l.Unit, l.Rate, l.Amount, l.Reference, l.Sequence)).ToList() ?? [],
            s?.Reasons ?? [], r.Exclusions.Select(e => new RateExclusionFact(e.ContractReference, e.RateReference, e.ReasonCode, e.Reason)).ToList());
    }

    public static RatingSummaryDto ToSummary(FreightRating r, IReadOnlyDictionary<Guid, string> names) =>
        new(r.Id, r.Reference, r.ShipmentReference, r.Committed, r.Qualified, r.ErrorCode, r.Lane, r.Service, r.ShipmentDate, r.TransporterId,
            r.TransporterId is { } t && names.TryGetValue(t, out var n) ? n : null, r.ContractReference, r.ContractRevision, r.RateCode, r.RateVersion, r.TotalFreight, r.OverrideAmount, r.Currency,
            r.CalculationVersion, r.CalculatedAt);
}
