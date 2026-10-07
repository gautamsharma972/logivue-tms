using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Application.Analytics;

/// <summary>
/// What a new version or renewal would have cost against what was actually rated. The kept ratings of the contract it replaces are rated again under the proposed rates, so the
/// figures come from real shipments and not from a guess about volume. With no history it says so and compares the rates themselves.
/// </summary>
internal sealed class ImpactHandler(ContractsDbContext db, ContractAccess access, TimeProvider clock)
{
    public async Task<Result<ImpactDto>> HandleAsync(Guid proposedId, int? months, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var window = Math.Clamp(months ?? 12, 1, 36);
        var proposed = await db.Contracts.AsNoTracking().AsSplitQuery().Include(c => c.RateCards).Include(c => c.DphRules).Include(c => c.Accessorials).Include(c => c.Slas)
            .FirstOrDefaultAsync(c => c.Id == proposedId, cancellationToken);
        if (proposed is null)
        {
            return ContractAccess.NotFound;
        }

        if (proposed.RevisionOfId is not { } currentId)
        {
            return Error.Validation("impact.not_a_revision", "Impact is measured for a revision or renewal of an existing contract.");
        }

        var current = await db.Contracts.AsNoTracking().Include(c => c.RateCards).FirstAsync(c => c.Id == currentId, cancellationToken);
        var chain = await db.Contracts.AsNoTracking().Where(c => c.Number == proposed.Number && c.Revision < proposed.Revision).Select(c => c.Id).ToListAsync(cancellationToken);
        var since = clock.GetUtcNow().AddMonths(-window);
        var history = await db.Ratings.AsNoTracking().Where(r => r.Committed && r.Qualified && r.ContractId != null && chain.Contains(r.ContractId.Value) && r.CalculatedAt >= since).OrderByDescending(r => r.CalculatedAt).Take(5_000).ToListAsync(cancellationToken);

        var zones = (await db.Zones.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(z => z.Code);
        var diesel = await db.DieselPrices.AsNoTracking().ToListAsync(cancellationToken);
        var context = new RatingContext(code => zones.GetValueOrDefault(code), diesel, null, RequireInForce: false);

        var perRate = new Dictionary<string, (int Shipments, decimal Current, decimal Proposed)>();
        var notRated = 0;
        foreach (var rating in history)
        {
            if (rating.ReadInput() is not { } input)
            {
                continue;
            }

            var outcome = RatingEngine.Rate([proposed], input with { TransporterId = proposed.TransporterId }, context);
            if (outcome.Selected is not { } best)
            {
                notRated++;
                continue;
            }

            var key = rating.RateCode ?? best.Card.Code;
            var (n, a, b) = perRate.GetValueOrDefault(key);
            perRate[key] = (n + 1, a + rating.TotalFreight, b + best.Total);
        }

        var rows = new List<ImpactRowDto>();
        var proposedByCode = proposed.RateCards.ToDictionary(c => c.Code);
        foreach (var old in current.RateCards.OrderBy(c => c.Code, StringComparer.Ordinal))
        {
            if (!proposedByCode.TryGetValue(old.Code, out var next))
            {
                continue;
            }

            var (before, after) = (Headline(old.Pricing), Headline(next.Pricing));
            var used = perRate.GetValueOrDefault(old.Code);
            if (before == after && used.Shipments == 0)
            {
                continue;
            }

            rows.Add(new ImpactRowDto(old.Code, $"{old.Origin} → {old.Destination}", before, after, before == 0 ? 0 : Math.Round((after - before) / before * 100m, 2), used.Shipments, used.Current, used.Proposed, used.Proposed - used.Current));
        }

        var currentSpend = perRate.Values.Sum(v => v.Current);
        var proposedSpend = perRate.Values.Sum(v => v.Proposed);
        var variance = proposedSpend - currentSpend;
        var basis = history.Count == 0
            ? $"No freight was rated against {current.Reference} in the last {window} month(s), so only the rates are compared. Keep ratings against shipments to see the effect on spend."
            : $"{history.Count} kept rating(s) from the last {window} month(s) were rated again under {proposed.Reference}. Annualised assumes the same volume continues.";
        return new ImpactDto(
            current.Id, current.Reference, proposed.Id, proposed.Reference, history.Count - notRated, window, currentSpend, proposedSpend, variance, currentSpend == 0 ? 0 : Math.Round(variance / currentSpend * 100m, 2),
            Math.Round(variance * 12m / window, 2), rows.Count(r => r.ProposedRate != r.CurrentRate), notRated, rows, basis);
    }

    /// <summary>The number a person would quote for the rate: the trip price, the per-km or per-unit rate, the monthly rental.</summary>
    internal static decimal Headline(Pricing pricing) => pricing switch
    {
        FlatTripPricing f => f.AmountPerTrip,
        PerKmPricing k => k.RatePerKm,
        WeightSlabPricing w => w.Slabs[0].RatePerKg,
        SlabRatePricing s => s.Slabs[0].Rate,
        DedicatedPricing d => d.MonthlyRental,
        _ => 0m,
    };
}
