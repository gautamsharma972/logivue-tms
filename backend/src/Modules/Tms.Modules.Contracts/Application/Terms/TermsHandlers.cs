using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Application.Contracts;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application.Terms;

/// <summary>The commercial terms that sit beside the rates: DPH rules, extra charges, capacity commitments and service levels. Edited only while a contract is a draft.</summary>
internal sealed class ContractTermsHandler(ContractsDbContext db, ContractLoader loader, IVehicleTypeDirectory vehicleTypes)
{
    public async Task<Result<IReadOnlyList<DphRuleSpec>>> GetDphAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await Load(id, write: false, cancellationToken);
        return found.IsFailure ? found.Error : Result.Success<IReadOnlyList<DphRuleSpec>>(found.Value.DphRules.OrderBy(r => r.Code).ThenBy(r => r.Version).Select(r => r.Spec).ToList());
    }

    public async Task<Result<ContractDto>> SaveDphAsync(Guid id, SaveDphRulesRequest request, CancellationToken cancellationToken) =>
        await SaveAsync(id, request.Version, c => c.ReplaceDphRules(request.Rules), cancellationToken);

    public async Task<Result<IReadOnlyList<AccessorialSpec>>> GetAccessorialsAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await Load(id, write: false, cancellationToken);
        return found.IsFailure ? found.Error : Result.Success<IReadOnlyList<AccessorialSpec>>(found.Value.Accessorials.Select(a => a.Spec).OrderBy(a => a.Code).ThenBy(a => a.ValidFrom).ToList());
    }

    public async Task<Result<ContractDto>> SaveAccessorialsAsync(Guid id, SaveAccessorialsRequest request, CancellationToken cancellationToken)
    {
        // A charge must be one the tenant has in its catalogue and has not switched off.
        var known = await db.AccessorialTypes.AsNoTracking().Where(a => a.IsActive).Select(a => a.Code).ToListAsync(cancellationToken);
        if (known.Count > 0 && request.Charges.FirstOrDefault(c => !known.Contains(c.Code.Trim().ToUpperInvariant())) is { } unknown)
        {
            return Error.Validation("contracts.accessorial_unknown", $"{unknown.Code} is not in the extra-charge catalogue. Add it under Extra charges first.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["accessorials"] = [$"{unknown.Code} is not in the extra-charge catalogue."] },
            };
        }

        return await SaveAsync(id, request.Version, c => c.ReplaceAccessorials(request.Charges), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<CapacityDto>>> GetCapacityAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await Load(id, write: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var names = await vehicleTypes.GetAsync(found.Value.Capacities.Where(c => c.Spec.VehicleTypeId.HasValue).Select(c => c.Spec.VehicleTypeId!.Value), cancellationToken);
        return Result.Success<IReadOnlyList<CapacityDto>>(found.Value.Capacities.Select(c => new CapacityDto(c.Id, c.Spec, c.Spec.VehicleTypeId is { } v && names.TryGetValue(v, out var n) ? n.Name : null)).ToList());
    }

    public async Task<Result<ContractDto>> SaveCapacityAsync(Guid id, SaveCapacityRequest request, CancellationToken cancellationToken)
    {
        var typeIds = request.Commitments.Where(c => c.VehicleTypeId.HasValue).Select(c => c.VehicleTypeId!.Value).Distinct().ToList();
        var types = await vehicleTypes.GetAsync(typeIds, cancellationToken);
        if (typeIds.Any(t => !types.ContainsKey(t)))
        {
            return Error.Validation("contracts.capacity_invalid", "A commitment refers to a vehicle type that does not exist.");
        }

        return await SaveAsync(id, request.Version, c => c.ReplaceCapacities(request.Commitments), cancellationToken);
    }

    public async Task<Result<IReadOnlyList<SlaSpec>>> GetSlaAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await Load(id, write: false, cancellationToken);
        return found.IsFailure ? found.Error : Result.Success<IReadOnlyList<SlaSpec>>(found.Value.Slas.Select(s => s.Spec).ToList());
    }

    public async Task<Result<ContractDto>> SaveSlaAsync(Guid id, SaveSlaRequest request, CancellationToken cancellationToken)
    {
        var zoneCodes = request.Levels.SelectMany(l => new[] { l.Origin, l.Destination }).Where(p => p is { Kind: PlaceKind.Zone }).Select(p => p!.ZoneCode!).Distinct().ToList();
        var known = await db.Zones.AsNoTracking().Where(z => zoneCodes.Contains(z.Code)).Select(z => z.Code).ToListAsync(cancellationToken);
        if (zoneCodes.FirstOrDefault(z => !known.Contains(z)) is { } missing)
        {
            return Error.Validation("contracts.sla_invalid", $"Zone {missing} does not exist. Define it under Zones first.");
        }

        return await SaveAsync(id, request.Version, c => c.ReplaceSlas(request.Levels), cancellationToken);
    }

    private async Task<Result<Contract>> Load(Guid id, bool write, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contract = found.Value;
        await db.Entry(contract).Collection(c => c.DphRules).LoadAsync(cancellationToken);
        await db.Entry(contract).Collection(c => c.Accessorials).LoadAsync(cancellationToken);
        await db.Entry(contract).Collection(c => c.Capacities).LoadAsync(cancellationToken);
        await db.Entry(contract).Collection(c => c.Slas).LoadAsync(cancellationToken);
        return contract;
    }

    private async Task<Result<ContractDto>> SaveAsync(Guid id, long version, Func<Contract, Result> change, CancellationToken cancellationToken)
    {
        var found = await Load(id, write: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        db.Entry(found.Value).Property(c => c.Version).OriginalValue = version;
        var changed = change(found.Value);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(found.Value, cancellationToken);
    }
}

/// <summary>DPH across contracts: what each rule says now against today's diesel price, a calculator for any rule, and recording a period's price so an adjustment can be reproduced.</summary>
internal sealed class DphHandler(ContractsDbContext db, ContractAccess access, ContractLoader loader, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<DphOverviewDto>>> ListAsync(Guid? contractId, bool? inForceOnly, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var today = clock.TodayInIndia();
        var rules = db.DphRules.AsNoTracking().AsQueryable();
        if (contractId is { } cid)
        {
            rules = rules.Where(r => r.ContractId == cid);
        }

        var list = await rules.Take(2_000).ToListAsync(cancellationToken);
        var contractIds = list.Select(r => r.ContractId).Distinct().ToList();
        var contracts = await db.Contracts.AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var prices = await db.DieselPrices.AsNoTracking().ToListAsync(cancellationToken);
        var snapshots = (await db.DphSnapshots.AsNoTracking().Select(s => new { s.RuleId, s.PeriodStart }).ToListAsync(cancellationToken)).Select(s => (s.RuleId, s.PeriodStart)).ToHashSet();

        var rows = new List<DphOverviewDto>();
        foreach (var rule in list.OrderBy(r => r.ContractId).ThenBy(r => r.Code).ThenBy(r => r.Version))
        {
            if (!contracts.TryGetValue(rule.ContractId, out var contract))
            {
                continue;
            }

            var from = rule.Spec.EffectiveFrom ?? contract.EffectiveFrom;
            var to = rule.Spec.EffectiveTo ?? contract.EffectiveTo;
            var live = from <= today && today <= to && contract.IsInForce(today);
            if (inForceOnly == true && !live)
            {
                continue;
            }

            var dto = new DphRuleDto(rule.Id, contract.Id, contract.Number, contract.Revision, rule.Code, rule.Version, rule.Spec, from, to, live);
            var period = DphCalculator.ReferenceDate(rule.Spec.Frequency, today);
            var price = DieselPrice.Resolve(prices, rule.Spec.Region, period);
            decimal? variation = null;
            decimal? adjustment = null;
            var due = false;
            if (price is { } p)
            {
                var result = DphCalculator.Calculate(rule.Spec, p, period, 100m, null);
                variation = result.VariationPercent;
                adjustment = result.Applied ? result.AdjustmentPercent : 0m;
                due = live && result.Applied && !snapshots.Contains((rule.Id, period));
            }

            rows.Add(new DphOverviewDto(dto, price, variation, adjustment, price is null ? null : prices.Where(x => x.Region == Text.Normalise(rule.Spec.Region) && x.EffectiveFrom <= period).Max(x => (DateOnly?)x.EffectiveFrom), due));
        }

        return Result.Success<IReadOnlyList<DphOverviewDto>>(rows);
    }

    /// <summary>Adds a rule (or a new version of a code, with a later start) to a draft contract.</summary>
    public async Task<Result<ContractDto>> CreateAsync(SaveDphRuleRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(request.ContractId, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contract = found.Value;
        await db.Entry(contract).Collection(c => c.DphRules).LoadAsync(cancellationToken);
        db.Entry(contract).Property(c => c.Version).OriginalValue = request.Version;
        var replaced = contract.ReplaceDphRules([.. contract.DphRules.Select(r => r.Spec), request.Rule]);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(contract, cancellationToken);
    }

    public async Task<Result<ContractDto>> UpdateAsync(Guid ruleId, SaveDphRuleRequest request, CancellationToken cancellationToken)
    {
        var rule = await db.DphRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return Error.NotFound("dph.not_found", "DPH rule not found.");
        }

        var found = await loader.FindAsync(rule.ContractId, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contract = found.Value;
        await db.Entry(contract).Collection(c => c.DphRules).LoadAsync(cancellationToken);
        db.Entry(contract).Property(c => c.Version).OriginalValue = request.Version;
        var replaced = contract.ReplaceDphRules(contract.DphRules.Select(r => r.Id == ruleId ? request.Rule : r.Spec).ToList());
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(contract, cancellationToken);
    }

    public async Task<Result<DphCalculationDto>> CalculateAsync(Guid ruleId, DphCalculateRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanRead || (request.Record && !access.CanManage))
        {
            return ContractAccess.Forbidden;
        }

        var rule = await db.DphRules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return Error.NotFound("dph.not_found", "DPH rule not found.");
        }

        if (request.BaseFreight <= 0)
        {
            return Error.Validation("dph.freight_required", "Give the freight to work the adjustment out on.");
        }

        var date = request.Date ?? clock.TodayInIndia();
        var period = DphCalculator.ReferenceDate(rule.Spec.Frequency, date);
        var existing = await db.DphSnapshots.FirstOrDefaultAsync(s => s.RuleId == ruleId && s.PeriodStart == period, cancellationToken);
        var price = request.OverridePrice ?? existing?.ReferencePrice ?? DieselPrice.Resolve(await db.DieselPrices.AsNoTracking().ToListAsync(cancellationToken), rule.Spec.Region, period);
        if (price is null)
        {
            return Error.Conflict("dph.no_price", $"There is no diesel price on file for {rule.Spec.Region} on or before {period:dd MMM yyyy}.");
        }

        var result = DphCalculator.Calculate(rule.Spec, price.Value, period, request.BaseFreight, request.DistanceKm);
        var recorded = existing is not null;
        if (request.Record && existing is null && request.OverridePrice is null)
        {
            var contract = await db.Contracts.FirstAsync(c => c.Id == rule.ContractId, cancellationToken);
            db.DphSnapshots.Add(DphPeriodSnapshot.Create(rule.TenantId, rule, period, price.Value, result.VariationPercent, result.Applied ? result.AdjustmentPercent : 0m, RatingEngine.Version));
            contract.AnnounceDphRevision(rule.Code, period, price.Value, result.Applied ? result.AdjustmentPercent : 0m);
            await db.SaveChangesAsync(cancellationToken);
            recorded = true;
        }

        return new DphCalculationDto(rule.Id, rule.Code, rule.Version, period, price, rule.Spec.BaseDieselPrice, result.VariationPercent, result.AdjustmentPercent, result.Amount, result.Applied, result.Explanation, recorded);
    }

    public async Task<Result<IReadOnlyList<PriceIndexDto>>> ListPricesAsync(string? region, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var prices = db.DieselPrices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(region))
        {
            var normal = region.Trim().ToUpperInvariant();
            prices = prices.Where(p => p.Region == normal);
        }

        var list = await prices.OrderBy(p => p.Region).ThenByDescending(p => p.EffectiveFrom).Take(1_000).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<PriceIndexDto>>(list.Select(p => new PriceIndexDto(p.Id, p.Region, p.EffectiveFrom, p.PricePerLitre, "INR", "LITRE", p.Source)).ToList());
    }
}

