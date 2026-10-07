using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application.Rating;

/// <summary>The rating API: calculate, qualify, simulate, what-if, compare, history, the stored trace, reproduction and the commercial override.</summary>
internal sealed class FreightRatingHandler(ContractsDbContext db, ContractAccess access, RatingService rating, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result<RatingResultDto>> CalculateAsync(RatingRequestDto request, CancellationToken cancellationToken)
    {
        // Keeping a rating against a shipment is a commercial act; rating to see a number is open to anyone who can read contracts.
        if (request.Commit ? !access.CanRate : !access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        return (await rating.RunAsync(request, RunMode.Calculate, cancellationToken)).Result;
    }

    /// <summary>Every transporter that qualifies, cheapest-preferred first, so a planner can choose.</summary>
    public async Task<Result<RatingResultDto>> QualifyAsync(RatingRequestDto request, CancellationToken cancellationToken) =>
        access.CanRead ? (await rating.RunAsync(request with { Commit = false, ShipmentReference = request.ShipmentReference }, RunMode.Simulate, cancellationToken)).Result : ContractAccess.Forbidden;

    public async Task<Result<RatingResultDto>> SimulateAsync(RatingRequestDto request, CancellationToken cancellationToken) =>
        access.CanRead ? (await rating.RunAsync(request with { Commit = false }, RunMode.Simulate, cancellationToken)).Result : ContractAccess.Forbidden;

    public async Task<Result<WhatIfResultDto>> WhatIfAsync(WhatIfRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var baseRun = (await rating.RunAsync(request.Base with { Commit = false }, RunMode.Simulate, cancellationToken)).Result;
        var baseRow = Row("Current", baseRun, null);
        var rows = new List<WhatIfRowDto>();
        foreach (var v in request.Variations)
        {
            var changed = request.Base with
            {
                Commit = false,
                WeightKg = v.WeightKg ?? request.Base.WeightKg,
                VolumeCbm = v.VolumeCbm ?? request.Base.VolumeCbm,
                DistanceKm = v.DistanceKm ?? request.Base.DistanceKm,
                VehicleTypeId = v.VehicleTypeId ?? request.Base.VehicleTypeId,
                StopCount = v.StopCount ?? request.Base.StopCount,
                ShipmentDate = v.ShipmentDate ?? request.Base.ShipmentDate,
                TransporterId = v.TransporterId ?? request.Base.TransporterId,
            };
            rows.Add(Row(v.Label, (await rating.RunAsync(changed, RunMode.Simulate, cancellationToken)).Result, baseRow.TotalFreight));
        }

        return new WhatIfResultDto(baseRow, rows);
    }

    private static WhatIfRowDto Row(string label, RatingResultDto result, decimal? baseTotal) =>
        new(label, result.Qualified, result.Selected?.TotalFreight, result.Selected is { } s && baseTotal is { } b ? s.TotalFreight - b : null, result.Qualified ? null : result.Message, result.Selected);

    /// <summary>The same shipment priced by several transporters side by side.</summary>
    public async Task<Result<IReadOnlyList<CompareRowDto>>> CompareAsync(CompareRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var ids = request.TransporterIds.Distinct().ToList();
        var outcomes = await rating.RunForEachAsync(request.Request with { Commit = false }, ids, cancellationToken);
        var names = await rating.NamesAsync(ids, cancellationToken);
        var rows = new List<CompareRowDto>();
        foreach (var (transporterId, outcome) in outcomes)
        {
            var input = new RatingInput(request.Request.ShipmentDate ?? clock.TodayInIndia(), transporterId, request.Request.Origin, request.Request.Destination, request.Request.Service, request.Request.VehicleTypeId,
                request.Request.WeightKg, request.Request.VolumeCbm, request.Request.DistanceKm, request.Request.StopCount, [], new Dictionary<string, decimal>());
            var option = outcome.Selected is { } best ? RatingMapper.ToOption(best, input, names.GetValueOrDefault(transporterId, "Unknown transporter"), _ => null) : null;
            rows.Add(new CompareRowDto(transporterId, names.GetValueOrDefault(transporterId, "Unknown transporter"), outcome.Qualified, outcome.Qualified ? null : outcome.Message, option));
        }

        return Result.Success<IReadOnlyList<CompareRowDto>>(rows.OrderBy(r => r.Option?.TotalFreight ?? decimal.MaxValue).ToList());
    }

    public async Task<Result<PagedResult<RatingSummaryDto>>> ListAsync(ListRatingsQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var ratings = db.Ratings.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.ShipmentReference))
        {
            var reference = query.ShipmentReference.Trim();
            ratings = ratings.Where(r => r.ShipmentReference == reference);
        }

        if (query.TransporterId is { } t)
        {
            ratings = ratings.Where(r => r.TransporterId == t);
        }

        if (query.ContractId is { } c)
        {
            ratings = ratings.Where(r => r.ContractId == c);
        }

        if (query.Qualified is { } q)
        {
            ratings = ratings.Where(r => r.Qualified == q);
        }

        if (query.Committed is { } committed)
        {
            ratings = ratings.Where(r => r.Committed == committed);
        }

        if (query.From is { } from)
        {
            var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
            ratings = ratings.Where(r => r.CalculatedAt >= since);
        }

        if (query.To is { } to)
        {
            var until = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
            ratings = ratings.Where(r => r.CalculatedAt < until);
        }

        var page = await ratings.OrderByDescending(r => r.CalculatedAt).ThenBy(r => r.Id).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        var names = await rating.NamesAsync(page.Items.Where(r => r.TransporterId.HasValue).Select(r => r.TransporterId!.Value), cancellationToken);
        return new PagedResult<RatingSummaryDto>(page.Items.Select(r => RatingMapper.ToSummary(r, names)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>A kept rating with everything it rests on: the request, every line, the rejected alternatives and the steps taken.</summary>
    public async Task<Result<RatingDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var r = await db.Ratings.AsNoTracking().AsSplitQuery().Include(x => x.Components).Include(x => x.Exclusions).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r is null)
        {
            return Error.NotFound("rating.not_found", "Rating not found.");
        }

        var names = await rating.NamesAsync(r.TransporterId is { } t ? [t] : [], cancellationToken);
        var contract = r.ContractId is { } cid ? await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, cancellationToken) : null;
        var transporterName = r.TransporterId is { } tid && names.TryGetValue(tid, out var n) ? n : "Unknown transporter";
        var option = r.Qualified
            ? new RatingOptionDto(
                r.ContractId!.Value, r.ContractReference!, r.ContractRevision ?? 1, r.TransporterId ?? Guid.Empty, transporterName,
                new RatingRateDto(r.RateCardId ?? Guid.Empty, r.RateCode ?? string.Empty, r.RateVersion ?? 1, r.Lane, 0, r.Service.ToString()), r.DphRuleCode, r.DphVersion,
                r.BaseFreight, r.DphAdjustment, r.AccessorialAmount, r.DiscountAmount, r.TotalFreight, r.Currency, null, contract?.EffectiveFrom ?? default, contract?.EffectiveTo ?? default,
                r.Components.OrderBy(c => c.Sequence).Select(c => new RatingLineDto(c.Sequence, c.Type, c.Description, c.Quantity, c.Unit, c.Rate, c.Amount, c.Reference)).ToList(), r.ReadReasons(), [], null)
            : null;
        var result = new RatingResultDto(
            r.Qualified, r.ErrorCode, r.Message, [], r.Id, r.Reference, r.Committed, r.CalculationVersion, r.ShipmentDate, option, option is null ? [] : [option],
            r.Exclusions.Select(e => new RatingExclusionDto(e.ContractReference, e.RateReference, e.ReasonCode, e.Reason)).ToList(), r.ReadTrace().Select(t => new TraceStepDto(t.Stage, t.Text, t.Ok)).ToList());

        RatingRequestDto? request = null;
        if (r.ReadInput() is { } input)
        {
            request = new RatingRequestDto(input.Date, input.Origin, input.Destination, input.Service, input.TransporterId, input.VehicleTypeId, input.WeightKg, input.VolumeCbm, input.DistanceKm, input.StopCount,
                input.Capabilities, input.AccessorialInputs, r.ShipmentReference, r.Committed);
        }

        return new RatingDetailDto(RatingMapper.ToSummary(r, names), result, request, r.OverrideAmount, r.OverrideReason, r.OverrideApprovedBy, r.OverriddenAt, r.DphRuleCode, r.DphVersion);
    }

    /// <summary>
    /// Rates the stored request again against the same contract and compares. A rating made under an older version of the calculation is not recalculated: the stored result
    /// is the authority and the answer says so.
    /// </summary>
    public async Task<Result<ReproduceDto>> ReproduceAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var stored = await db.Ratings.AsNoTracking().Include(x => x.Components).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (stored is null)
        {
            return Error.NotFound("rating.not_found", "Rating not found.");
        }

        if (!stored.Qualified)
        {
            return new ReproduceDto(true, false, stored.CalculationVersion, RatingEngine.Version, 0, null, [], "This attempt found no rate, so there is nothing to reproduce.");
        }

        if (stored.CalculationVersion != RatingEngine.Version)
        {
            return new ReproduceDto(true, false, stored.CalculationVersion, RatingEngine.Version, stored.TotalFreight, null, [],
                $"Rated under calculation version {stored.CalculationVersion}; this system now calculates with {RatingEngine.Version}. The stored result stands and is not recalculated.");
        }

        var again = await rating.RerateAsync(stored, cancellationToken);
        var best = again?.Selected;
        var differences = new List<string>();
        if (best is null)
        {
            differences.Add("The contract no longer produces a rate for this request.");
        }
        else
        {
            if (best.Total != stored.TotalFreight)
            {
                differences.Add($"Total {best.Total:0.##} against stored {stored.TotalFreight:0.##}");
            }

            var storedLines = stored.Components.OrderBy(c => c.Sequence).Select(c => (c.Type, c.Amount)).ToList();
            var lines = best.Lines.Select(l => (l.Type, l.Amount)).ToList();
            if (!storedLines.SequenceEqual(lines))
            {
                differences.Add("The lines differ from those stored.");
            }
        }

        return new ReproduceDto(differences.Count == 0, true, stored.CalculationVersion, RatingEngine.Version, stored.TotalFreight, best?.Total, differences,
            differences.Count == 0 ? "Rated again from the stored request, the same freight and the same lines were produced." : "The result differs from the stored one; the stored rating remains the record.");
    }

    public async Task<Result<RatingDetailDto>> OverrideAsync(Guid id, OverrideRatingRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanOverride)
        {
            return ContractAccess.Forbidden;
        }

        var r = await db.Ratings.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r is null)
        {
            return Error.NotFound("rating.not_found", "Rating not found.");
        }

        var applied = r.ApplyOverride(request.Amount, request.Reason, request.ApprovedBy, currentUser.UserId, clock.GetUtcNow());
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<RatingDetailDto>> ClearOverrideAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanOverride)
        {
            return ContractAccess.Forbidden;
        }

        var r = await db.Ratings.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r is null)
        {
            return Error.NotFound("rating.not_found", "Rating not found.");
        }

        var cleared = r.ClearOverride();
        if (cleared.IsFailure)
        {
            return cleared.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }
}
