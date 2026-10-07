using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tms.Modules.Contracts.Application.Contracts;
using Tms.Modules.Contracts.Application.Lifecycle;
using Tms.Modules.Contracts.Application.Rates;
using Tms.Modules.Contracts.Application.Terms;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Application.Analytics;

/// <summary>The commercial picture: how many contracts and rates are live, what is about to lapse, where there is no rate, and what is wrong with the drafts.</summary>
internal sealed class DashboardHandler(ContractsDbContext db, ContractAccess access, ContractRateChecker checker, DphHandler dph, IOptions<ContractLifecycleOptions> options, TimeProvider clock)
{
    private const int Window = 90;

    public async Task<Result<ContractDashboardDto>> SummaryAsync(CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var today = clock.TodayInIndia();
        var soon = today.AddDays(60);
        var contracts = await db.Contracts.AsNoTracking().Select(c => new { c.Number, c.Status, c.EffectiveFrom, c.EffectiveTo, c.RateCount }).ToListAsync(cancellationToken);
        var live = contracts.Where(c => c.Status == ContractStatus.Active && c.EffectiveFrom <= today && c.EffectiveTo >= today).ToList();
        var expiringRates = await db.RateCards.AsNoTracking()
            .Join(db.Contracts, r => r.ContractId, c => c.Id, (r, c) => new { r.ValidTo, c.Status, c.EffectiveTo })
            .CountAsync(x => x.Status == ContractStatus.Active && (x.ValidTo ?? x.EffectiveTo) >= today && (x.ValidTo ?? x.EffectiveTo) <= soon, cancellationToken);
        var rules = await dph.ListAsync(null, true, cancellationToken);
        var coverage = await CoverageAsync(cancellationToken);
        var failed = await db.Ratings.AsNoTracking().CountAsync(r => !r.Qualified && r.CalculatedAt >= clock.GetUtcNow().AddDays(-30), cancellationToken);
        var validation = await ValidationAsync(cancellationToken);

        return new ContractDashboardDto(
            contracts.Select(c => c.Number).Distinct().Count(),
            live.Count,
            contracts.Count(c => c.Status is ContractStatus.Draft or ContractStatus.Rejected),
            contracts.Count(c => c.Status == ContractStatus.PendingApproval),
            live.Count(c => c.EffectiveTo <= soon),
            contracts.Count(c => c.Status == ContractStatus.Expired || (c.Status == ContractStatus.Active && c.EffectiveTo < today)),
            contracts.Count(c => c.Status == ContractStatus.Suspended),
            live.Sum(c => c.RateCount),
            expiringRates,
            rules.IsSuccess ? rules.Value.Count : 0,
            rules.IsSuccess ? rules.Value.Count(r => r.RevisionDue) : 0,
            coverage.IsSuccess ? coverage.Value.UncoveredLanes : 0,
            validation.IsSuccess ? validation.Value.Errors : 0,
            failed,
            clock.GetUtcNow());
    }