/// <summary>The catalogue of extra charges a tenant uses. The standard set is offered the first time it is needed; tenants can add their own, change them or switch them off.</summary>
internal sealed class AccessorialTypeHandler(ContractsDbContext db, ContractAccess access, ICurrentUser currentUser)
{
    public async Task<Result<IReadOnlyList<AccessorialTypeDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        if (!await db.AccessorialTypes.AnyAsync(cancellationToken) && access.CanManage && currentUser.TenantId is { } tenant)
        {
            foreach (var (code, name, calc, unit) in AccessorialType.Standard)
            {
                db.AccessorialTypes.Add(AccessorialType.Create(tenant, code, name, null, calc, unit).Value);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var list = await db.AccessorialTypes.AsNoTracking().OrderBy(a => a.Name).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<AccessorialTypeDto>>(list.Select(ToDto).ToList());
    }

    public async Task<Result<AccessorialTypeDto>> CreateAsync(SaveAccessorialTypeRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage || currentUser.TenantId is not { } tenant)
        {
            return ContractAccess.Forbidden;
        }

        var created = AccessorialType.Create(tenant, request.Code, request.Name, request.Description, request.Calc, request.Unit);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (await db.AccessorialTypes.AnyAsync(a => a.Code == created.Value.Code, cancellationToken))
        {
            return Error.Conflict("accessorials.duplicate", $"{created.Value.Code} already exists.");
        }

        db.AccessorialTypes.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(created.Value);
    }

    public async Task<Result<AccessorialTypeDto>> UpdateAsync(Guid id, SaveAccessorialTypeRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        var type = await db.AccessorialTypes.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (type is null)
        {
            return Error.NotFound("accessorials.not_found", "Extra charge not found.");
        }

        var updated = type.Update(request.Name, request.Description, request.Calc, request.Unit, request.IsActive);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(type);
    }

    private static AccessorialTypeDto ToDto(AccessorialType a) => new(a.Id, a.Code, a.Name, a.Description, a.Calc, a.Unit, a.IsActive);
}
