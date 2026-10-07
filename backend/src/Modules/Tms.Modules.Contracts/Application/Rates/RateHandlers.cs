using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Application.Contracts;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application.Rates;

internal static class RateSummary
{
    public static string Describe(Pricing p) => p switch
    {
        FlatTripPricing f => $"₹{f.AmountPerTrip:#,0.##} per trip",
        PerKmPricing k => $"₹{k.RatePerKm:0.##}/km",
        WeightSlabPricing w => $"{w.Slabs.Count} weight slab(s), ₹{w.Slabs.Min(s => s.RatePerKg):0.##}–₹{w.Slabs.Max(s => s.RatePerKg):0.##}/kg",
        SlabRatePricing s when s.Slabs.Count == 1 => s.Slabs[0].Type == SlabRateType.Fixed ? $"₹{s.Slabs[0].Rate:#,0.##} fixed" : $"₹{s.Slabs[0].Rate:0.####}/{s.Unit.ToString().ToLowerInvariant()}",
        SlabRatePricing s => $"{s.Slabs.Count} {s.Dimension.ToString().ToLowerInvariant()} slabs ({s.Method})",
        DedicatedPricing d => $"₹{d.MonthlyRental:#,0.##} per month",
        _ => "—",
    };

    public static string Kind(Pricing p) => p switch
    {
        FlatTripPricing => "FIXED",
        PerKmPricing => "PER_KM",
        WeightSlabPricing => "WEIGHT_SLABS",
        SlabRatePricing s => s.Method switch { SlabMethod.Progressive => "PROGRESSIVE", SlabMethod.BaseExcess => "BASE_EXCESS", _ => s.Slabs.Count == 1 ? "PER_" + s.Unit.ToString().ToUpperInvariant() : "FLAT_SLAB" },
        DedicatedPricing => "MONTHLY",
        _ => "OTHER",
    };
}