    /// <summary>What is about to lapse: contracts, rates, DPH versions, documents and commitments, each in the nearest band.</summary>
    public async Task<Result<ExpiryDto>> ExpiryAsync(int? withinDays, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var today = clock.TodayInIndia();
        var bands = options.Value.ExpiryBands.OrderBy(b => b).ToArray();
        var horizon = today.AddDays(Math.Clamp(withinDays ?? bands[^1], 1, 365));
        var items = new List<ExpiryItemDto>();

        ExpiryItemDto Item(string kind, string reference, string title, Guid contractId, string number, DateOnly on)
        {
            var left = on.DayNumber - today.DayNumber;
            var band = left < 0 ? "Expired" : $"{bands.FirstOrDefault(b => left <= b, bands[^1])} days";
            return new ExpiryItemDto(kind, reference, title, contractId, number, on, left, band);
        }

        var contracts = await db.Contracts.AsNoTracking().Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Suspended) && c.EffectiveTo <= horizon).ToListAsync(cancellationToken);
        items.AddRange(contracts.Select(c => Item("Contract", c.Reference, c.Title, c.Id, c.Number, c.EffectiveTo)));

        var rates = await db.RateCards.AsNoTracking().Join(db.Contracts, r => r.ContractId, c => c.Id, (r, c) => new { Card = r, Contract = c })
            .Where(x => x.Contract.Status == ContractStatus.Active && x.Card.ValidTo != null && x.Card.ValidTo <= horizon && x.Card.ValidTo < x.Contract.EffectiveTo)
            .Take(500).ToListAsync(cancellationToken);
        items.AddRange(rates.Select(x => Item("Rate", $"{x.Card.Code} V{x.Card.Version}", $"{x.Card.Origin} → {x.Card.Destination}", x.Contract.Id, x.Contract.Number, x.Card.ValidTo!.Value)));

        var rules = await db.DphRules.AsNoTracking().Join(db.Contracts, r => r.ContractId, c => c.Id, (r, c) => new { Rule = r, Contract = c })
            .Where(x => x.Contract.Status == ContractStatus.Active).ToListAsync(cancellationToken);
        items.AddRange(rules.Where(x => x.Rule.Spec.EffectiveTo is { } to && to <= horizon && to < x.Contract.EffectiveTo)
            .Select(x => Item("DPH rule", $"{x.Rule.Code} V{x.Rule.Version}", x.Rule.Spec.Name, x.Contract.Id, x.Contract.Number, x.Rule.Spec.EffectiveTo!.Value)));

        var documents = await db.Documents.AsNoTracking().Join(db.Contracts, d => d.ContractId, c => c.Id, (d, c) => new { Doc = d, Contract = c })
            .Where(x => x.Doc.ExpiryDate != null && x.Doc.ExpiryDate <= horizon && (x.Contract.Status == ContractStatus.Active || x.Contract.Status == ContractStatus.Suspended)).Take(500).ToListAsync(cancellationToken);
        items.AddRange(documents.Select(x => Item("Document", x.Doc.Number ?? x.Doc.Title, x.Doc.Title, x.Contract.Id, x.Contract.Number, x.Doc.ExpiryDate!.Value)));

        var capacity = await db.Capacities.AsNoTracking().Join(db.Contracts, d => d.ContractId, c => c.Id, (d, c) => new { Cap = d, Contract = c })
            .Where(x => x.Contract.Status == ContractStatus.Active).ToListAsync(cancellationToken);
        items.AddRange(capacity.Where(x => x.Cap.Spec.ValidTo is { } to && to <= horizon && to < x.Contract.EffectiveTo)
            .Select(x => Item("Capacity commitment", $"{x.Cap.Spec.CommittedVehicleCount} vehicle(s)", "Committed capacity", x.Contract.Id, x.Contract.Number, x.Cap.Spec.ValidTo!.Value)));

        return new ExpiryDto(bands, items.OrderBy(i => i.ExpiresOn).ThenBy(i => i.Kind).ToList());
    }

    /// <summary>
    /// Where rates exist and where they were missed. Lanes are the ones that have been asked for (a kept rating, or an attempt that found nothing), so this measures the book
    /// against real demand rather than against a list somebody has to maintain.
    /// </summary>
    public async Task<Result<RateCoverageDto>> CoverageAsync(CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var today = clock.TodayInIndia();
        var since = clock.GetUtcNow().AddDays(-Window);
        var recent = await db.Ratings.AsNoTracking().Where(r => r.CalculatedAt >= since).Select(r => new { r.Lane, r.Service, r.Qualified, r.Committed, r.CalculatedAt, r.RateCardId, r.ErrorCode, r.Message }).ToListAsync(cancellationToken);
        var lanes = recent.GroupBy(r => (r.Lane, r.Service)).Select(g => new
        {
            g.Key.Lane,
            g.Key.Service,
            Requests = g.Count(),
            Failed = g.Count(x => !x.Qualified),
            LastFailure = g.Where(x => !x.Qualified).Select(x => (DateTimeOffset?)x.CalculatedAt).Max(),
            LastSuccess = g.Where(x => x.Qualified).Select(x => (DateTimeOffset?)x.CalculatedAt).Max(),
            Reason = g.Where(x => !x.Qualified).OrderByDescending(x => x.CalculatedAt).Select(x => x.ErrorCode).FirstOrDefault(),
        }).ToList();
        var uncovered = lanes.Where(l => l.LastFailure is not null && (l.LastSuccess is null || l.LastSuccess < l.LastFailure)).ToList();

        var rateIds = recent.Where(r => r.Qualified && r.RateCardId.HasValue).Select(r => r.RateCardId!.Value).Distinct().ToList();
        var fallback = rateIds.Count == 0 ? 0 : await db.RateCards.AsNoTracking().CountAsync(r => rateIds.Contains(r.Id) && (r.OriginKind != PlaceKind.City || r.DestinationKind != PlaceKind.City), cancellationToken);

        var live = await db.Contracts.AsNoTracking().Include(c => c.RateCards).Where(c => c.Status == ContractStatus.Active && c.EffectiveFrom <= today && c.EffectiveTo >= today).Take(2_000).ToListAsync(cancellationToken);
        var soon = today.AddDays(60);
        var rates = live.SelectMany(c => c.RateCards.Select(r => new RateToCheck(0, r.ToSpec(), r.ValidFrom ?? c.EffectiveFrom, r.ValidTo ?? c.EffectiveTo, c.Reference))).ToList();
        var indexed = rates.Select((r, i) => r with { Row = i + 1 }).ToList();
        var clashes = RateValidator.Validate(indexed, DateOnly.MinValue, DateOnly.MaxValue, [ContractType.Ftl, ContractType.Ptl, ContractType.Dedicated], new HashSet<string>(StringComparer.Ordinal), null);
        return new RateCoverageDto(
            lanes.Count, lanes.Count - uncovered.Count, uncovered.Count, fallback, live.Count, rates.Count, rates.Count(r => r.To <= soon),
            clashes.Issues.Count(i => i.Code == "RATE_DUPLICATE"), clashes.Issues.Count(i => i.Code is "RATE_CONFLICT" or "RATE_OVERLAP_PRIORITY"),
            recent.Count(r => !r.Qualified && r.CalculatedAt >= clock.GetUtcNow().AddDays(-30)),
            uncovered.OrderByDescending(l => l.Failed).Take(100).Select(l => new LaneCoverageDto(l.Lane, l.Service.ToString(), "Uncovered", l.Requests, l.Failed, l.Reason)).ToList());
    }

    /// <summary>What is wrong with contracts that are not yet in force, so it can be fixed before it blocks approval.</summary>
    public async Task<Result<ValidationOverviewDto>> ValidationAsync(CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var drafts = await db.Contracts.AsNoTracking().AsSplitQuery().Include(c => c.RateCards).Include(c => c.DphRules)
            .Where(c => c.Status == ContractStatus.Draft || c.Status == ContractStatus.Rejected || c.Status == ContractStatus.PendingApproval).OrderByDescending(c => c.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var rows = new List<ValidationContractDto>();
        foreach (var contract in drafts)
        {
            var result = await checker.CheckAsync(contract, cancellationToken);
            if (result.Issues.Count > 0)
            {
                rows.Add(new ValidationContractDto(contract.Id, contract.Reference, contract.Status, result.Errors, result.Warnings, result.Issues.Take(50).Select(RateHandler.ToDto).ToList()));
            }
        }

        return new ValidationOverviewDto(drafts.Count, rows.Sum(r => r.Errors), rows.Sum(r => r.Warnings), rows);
    }

    /// <summary>How much each rate has been used, from kept ratings: shipments, freight rated and the average.</summary>
    public async Task<Result<IReadOnlyList<RateUsageDto>>> UsageAsync(int? months, Guid? transporterId, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var since = clock.GetUtcNow().AddMonths(-Math.Clamp(months ?? 12, 1, 60));
        var ratings = db.Ratings.AsNoTracking().Where(r => r.Committed && r.Qualified && r.CalculatedAt >= since && r.RateCardId != null);
        if (transporterId is { } t)
        {
            ratings = ratings.Where(r => r.TransporterId == t);
        }

        var rows = await ratings.GroupBy(r => new { r.ContractId, r.ContractReference, r.ContractRevision, r.RateCode, r.RateVersion, r.Lane })
            .Select(g => new { g.Key.ContractId, g.Key.ContractReference, g.Key.ContractRevision, g.Key.RateCode, g.Key.RateVersion, g.Key.Lane, Count = g.Count(), Total = g.Sum(x => x.TotalFreight) })
            .OrderByDescending(x => x.Total).Take(500).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<RateUsageDto>>(rows.Select(x => new RateUsageDto(x.ContractId!.Value, x.ContractReference!, x.ContractRevision ?? 1, x.RateCode!, x.RateVersion ?? 1, x.Lane, x.Count, x.Total, Math.Round(x.Total / x.Count, 2))).ToList());
    }
}
