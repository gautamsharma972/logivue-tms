using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application.Contracts;

/// <summary>Loads a contract and the names shown beside it. Every contract handler starts here.</summary>
internal sealed class ContractLoader(
    ContractsDbContext db,
    ContractRateChecker checker,
    ContractAccess access,
    ITransporterDirectory transporters,
    IUserDirectory users,
    TimeProvider clock)
{
    public async Task<Result<Contract>> FindAsync(Guid id, bool write, bool withRates, CancellationToken cancellationToken)
    {
        if (write ? !access.CanManage : !access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var query = db.Contracts.AsQueryable();
        if (withRates)
        {
            query = query.Include(c => c.RateCards);
        }

        return await query.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) is { } contract ? contract : ContractAccess.NotFound;
    }

    public async Task<ContractDto> ToDtoAsync(Contract contract, CancellationToken cancellationToken)
    {
        var transporter = await transporters.GetAsync([contract.TransporterId], cancellationToken);
        var owner = contract.OwnerUserId is { } o ? await users.GetDisplayNamesAsync([o], cancellationToken) : null;
        var detail = await db.Contracts.AsNoTracking().AsSplitQuery()
            .Include(c => c.RateCards).Include(c => c.DphRules).Include(c => c.Accessorials).Include(c => c.Capacities).Include(c => c.Slas)
            .FirstAsync(c => c.Id == contract.Id, cancellationToken);
        var validation = detail.Status is ContractStatus.Draft or ContractStatus.Rejected ? await checker.CheckAsync(detail, cancellationToken) : null;
        return detail.ToDto(
            transporter.TryGetValue(contract.TransporterId, out var t) ? t.LegalName : "Unknown transporter",
            contract.OwnerUserId is { } id && owner is not null && owner.TryGetValue(id, out var name) ? name : null,
            clock.TodayInIndia(), validation);
    }

    public async Task<IReadOnlyList<ContractSummaryDto>> ToSummariesAsync(IReadOnlyList<Contract> contracts, CancellationToken cancellationToken)
    {
        var names = await transporters.GetAsync(contracts.Select(c => c.TransporterId), cancellationToken);
        var today = clock.TodayInIndia();
        var ids = contracts.Select(c => c.Id).ToList();
        var horizon = today.AddDays(60);
        var expiring = await db.RateCards.AsNoTracking().Where(r => ids.Contains(r.ContractId) && r.ValidTo != null && r.ValidTo >= today && r.ValidTo <= horizon)
            .GroupBy(r => r.ContractId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var vehicles = (await db.Capacities.AsNoTracking().Where(c => ids.Contains(c.ContractId)).Select(c => new { c.ContractId, c.Spec }).ToListAsync(cancellationToken))
            .GroupBy(c => c.ContractId).ToDictionary(g => g.Key, g => g.Sum(c => c.Spec.CommittedVehicleCount));
        var revisions = (await db.Contracts.AsNoTracking().Where(c => c.RevisionOfId != null && ids.Contains(c.RevisionOfId.Value)).Select(c => new { Of = c.RevisionOfId!.Value, c.Status, c.RevisionKind }).ToListAsync(cancellationToken))
            .ToLookup(c => c.Of);

        string? RenewalState(Contract c)
        {
            var next = revisions[c.Id].ToList();
            if (next.Any(r => r.RevisionKind == RevisionKind.Renewal && r.Status == ContractStatus.Active))
            {
                return "Renewed";
            }

            if (next.Any(r => r.Status is ContractStatus.Draft or ContractStatus.PendingApproval or ContractStatus.Rejected))
            {
                return "RenewalInProgress";
            }

            return c.Status == ContractStatus.Active && c.DaysUntilExpiry(today) is { } left
                ? left < 0 ? "Expired" : left <= c.RenewalNoticeDays ? "RenewalDue" : left <= 90 ? "ExpiringSoon" : null
                : null;
        }

        return contracts.Select(c => c.ToSummary(
            names.TryGetValue(c.TransporterId, out var t) ? t.LegalName : "Unknown transporter", today,
            new Mapping.SummaryExtras(c.IsInForce(today) ? c.RateCount : 0, expiring.GetValueOrDefault(c.Id), vehicles.TryGetValue(c.Id, out var v) ? v : null, RenewalState(c)))).ToList();
    }
}

internal sealed class ListContractsHandler(ContractsDbContext db, ContractAccess access, ContractLoader loader, TimeProvider clock)
{
    public async Task<Result<PagedResult<ContractSummaryDto>>> HandleAsync(ListContractsQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var today = clock.TodayInIndia();
        var contracts = db.Contracts.AsNoTracking().AsQueryable();

        if (query.Status is { } status)
        {
            // "Expired" and "Active" are decided by the end date, not only by the stored flag the nightly job maintains.
            contracts = status switch
            {
                ContractStatus.Expired => contracts.Where(c => c.Status == ContractStatus.Expired || (c.Status == ContractStatus.Active && c.EffectiveTo < today)),
                ContractStatus.Active => contracts.Where(c => c.Status == ContractStatus.Active && c.EffectiveTo >= today),
                _ => contracts.Where(c => c.Status == status),
            };
        }

        if (query.Type is { } type)
        {
            contracts = contracts.Where(c => c.Type == type);
        }

        if (query.TransporterId is { } transporterId)
        {
            contracts = contracts.Where(c => c.TransporterId == transporterId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            contracts = contracts.Where(c => c.Number.Contains(term) || c.Title.Contains(term));
        }

        var page = await contracts.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<ContractSummaryDto>(await loader.ToSummariesAsync(page.Items, cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

/// <summary>Active contracts ending soon (or already past their end date but not yet marked), soonest first.</summary>
internal sealed class ExpiringContractsHandler(ContractsDbContext db, ContractAccess access, ContractLoader loader, TimeProvider clock)
{
    public async Task<Result<PagedResult<ContractSummaryDto>>> HandleAsync(ExpiringQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var horizon = clock.TodayInIndia().AddDays(Math.Clamp(query.WithinDays, 0, 365));
        var page = await db.Contracts.AsNoTracking()
            .Where(c => c.Status == ContractStatus.Active && c.EffectiveTo <= horizon)
            .OrderBy(c => c.EffectiveTo).ThenBy(c => c.Id)
            .ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<ContractSummaryDto>(await loader.ToSummariesAsync(page.Items, cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetContractHandler(ContractLoader loader)
{
    public async Task<Result<ContractDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: false, withRates: false, cancellationToken);
        return found.IsFailure ? found.Error : await loader.ToDtoAsync(found.Value, cancellationToken);
    }
}

internal sealed class GetRatesHandler(ContractsDbContext db, ContractLoader loader, IVehicleTypeDirectory vehicleTypes)
{
    public async Task<Result<IReadOnlyList<RateCardDto>>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: false, withRates: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var cards = await db.RateCards.AsNoTracking().Where(r => r.ContractId == id).ToListAsync(cancellationToken);
        var types = await vehicleTypes.GetAsync(cards.Where(c => c.VehicleTypeId.HasValue).Select(c => c.VehicleTypeId!.Value), cancellationToken);
        return Result.Success<IReadOnlyList<RateCardDto>>(
            cards.OrderBy(c => c.OriginKind == PlaceKind.Any ? 1 : 0).ThenBy(c => c.OriginState).ThenBy(c => c.OriginCity)
                 .ThenBy(c => c.DestinationState).ThenBy(c => c.DestinationCity).ThenBy(c => c.MinDistanceKm)
                 .Select(c => c.ToDto(types)).ToList());
    }
}

internal sealed class CreateContractHandler(
    ContractsDbContext db,
    ICurrentUser currentUser,
    ContractAccess access,
    ITransporterDirectory transporters,
    NumberSequence sequence,
    ContractLoader loader)
{
    public async Task<Result<ContractDto>> HandleAsync(SaveContractRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        var transporter = (await transporters.GetAsync([request.TransporterId], cancellationToken)).GetValueOrDefault(request.TransporterId);
        if (transporter is null)
        {
            return Error.Validation("contracts.transporter_unknown", "The selected transporter does not exist.");
        }

        if (!transporter.IsActive)
        {
            return Error.Validation("contracts.transporter_inactive", "Contracts can only be created with an active transporter.");
        }

        var tenantId = currentUser.TenantId!.Value;
        var number = await sequence.NextAsync(tenantId, "contract", cancellationToken);
        var created = Contract.Create(
            tenantId, $"CN-{number:D5}", request.TransporterId, request.Type, request.Title, request.EffectiveFrom, request.EffectiveTo,
            request.PaymentTermsDays, request.EstimatedAnnualSpend, request.OwnerUserId ?? currentUser.UserId, request.Terms, request.Fuel);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (request.Extras is { } extras && created.Value.ApplyExtras(extras) is { IsFailure: true } extrasFailed)
        {
            return extrasFailed.Error;
        }

        db.Contracts.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(created.Value, cancellationToken);
    }
}

internal sealed class UpdateContractHandler(ContractsDbContext db, ContractLoader loader)
{
    public async Task<Result<ContractDto>> HandleAsync(Guid id, SaveContractRequest request, CancellationToken cancellationToken)
    {
        if (request.Version is null)
        {
            return Error.Validation("contracts.version_required", "The current version of the contract is required.");
        }

        var found = await loader.FindAsync(id, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contract = found.Value;
        db.Entry(contract).Property(c => c.Version).OriginalValue = request.Version.Value;
        var updated = contract.UpdateHeader(
            request.Title, request.EffectiveFrom, request.EffectiveTo, request.PaymentTermsDays, request.EstimatedAnnualSpend,
            request.OwnerUserId, request.Terms, request.Fuel);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (request.Extras is { } extras && contract.ApplyExtras(extras) is { IsFailure: true } extrasFailed)
        {
            return extrasFailed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(contract, cancellationToken);
    }
}

internal sealed class ReplaceRatesHandler(ContractsDbContext db, ContractLoader loader, IVehicleTypeDirectory vehicleTypes)
{
    public async Task<Result<ContractDto>> HandleAsync(Guid id, SaveRatesRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var specs = new List<RateCardSpec>(request.Rates.Count);
        for (var i = 0; i < request.Rates.Count; i++)
        {
            var rate = request.Rates[i];
            var origin = rate.Origin.ToPlace();
            var destination = rate.Destination.ToPlace();
            if (origin.IsFailure || destination.IsFailure)
            {
                return RatesError($"Rate {i + 1}: {(origin.IsFailure ? origin : destination).Error.Description}");
            }

            specs.Add(new RateCardSpec(origin.Value, destination.Value, rate.BothWays, rate.VehicleTypeId, rate.MinDistanceKm, rate.MaxDistanceKm, rate.Pricing, rate.Extras));
        }

        var typeIds = specs.Where(s => s.VehicleTypeId.HasValue).Select(s => s.VehicleTypeId!.Value).Distinct().ToList();
        var types = await vehicleTypes.GetAsync(typeIds, cancellationToken);
        if (typeIds.FirstOrDefault(t => !types.ContainsKey(t)) is var missing && missing != Guid.Empty)
        {
            return RatesError("One of the rates refers to a vehicle type that does not exist.");
        }

        var zoneCodes = specs.SelectMany(s => new[] { s.Origin, s.Destination }).Where(p => p.Kind == PlaceKind.Zone).Select(p => p.ZoneCode!).Distinct().ToList();
        var known = await db.Zones.AsNoTracking().Where(z => zoneCodes.Contains(z.Code)).Select(z => z.Code).ToListAsync(cancellationToken);
        if (zoneCodes.FirstOrDefault(z => !known.Contains(z)) is { } unknownZone)
        {
            return RatesError($"Zone {unknownZone} does not exist. Define it under Zones first.");
        }

        var contract = found.Value;
        db.Entry(contract).Property(c => c.Version).OriginalValue = request.Version;
        await db.Entry(contract).Collection(c => c.DphRules).LoadAsync(cancellationToken);
        var before = contract.RevisionOfId is { } previousId
            ? await db.RateCards.AsNoTracking().Where(r => r.ContractId == previousId).ToDictionaryAsync(r => r.Code, cancellationToken)
            : null;
        var replaced = contract.ReplaceRates(specs, before);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(contract, cancellationToken);

        static Error RatesError(string message) =>
            Error.Validation("contracts.rates_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { ["rates"] = [message] } };
    }
}

/// <summary>Sends a completed draft for approval through the approval engine; the matrix may key on the estimated annual spend.</summary>
internal sealed class SubmitContractHandler(
    ContractsDbContext db,
    ContractLoader loader,
    ContractRateChecker checker,
    ITransporterDirectory transporters,
    IApprovalGateway approvals,
    TimeProvider clock)
{
    public const string DocumentType = "freight_contract";

    public async Task<Result<ContractDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var contract = found.Value;
        if (contract.Status is not (ContractStatus.Draft or ContractStatus.Rejected))
        {
            return Error.Conflict("contracts.not_submittable", "Only a draft or rejected contract can be submitted for approval.");
        }

        await db.Entry(contract).Collection(c => c.DphRules).LoadAsync(cancellationToken);
        var validation = await checker.CheckAsync(contract, cancellationToken);
        if (!validation.IsValid)
        {
            var messages = validation.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Row is { } r ? $"Rate {r}: {i.Message}" : i.Message).Take(25).ToArray();
            return Error.Validation("contracts.rates_blocked", $"The rates have {validation.Errors} problem(s) that must be fixed before approval.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["rates"] = messages },
            };
        }

        var missing = contract.MissingForSubmission(clock.TodayInIndia());
        if (missing.Count > 0)
        {
            return Error.Validation("contracts.incomplete", "Complete the contract before submitting it.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["requirements"] = [.. missing] },
            };
        }

        var transporter = (await transporters.GetAsync([contract.TransporterId], cancellationToken)).GetValueOrDefault(contract.TransporterId);
        if (transporter is not { IsActive: true })
        {
            return Error.Conflict("contracts.transporter_inactive", "The transporter is not active, so the contract cannot be approved.");
        }

        var submitted = await approvals.SubmitAsync(
            new SubmitApproval(DocumentType, contract.Id, $"Contract {contract.Reference} · {transporter.LegalName}", contract.EstimatedAnnualSpend), cancellationToken);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var marked = contract.MarkSubmitted(submitted.Value.RequestId, submitted.Value.Status, clock.GetUtcNow());
        if (marked.IsFailure)
        {
            return marked.Error;
        }

        await db.SwitchOverAsync(contract, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(contract, cancellationToken);
    }
}

internal sealed class TerminateContractHandler(ContractsDbContext db, ContractLoader loader, TimeProvider clock)
{
    public async Task<Result<ContractDto>> HandleAsync(Guid id, TerminateRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, withRates: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var terminated = found.Value.Terminate(request.Reason, clock.TodayInIndia());
        if (terminated.IsFailure)
        {
            return terminated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(found.Value, cancellationToken);
    }
}

internal sealed class ReviseContractHandler(ContractsDbContext db, ContractLoader loader, ICurrentUser currentUser)
{
    public async Task<Result<ContractDto>> HandleAsync(Guid id, ReviseRequest request, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(id, write: true, withRates: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (await db.Contracts.AnyAsync(c => c.RevisionOfId == id && (c.Status == ContractStatus.Draft || c.Status == ContractStatus.PendingApproval || c.Status == ContractStatus.Rejected), cancellationToken))
        {
            return Error.Conflict("contracts.revision_in_progress", "A revision of this contract is already being prepared.");
        }

        await db.Entry(found.Value).Collection(c => c.DphRules).LoadAsync(cancellationToken);
        await db.Entry(found.Value).Collection(c => c.Accessorials).LoadAsync(cancellationToken);
        await db.Entry(found.Value).Collection(c => c.Capacities).LoadAsync(cancellationToken);
        await db.Entry(found.Value).Collection(c => c.Slas).LoadAsync(cancellationToken);
        var revision = found.Value.CreateRevision(request.EffectiveFrom, request.EffectiveTo, currentUser.UserId, request.Kind ?? RevisionKind.Amendment);
        if (revision.IsFailure)
        {
            return revision.Error;
        }

        db.Contracts.Add(revision.Value);
        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(revision.Value, cancellationToken);
    }
}