/// <summary>The rate book across contracts: a filterable grid, one rate with its history, validation, adding a rate and making a new version of one.</summary>
internal sealed class RateHandler(ContractsDbContext db, ContractAccess access, ContractLoader loader, ContractRateChecker checker, ITransporterDirectory transporters, IVehicleTypeDirectory vehicleTypes, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result<PagedResult<RateRowDto>>> ListAsync(ListRatesQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var today = clock.TodayInIndia();
        var rows = db.RateCards.AsNoTracking().Join(db.Contracts.AsNoTracking(), r => r.ContractId, c => c.Id, (r, c) => new { Card = r, Contract = c });
        if (query.TransporterId is { } t)
        {
            rows = rows.Where(x => x.Contract.TransporterId == t);
        }

        if (query.ContractId is { } cid)
        {
            rows = rows.Where(x => x.Contract.Id == cid);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(x => x.Contract.Status == status);
        }

        if (query.VehicleTypeId is { } v)
        {
            rows = rows.Where(x => x.Card.VehicleTypeId == v);
        }

        if (!string.IsNullOrWhiteSpace(query.Origin))
        {
            var o = query.Origin.Trim().ToUpperInvariant();
            rows = rows.Where(x => (x.Card.OriginCity != null && x.Card.OriginCity.Contains(o)) || (x.Card.OriginState != null && x.Card.OriginState.Contains(o)) || x.Card.OriginZone == o);
        }

        if (!string.IsNullOrWhiteSpace(query.Destination))
        {
            var d = query.Destination.Trim().ToUpperInvariant();
            rows = rows.Where(x => (x.Card.DestinationCity != null && x.Card.DestinationCity.Contains(d)) || (x.Card.DestinationState != null && x.Card.DestinationState.Contains(d)) || x.Card.DestinationZone == d);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(x => x.Card.Code.Contains(term) || x.Contract.Number.Contains(term) || x.Contract.Title.Contains(term));
        }

        if (query.InForceOnly == true)
        {
            rows = rows.Where(x => x.Contract.Status == ContractStatus.Active && x.Contract.EffectiveFrom <= today && x.Contract.EffectiveTo >= today
                                   && (x.Card.ValidTo == null || x.Card.ValidTo >= today) && (x.Card.ValidFrom == null || x.Card.ValidFrom <= today));
        }

        if (query.ExpiringWithinDays is { } days)
        {
            var horizon = today.AddDays(Math.Clamp(days, 0, 365));
            rows = rows.Where(x => x.Contract.Status == ContractStatus.Active && (x.Card.ValidTo ?? x.Contract.EffectiveTo) >= today && (x.Card.ValidTo ?? x.Contract.EffectiveTo) <= horizon);
        }

        var ordered = (query.SortBy?.ToLowerInvariant(), query.Descending) switch
        {
            ("priority", false) => rows.OrderBy(x => x.Card.Priority).ThenBy(x => x.Card.Code),
            ("priority", true) => rows.OrderByDescending(x => x.Card.Priority).ThenBy(x => x.Card.Code),
            ("validfrom", false) => rows.OrderBy(x => x.Card.ValidFrom).ThenBy(x => x.Card.Code),
            ("validfrom", true) => rows.OrderByDescending(x => x.Card.ValidFrom).ThenBy(x => x.Card.Code),
            ("code", true) => rows.OrderByDescending(x => x.Card.Code),
            ("code", false) => rows.OrderBy(x => x.Card.Code),
            (_, true) => rows.OrderByDescending(x => x.Contract.Number).ThenBy(x => x.Card.Code),
            _ => rows.OrderBy(x => x.Contract.Number).ThenBy(x => x.Card.Code),
        };
        var page = await ordered.ToPagedAsync(query.Page, Math.Clamp(query.PageSize, 1, 500), cancellationToken);
        var names = await transporters.GetAsync(page.Items.Select(x => x.Contract.TransporterId), cancellationToken);
        var types = await vehicleTypes.GetAsync(page.Items.Where(x => x.Card.VehicleTypeId.HasValue).Select(x => x.Card.VehicleTypeId!.Value), cancellationToken);
        var items = page.Items.Select(x => ToRow(x.Card, x.Contract, names, types, today)).ToList();
        return new PagedResult<RateRowDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    internal static RateRowDto ToRow(RateCard r, Contract c, IReadOnlyDictionary<Guid, TransporterInfo> names, IReadOnlyDictionary<Guid, VehicleTypeInfo> types, DateOnly today)
    {
        var from = r.ValidFrom ?? c.EffectiveFrom;
        var to = r.ValidTo ?? c.EffectiveTo;
        var inForce = c.IsInForce(today) && from <= today && today <= to;
        return new RateRowDto(
            r.Id, c.Id, c.Number, c.Revision, c.Status, c.TransporterId, names.TryGetValue(c.TransporterId, out var n) ? n.LegalName : "Unknown transporter", r.Code, r.Version, r.Pricing.ServiceType().ToString(),
            $"{r.Origin} → {r.Destination}", r.Origin.ToDto(), r.Destination.ToDto(), r.VehicleTypeId, r.VehicleTypeId is { } vt && types.TryGetValue(vt, out var t) ? t.Name : null,
            r.MinWeightKg, r.MaxWeightKg, r.MinDistanceKm, r.MaxDistanceKm, r.MinVolumeCbm, r.MaxVolumeCbm, RateSummary.Kind(r.Pricing), RateSummary.Describe(r.Pricing), r.MinimumCharge, r.MaximumCharge,
            r.DphRuleCode, r.Priority, from, to, inForce, c.Status == ContractStatus.Active && to >= today && to <= today.AddDays(60));
    }

    public async Task<Result<RateRowDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var card = await db.RateCards.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var contract = card is null ? null : await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == card.ContractId, cancellationToken);
        if (card is null || contract is null)
        {
            return Error.NotFound("rates.not_found", "Rate not found.");
        }

        var names = await transporters.GetAsync([contract.TransporterId], cancellationToken);
        var types = await vehicleTypes.GetAsync(card.VehicleTypeId is { } v ? [v] : [], cancellationToken);
        return ToRow(card, contract, names, types, clock.TodayInIndia());
    }

    /// <summary>Every version of one rate across the contract's revisions, newest first.</summary>
    public async Task<Result<IReadOnlyList<RateRowDto>>> HistoryAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var card = await db.RateCards.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var contract = card is null ? null : await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == card.ContractId, cancellationToken);
        if (card is null || contract is null)
        {
            return Error.NotFound("rates.not_found", "Rate not found.");
        }

        var chain = await db.Contracts.AsNoTracking().Where(c => c.Number == contract.Number).ToListAsync(cancellationToken);
        var chainIds = chain.Select(c => c.Id).ToList();
        var code = card.Code;
        var versions = await db.RateCards.AsNoTracking().Where(r => chainIds.Contains(r.ContractId) && r.Code == code).ToListAsync(cancellationToken);
        var names = await transporters.GetAsync([contract.TransporterId], cancellationToken);
        var types = await vehicleTypes.GetAsync(card.VehicleTypeId is { } v ? [v] : [], cancellationToken);
        var today = clock.TodayInIndia();
        return Result.Success<IReadOnlyList<RateRowDto>>(versions.Join(chain, r => r.ContractId, c => c.Id, (r, c) => ToRow(r, c, names, types, today)).OrderByDescending(r => r.ContractRevision).ToList());
    }

    public async Task<Result<RateValidationDto>> ValidateAsync(ValidateRatesRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        Contract? contract = null;
        if (request.ContractId is { } cid)
        {
            contract = await db.Contracts.AsNoTracking().Include(c => c.DphRules).FirstOrDefaultAsync(c => c.Id == cid, cancellationToken);
            if (contract is null)
            {
                return ContractAccess.NotFound;
            }
        }

        var from = contract?.EffectiveFrom ?? request.ContractFrom ?? clock.TodayInIndia();
        var to = contract?.EffectiveTo ?? request.ContractTo ?? from.AddYears(1);
        var services = contract?.EffectiveServices ?? request.Services ?? [ContractType.Ftl, ContractType.Ptl, ContractType.Dedicated];
        var dphCodes = contract?.DphRules.Select(r => r.Code.ToUpperInvariant()).ToHashSet() ?? (request.DphCodes ?? []).Select(c => c.Trim().ToUpperInvariant()).ToHashSet();

        var issues = new List<RateIssue>();
        var rows = new List<RateToCheck>();
        var typeIds = request.Rows.Where(r => r.VehicleTypeId.HasValue).Select(r => r.VehicleTypeId!.Value).Distinct().ToList();
        var knownTypes = await vehicleTypes.GetAsync(typeIds, cancellationToken);
        var zoneCodes = request.Rows.SelectMany(r => new[] { r.Origin, r.Destination }).Where(p => p.Kind == PlaceKind.Zone && p.ZoneCode != null).Select(p => p.ZoneCode!.Trim().ToUpperInvariant()).Distinct().ToList();
        var knownZones = (await db.Zones.AsNoTracking().Where(z => zoneCodes.Contains(z.Code)).Select(z => z.Code).ToListAsync(cancellationToken)).ToHashSet();
        for (var i = 0; i < request.Rows.Count; i++)
        {
            var row = request.Rows[i];
            var origin = row.Origin.ToPlace();
            var destination = row.Destination.ToPlace();
            if (origin.IsFailure || destination.IsFailure)
            {
                issues.Add(new RateIssue(IssueSeverity.Error, i + 1, "lane", "RATE_LANE_INVALID", (origin.IsFailure ? origin : destination).Error.Description));
                continue;
            }

            if (row.VehicleTypeId is { } vt && !knownTypes.ContainsKey(vt))
            {
                issues.Add(new RateIssue(IssueSeverity.Error, i + 1, "vehicleType", "RATE_VEHICLE_UNKNOWN", "The vehicle type does not exist."));
            }

            foreach (var place in new[] { origin.Value, destination.Value }.Where(p => p.Kind == PlaceKind.Zone && !knownZones.Contains(p.ZoneCode!)))
            {
                issues.Add(new RateIssue(IssueSeverity.Error, i + 1, "zone", "RATE_ZONE_UNKNOWN", $"Zone {place.ZoneCode} does not exist."));
            }

            var spec = new RateCardSpec(origin.Value, destination.Value, row.BothWays, row.VehicleTypeId, row.MinDistanceKm, row.MaxDistanceKm, row.Pricing, row.Extras);
            var x = row.Extras ?? RateExtras.None;
            rows.Add(new RateToCheck(i + 1, spec, x.ValidFrom ?? from, x.ValidTo ?? to, contract?.Reference ?? "this sheet"));
        }

        var result = await checker.CheckRowsAsync(rows, contract?.TransporterId, contract?.Number, from, to, services, dphCodes, cancellationToken);
        var all = issues.Concat(result.Issues).OrderBy(i => i.Row ?? int.MaxValue).ToList();
        var errors = all.Count(i => i.Severity == IssueSeverity.Error);
        var warnings = all.Count - errors;
        return new RateValidationDto(errors > 0 ? "Invalid" : warnings > 0 ? "Warning" : "Valid", errors, warnings, all.Select(ToDto).ToList());
    }

    public static RateIssueDto ToDto(RateIssue i) => new(i.Severity.ToString(), i.Row, i.Field, i.Code, i.Message);

    public async Task<Result<ContractDto>> CreateAsync(CreateRateRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(request.ContractId, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contract = found.Value;
        await db.Entry(contract).Collection(c => c.DphRules).LoadAsync(cancellationToken);
        db.Entry(contract).Property(c => c.Version).OriginalValue = request.ContractVersion;
        var spec = ToSpec(request.Rate);
        if (spec.IsFailure)
        {
            return spec.Error;
        }

        var replaced = contract.ReplaceRates([.. contract.RateCards.Select(c => c.ToSpec()), spec.Value]);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(contract, cancellationToken);
    }

    /// <summary>
    /// A new version of one rate. A draft is simply edited; an approved contract is never touched: its rate is changed in a draft revision (the one already open, or a new one)
    /// where the rate's version moves up by one, and the old version keeps pricing what it priced until the revision is approved.
    /// </summary>
    public async Task<Result<ContractDto>> CreateVersionAsync(Guid rateId, CreateRateVersionRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        var card = await db.RateCards.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rateId, cancellationToken);
        if (card is null)
        {
            return Error.NotFound("rates.not_found", "Rate not found.");
        }

        var parent = await db.Contracts.AsNoTracking().FirstAsync(c => c.Id == card.ContractId, cancellationToken);
        var today = clock.TodayInIndia();
        Guid targetId;
        if (parent.Status is ContractStatus.Draft or ContractStatus.Rejected)
        {
            targetId = parent.Id;
        }
        else if (await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.RevisionOfId == parent.Id && (c.Status == ContractStatus.Draft || c.Status == ContractStatus.Rejected), cancellationToken) is { } open)
        {
            targetId = open.Id;
        }
        else
        {
            var source = await loader.FindAsync(parent.Id, write: true, withRates: true, cancellationToken);
            if (source.IsFailure)
            {
                return source.Error;
            }

            foreach (var navigation in new[] { "DphRules", "Accessorials", "Capacities", "Slas" })
            {
                await db.Entry(source.Value).Collection(navigation).LoadAsync(cancellationToken);
            }

            var from = request.EffectiveFrom ?? today.AddDays(1);
            var to = request.EffectiveTo ?? (parent.EffectiveTo > from ? parent.EffectiveTo : from.AddYears(1));
            var revision = source.Value.CreateRevision(from, to, currentUser.UserId, RevisionKind.Amendment);
            if (revision.IsFailure)
            {
                return revision.Error;
            }

            db.Contracts.Add(revision.Value);
            await db.SaveChangesAsync(cancellationToken);
            targetId = revision.Value.Id;
        }

        var found = await loader.FindAsync(targetId, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var target = found.Value;
        await db.Entry(target).Collection(c => c.DphRules).LoadAsync(cancellationToken);
        var previous = target.RevisionOfId is { } prev ? await db.RateCards.AsNoTracking().Where(r => r.ContractId == prev).ToDictionaryAsync(r => r.Code, cancellationToken) : null;
        var replacement = ToSpec(request.Rate with { Extras = (request.Rate.Extras ?? RateExtras.None) with { Code = card.Code } });
        if (replacement.IsFailure)
        {
            return replacement.Error;
        }

        var specs = target.RateCards.Select(c => c.Code == card.Code ? replacement.Value : c.ToSpec()).ToList();
        if (!target.RateCards.Any(c => c.Code == card.Code))
        {
            return Error.Conflict("rates.not_in_draft", "That rate is not in the draft revision.");
        }

        var replaced = target.ReplaceRates(specs, previous);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(target, cancellationToken);
    }

    private static Result<RateCardSpec> ToSpec(RateInputDto rate)
    {
        var origin = rate.Origin.ToPlace();
        var destination = rate.Destination.ToPlace();
        return origin.IsFailure ? origin.Error : destination.IsFailure ? destination.Error
            : new RateCardSpec(origin.Value, destination.Value, rate.BothWays, rate.VehicleTypeId, rate.MinDistanceKm, rate.MaxDistanceKm, rate.Pricing, rate.Extras);
    }
}
