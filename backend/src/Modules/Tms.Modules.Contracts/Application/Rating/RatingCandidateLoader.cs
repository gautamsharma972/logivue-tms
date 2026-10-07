using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;

namespace Tms.Modules.Contracts.Application.Rating;

public sealed record Candidates(IReadOnlyList<Contract> Contracts, RatingContext Context);

/// <summary>
/// Finds the contracts and rates that could price a shipment without reading the whole rate book: contracts by transporter and dates first, then only rates on plausible
/// lanes, with their DPH rules, charges and service levels. The engine then decides among that small set.
/// </summary>
internal sealed class RatingCandidateLoader(ContractsDbContext db)
{
    private const int MaxContracts = 200;

    public async Task<Candidates> LoadAsync(RatingInput input, Guid? contractId, bool preview, CancellationToken cancellationToken)
    {
        var origin = input.Origin.NormalState;
        var destination = input.Destination.NormalState;

        // Narrow to plausible lanes in SQL (by state / kind); exact matching, zones and scoring happen in the engine.
        Expression<Func<RateCard, bool>> plausible = rc =>
            ((rc.OriginKind == PlaceKind.Any || rc.OriginKind == PlaceKind.Zone || rc.OriginState == origin)
             && (rc.DestinationKind == PlaceKind.Any || rc.DestinationKind == PlaceKind.Zone || rc.DestinationState == destination))
            || (rc.BothWays
                && (rc.OriginKind == PlaceKind.Any || rc.OriginKind == PlaceKind.Zone || rc.OriginState == destination)
                && (rc.DestinationKind == PlaceKind.Any || rc.DestinationKind == PlaceKind.Zone || rc.DestinationState == origin));

        var contracts = db.Contracts.AsNoTracking().AsSplitQuery()
            .Include(c => c.RateCards.AsQueryable().Where(plausible))
            .Include(c => c.DphRules)
            .Include(c => c.Accessorials)
            .Include(c => c.Slas);

        List<Contract> found;
        if (contractId is { } id)
        {
            found = await contracts.Where(c => c.Id == id).ToListAsync(cancellationToken);
        }
        else
        {
            var date = input.Date;
            var approved = contracts.Where(c => c.Status == ContractStatus.Active || c.Status == ContractStatus.Terminated || c.Status == ContractStatus.Superseded
                                                || c.Status == ContractStatus.Expired || c.Status == ContractStatus.Suspended);
            // A named transporter's other approved contracts are loaded too, so the result can say a contract ended or has not started; anonymous requests stay on the date.
            found = input.TransporterId is { } transporter
                ? await approved.Where(c => c.TransporterId == transporter).OrderByDescending(c => c.EffectiveTo).Take(MaxContracts).ToListAsync(cancellationToken)
                : await approved.Where(c => c.EffectiveFrom <= date && c.EffectiveTo >= date).Take(MaxContracts).ToListAsync(cancellationToken);
        }

        var zones = (await db.Zones.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(z => z.Code);
        var diesel = await db.DieselPrices.AsNoTracking().ToListAsync(cancellationToken);
        var ruleIds = found.SelectMany(c => c.DphRules).Select(r => r.Id).ToList();
        var snapshots = ruleIds.Count == 0
            ? new Dictionary<(Guid, DateOnly), DphPeriodSnapshot>()
            : (await db.DphSnapshots.AsNoTracking().Where(s => ruleIds.Contains(s.RuleId)).ToListAsync(cancellationToken)).ToDictionary(s => (s.RuleId, s.PeriodStart));

        return new Candidates(found, new RatingContext(code => zones.GetValueOrDefault(code), diesel, snapshots, RequireInForce: !preview));
    }
}
