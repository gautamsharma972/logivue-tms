using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Application.Contracts;

/// <summary>
/// Prices a shipment against every contract in force on its date and returns the quotes cheapest first, each with an
/// itemised breakdown. This is the single source of freight prices for planning, procurement and bill audit.
/// </summary>
internal sealed class QuoteHandler(ContractsDbContext db, ContractAccess access, ITransporterDirectory transporters, TimeProvider clock)
{
    public async Task<Result<QuoteResultDto>> HandleAsync(QuoteRequest request, CancellationToken cancellationToken) =>
        access.CanRead ? await ExecuteAsync(request, cancellationToken) : ContractAccess.Forbidden;

    /// <summary>The pricing itself, without the caller-permission check. Callers (this handler, <see cref="FreightQuoteService"/>) authorise first.</summary>
    internal async Task<Result<QuoteResultDto>> ExecuteAsync(QuoteRequest request, CancellationToken cancellationToken)
    {
        var date = request.Date ?? clock.TodayInIndia();
        var query = new FreightQuery(date, request.Origin, request.Destination, request.VehicleTypeId, request.Type, request.WeightKg, request.VolumeCbm, request.DistanceKm, request.Drops);

        var contracts = await LoadCandidatesAsync(request, query, cancellationToken);
        if (contracts.IsFailure)
        {
            return contracts.Error;
        }

        var zones = (await db.Zones.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(z => z.Code);
        var diesel = await db.DieselPrices.AsNoTracking().ToListAsync(cancellationToken);
        var picked = RateSelector.BestPerContract(contracts.Value, query, code => zones.GetValueOrDefault(code), requireInForce: !request.Preview);

        var quotes = new List<FreightQuote>();
        var problems = new List<string>();
        foreach (var (contract, card) in picked)
        {
            var price = contract.Fuel is { } fuel ? DieselPrice.Resolve(diesel, fuel.Region, date) : null;
            var quote = FreightCalculator.Calculate(contract, card, query, price);
            if (quote.IsSuccess)
            {
                quotes.Add(quote.Value);
            }
            else
            {
                problems.Add($"{contract.Reference}: {quote.Error.Description}");
            }
        }

        var names = await transporters.GetAsync(quotes.Select(q => q.TransporterId), cancellationToken);
        var dtos = quotes.OrderBy(q => q.Total).ThenBy(q => q.ContractReference, StringComparer.Ordinal)
            .Select(q => new QuoteDto(q.ContractId, q.ContractReference, q.TransporterId,
                names.TryGetValue(q.TransporterId, out var t) ? t.LegalName : "Unknown transporter",
                q.Type, q.Lane, q.ChargeableWeightKg, q.Lines, q.Notes, q.Total))
            .ToList();

        string? message = null;
        if (dtos.Count == 0)
        {
            message = problems.Count > 0
                ? string.Join(" ", problems)
                : "No contract has an applicable rate for this shipment on that date.";
        }
        else if (problems.Count > 0)
        {
            message = string.Join(" ", problems);
        }

        return new QuoteResultDto(date, dtos, message);
    }

    private async Task<Result<List<Contract>>> LoadCandidatesAsync(QuoteRequest request, FreightQuery query, CancellationToken cancellationToken)
    {
        var origin = request.Origin.NormalState;
        var destination = request.Destination.NormalState;

        // Narrow to plausible lanes in SQL (by state / kind); exact matching, zones and scoring happen in memory.
        Expression<Func<RateCard, bool>> plausible = rc =>
            ((rc.OriginKind == PlaceKind.Any || rc.OriginKind == PlaceKind.Zone || rc.OriginState == origin)
             && (rc.DestinationKind == PlaceKind.Any || rc.DestinationKind == PlaceKind.Zone || rc.DestinationState == destination))
            || (rc.BothWays
                && (rc.OriginKind == PlaceKind.Any || rc.OriginKind == PlaceKind.Zone || rc.OriginState == destination)
                && (rc.DestinationKind == PlaceKind.Any || rc.DestinationKind == PlaceKind.Zone || rc.DestinationState == origin));

        var contracts = db.Contracts.AsNoTracking().Include(c => c.RateCards.AsQueryable().Where(plausible));

        if (request.ContractId is { } id)
        {
            var one = await contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            return one is null ? ContractAccess.NotFound : new List<Contract> { one };
        }

        var date = query.Date;
        return await contracts
            .Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Terminated || c.Status == ContractStatus.Superseded || c.Status == ContractStatus.Expired)
                        && c.EffectiveFrom <= date && c.EffectiveTo >= date)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>The price engine as other modules call it (planning, procurement). They authorise their own users.</summary>
internal sealed class FreightQuoteService(QuoteHandler handler) : IFreightQuoteService
{
    public async Task<FreightQuoteSet> QuoteAsync(FreightQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var type = request.Mode switch { FreightMode.Ftl => ContractType.Ftl, FreightMode.Ptl => ContractType.Ptl, _ => (ContractType?)null };
        var result = await handler.ExecuteAsync(
            new QuoteRequest(
                request.Date,
                new Location(request.OriginState, request.OriginCity),
                new Location(request.DestinationState, request.DestinationCity),
                request.VehicleTypeId, type, request.WeightKg, request.VolumeCbm, request.DistanceKm, request.Drops),
            cancellationToken);

        if (result.IsFailure)
        {
            return new FreightQuoteSet([], result.Error.Description);
        }

        var quotes = result.Value.Quotes
            .Where(q => q.Type != ContractType.Dedicated)
            .Select(q => new FreightQuoteResult(
                q.ContractId, q.ContractReference, q.TransporterId, q.TransporterName,
                q.Type == ContractType.Ftl ? FreightMode.Ftl : FreightMode.Ptl,
                q.Lane, q.ChargeableWeightKg,
                q.Lines.Select(l => new FreightQuoteLine(l.Code, l.Description, l.Amount)).ToList(), q.Notes, q.Total))
            .ToList();
        return new FreightQuoteSet(quotes, result.Value.Message);
    }
}
