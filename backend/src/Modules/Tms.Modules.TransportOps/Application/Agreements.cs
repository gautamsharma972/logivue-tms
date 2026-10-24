using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

public sealed record RateDto(
    Guid? Id, Guid? OriginLocationId, string? OriginCity, string? OriginState, Guid? DestinationLocationId, string? DestinationCity, string? DestinationState, string? Material, RateMethod Method,
    decimal Rate, IReadOnlyList<WeightSlab> Slabs, decimal? MinimumCharge);

public sealed record AgreementDto(
    Guid Id, string Number, int Revision, Guid CustomerId, string CustomerName, string Title, DateOnly EffectiveFrom, DateOnly? EffectiveTo, AgreementStatus Status, WeightBasis WeightBasis,
    decimal? VolumetricKgPerCbm, decimal? MinimumFreight, DetentionPolicy Detention, TaxSettings Tax, BillingRules Billing, IReadOnlyList<ExtraCharge> ExtraCharges, IReadOnlyList<RateDto> Rates,
    DateTimeOffset? ActivatedAt, long Version);

public sealed record AgreementSummaryDto(Guid Id, string Number, int Revision, Guid CustomerId, string CustomerName, string Title, DateOnly EffectiveFrom, DateOnly? EffectiveTo, AgreementStatus Status, int RateCount);

public sealed record SaveAgreementRequest(
    Guid CustomerId, string Title, DateOnly EffectiveFrom, DateOnly? EffectiveTo, WeightBasis WeightBasis, decimal? VolumetricKgPerCbm, decimal? MinimumFreight, DetentionPolicy Detention,
    TaxSettings Tax, BillingRules Billing, IReadOnlyList<ExtraCharge> ExtraCharges, IReadOnlyList<RateDto> Rates, long Version = 0);

public sealed record ReviseAgreementRequest(DateOnly EffectiveFrom);

internal sealed class SaveAgreementRequestValidator : AbstractValidator<SaveAgreementRequest>
{
    public SaveAgreementRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EffectiveTo).GreaterThanOrEqualTo(x => x.EffectiveFrom).When(x => x.EffectiveTo.HasValue).WithMessage("The end date cannot be before the start date.");
        RuleFor(x => x.MinimumFreight).GreaterThanOrEqualTo(0).When(x => x.MinimumFreight.HasValue);
        RuleFor(x => x.VolumetricKgPerCbm).InclusiveBetween(1, 2000).When(x => x.VolumetricKgPerCbm.HasValue);
        RuleFor(x => x.VolumetricKgPerCbm).NotNull().When(x => x.WeightBasis == WeightBasis.Volumetric).WithMessage("A volumetric basis needs the kilograms per cubic metre.");
        RuleFor(x => x.Rates).NotNull();
        RuleForEach(x => x.Rates).SetValidator(new RateValidator());
        RuleFor(x => x.Rates).Must(NoDuplicateScopes).WithMessage("Two rates cover exactly the same lane and material. Keep one, or make one of them more specific.");
        RuleFor(x => x.Detention.Rate).GreaterThan(0).When(x => x.Detention.Applicable).WithMessage("Enter the detention rate.");
        RuleFor(x => x.Detention.FreeAmount).GreaterThanOrEqualTo(0).When(x => x.Detention.Applicable);
        RuleFor(x => x.Detention.MaxCharge).GreaterThan(0).When(x => x.Detention.MaxCharge.HasValue);
        RuleFor(x => x.Tax.RatePercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Tax.RatePercent).GreaterThan(0).When(x => x.Tax.Mode == TaxMode.Forward).WithMessage("Enter the tax rate for forward-charge tax.");
        RuleForEach(x => x.ExtraCharges).ChildRules(c =>
        {
            c.RuleFor(e => e.Code).NotEmpty().MaximumLength(30);
            c.RuleFor(e => e.Name).NotEmpty().MaximumLength(100);
            c.RuleFor(e => e.Amount).GreaterThanOrEqualTo(0);
        });
    }

    private static bool NoDuplicateScopes(IReadOnlyList<RateDto> rates) =>
        rates.GroupBy(r => (r.OriginLocationId, AgreementRate.Norm(r.OriginCity), AgreementRate.Norm(r.OriginState), r.DestinationLocationId, AgreementRate.Norm(r.DestinationCity), AgreementRate.Norm(r.DestinationState), AgreementRate.Norm(r.Material))).All(g => g.Count() == 1);
}

internal sealed class RateValidator : AbstractValidator<RateDto>
{
    public RateValidator()
    {
        RuleFor(x => x.Rate).GreaterThan(0).When(x => x.Method != RateMethod.WeightSlabs).WithMessage("Enter the rate.");
        RuleFor(x => x.MinimumCharge).GreaterThanOrEqualTo(0).When(x => x.MinimumCharge.HasValue);
        RuleFor(x => x.Slabs).NotEmpty().When(x => x.Method == RateMethod.WeightSlabs).WithMessage("Add at least one weight band.");
        RuleFor(x => x.Slabs).Must(ValidSlabs).When(x => x.Method == RateMethod.WeightSlabs).WithMessage("Weight bands must start at 0 or above, each rate must be above zero, and the bands must not overlap or leave gaps.");
        RuleFor(x => x.OriginCity).MaximumLength(100);
        RuleFor(x => x.DestinationCity).MaximumLength(100);
        RuleFor(x => x.Material).MaximumLength(100);
    }

    private static bool ValidSlabs(IReadOnlyList<WeightSlab> slabs)
    {
        var ordered = slabs.OrderBy(s => s.FromKg).ToList();
        if (ordered.Count == 0 || ordered[0].FromKg < 0)
        {
            return false;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            if (s.RatePerKg <= 0 || (s.ToKg is { } to && to <= s.FromKg))
            {
                return false;
            }

            if (i < ordered.Count - 1 && (s.ToKg is null || s.ToKg != ordered[i + 1].FromKg))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class AgreementHandler(TransportOpsDbContext db, OpsAccess access, ISequenceGenerator sequences, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<AgreementSummaryDto>>> ListAsync(Guid? customerId, AgreementStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Agreements.AsNoTracking().AsQueryable();
        if (customerId is { } c)
        {
            query = query.Where(a => a.CustomerId == c);
        }

        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        var rows = await query.OrderBy(a => a.CustomerId).ThenByDescending(a => a.EffectiveFrom).ThenByDescending(a => a.Revision).Select(a => new { a, Rates = a.Rates.Count }).ToListAsync(cancellationToken);
        var names = await CustomerNamesAsync(rows.Select(r => r.a.CustomerId), cancellationToken);
        return rows.Select(r => new AgreementSummaryDto(r.a.Id, r.a.Number, r.a.Revision, r.a.CustomerId, names.GetValueOrDefault(r.a.CustomerId, "?"), r.a.Title, r.a.EffectiveFrom, r.a.EffectiveTo, r.a.Status, r.Rates)).ToList();
    }

    public async Task<Result<AgreementDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var agreement = await db.Agreements.AsNoTracking().Include(a => a.Rates).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return agreement is null ? OpsErrors.AgreementNotFound : ToDto(agreement, await CustomerNamesAsync([agreement.CustomerId], cancellationToken));
    }

    public async Task<Result<AgreementDto>> CreateAsync(SaveAgreementRequest request, CancellationToken cancellationToken)
    {
        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        if (!await db.Customers.AnyAsync(c => c.Id == request.CustomerId, cancellationToken))
        {
            return OpsErrors.CustomerNotFound;
        }

        var number = $"AGR-{await sequences.NextAsync(access.TenantId!.Value, "ops-agreement", cancellationToken):D5}";
        var agreement = CustomerAgreement.Create(access.TenantId!.Value, number, request.CustomerId, request.Title, request.EffectiveFrom, request.EffectiveTo);
        Apply(agreement, request);
        db.Agreements.Add(agreement);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(agreement, await CustomerNamesAsync([agreement.CustomerId], cancellationToken));
    }

    public async Task<Result<AgreementDto>> UpdateAsync(Guid id, SaveAgreementRequest request, CancellationToken cancellationToken)
    {
        var agreement = await db.Agreements.Include(a => a.Rates).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agreement is null)
        {
            return OpsErrors.AgreementNotFound;
        }

        if (!agreement.IsEditable)
        {
            return Error.Conflict("ops.agreement_locked", "An active agreement is never edited. Create a revision instead, so past deliveries keep the terms they were priced with.");
        }

        db.Entry(agreement).Property(a => a.Version).OriginalValue = request.Version;
        foreach (var old in agreement.Rates.ToList())
        {
            db.AgreementRates.Remove(old);
        }

        Apply(agreement, request);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OpsErrors.ConcurrencyConflict;
        }

        return ToDto(agreement, await CustomerNamesAsync([agreement.CustomerId], cancellationToken));
    }

    public async Task<Result<AgreementDto>> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var agreement = await db.Agreements.Include(a => a.Rates).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agreement is null)
        {
            return OpsErrors.AgreementNotFound;
        }

        if (agreement.Status != AgreementStatus.Draft)
        {
            return Error.Conflict("ops.agreement_state", "Only a draft agreement can be activated.");
        }

        if (agreement.Rates.Count == 0)
        {
            return Error.Validation("ops.agreement_no_rates", "Add at least one rate before activating the agreement.");
        }

        var others = await db.Agreements.Where(a => a.CustomerId == agreement.CustomerId && a.Id != agreement.Id && a.Status == AgreementStatus.Active).ToListAsync(cancellationToken);
        // Another revision of the same agreement is taken over by this one; a different agreement for the same customer must not overlap it.
        var previous = others.Where(o => o.Number == agreement.Number).ToList();
        var clash = others.FirstOrDefault(o => o.Number != agreement.Number && o.EffectiveFrom <= (agreement.EffectiveTo ?? DateOnly.MaxValue) && (o.EffectiveTo ?? DateOnly.MaxValue) >= agreement.EffectiveFrom);
        if (clash is not null)
        {
            return Error.Conflict("ops.agreement_overlap", $"Agreement {clash.Number} for this customer is already in force over these dates. End it first, or make this one a revision of it.");
        }

        foreach (var p in previous)
        {
            p.SupersedeBy(agreement.EffectiveFrom);
        }

        agreement.Activate(access.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(agreement, await CustomerNamesAsync([agreement.CustomerId], cancellationToken));
    }

    public async Task<Result<AgreementDto>> ReviseAsync(Guid id, ReviseAgreementRequest request, CancellationToken cancellationToken)
    {
        var agreement = await db.Agreements.Include(a => a.Rates).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agreement is null)
        {
            return OpsErrors.AgreementNotFound;
        }

        if (agreement.Status != AgreementStatus.Active)
        {
            return Error.Conflict("ops.agreement_state", "Only an active agreement can be revised.");
        }

        if (await db.Agreements.AnyAsync(a => a.RevisionOfId == agreement.Id && a.Status == AgreementStatus.Draft, cancellationToken))
        {
            return Error.Conflict("ops.agreement_draft_exists", "There is already a draft revision of this agreement.");
        }

        if (request.EffectiveFrom <= agreement.EffectiveFrom)
        {
            return Error.Validation("ops.agreement_revision_date", "A revision must start after the agreement it replaces.");
        }

        var latestRevision = await db.Agreements.Where(a => a.Number == agreement.Number).MaxAsync(a => a.Revision, cancellationToken);
        var revision = agreement.CreateRevision(request.EffectiveFrom, latestRevision + 1);
        db.Agreements.Add(revision);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(revision, await CustomerNamesAsync([revision.CustomerId], cancellationToken));
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var agreement = await db.Agreements.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agreement is null)
        {
            return OpsErrors.AgreementNotFound;
        }

        if (agreement.Status != AgreementStatus.Draft)
        {
            return Error.Conflict("ops.agreement_state", "Only a draft agreement can be cancelled.");
        }

        agreement.Cancel();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private void Apply(CustomerAgreement agreement, SaveAgreementRequest r)
    {
        agreement.Edit(r.Title, r.EffectiveFrom, r.EffectiveTo, r.WeightBasis, r.VolumetricKgPerCbm, r.MinimumFreight, r.Detention, r.Tax, r.Billing, r.ExtraCharges);
        agreement.SetRates(r.Rates.Select(x => AgreementRate.Create(agreement.TenantId, agreement.Id, x.OriginLocationId, x.OriginCity, x.OriginState, x.DestinationLocationId, x.DestinationCity,
            x.DestinationState, x.Material, x.Method, x.Rate, x.Slabs, x.MinimumCharge)));
        foreach (var rate in agreement.Rates)
        {
            db.Entry(rate).State = EntityState.Added;
        }
    }

    private async Task<Dictionary<Guid, string>> CustomerNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        return await db.Customers.AsNoTracking().Where(c => wanted.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
    }

    internal static AgreementDto ToDto(CustomerAgreement a, IReadOnlyDictionary<Guid, string> names) =>
        new(a.Id, a.Number, a.Revision, a.CustomerId, names.GetValueOrDefault(a.CustomerId, "?"), a.Title, a.EffectiveFrom, a.EffectiveTo, a.Status, a.WeightBasis, a.VolumetricKgPerCbm, a.MinimumFreight,
            a.Detention, a.Tax, a.Billing, a.ExtraCharges, a.Rates.Select(r => new RateDto(r.Id, r.OriginLocationId, r.OriginCity, r.OriginState, r.DestinationLocationId, r.DestinationCity, r.DestinationState, r.Material,
                r.Method, r.Rate, r.Slabs, r.MinimumCharge)).ToList(), a.ActivatedAt, a.Version);
}

/// <summary>Finds the agreement that was in force for a customer on a date. Judged by dates and activation, so a superseded revision still prices the deliveries of its own period.</summary>
internal static class AgreementSelector
{
    public static async Task<Result<CustomerAgreement>> InForceAsync(TransportOpsDbContext db, Guid customerId, DateOnly date, CancellationToken cancellationToken)
    {
        var candidates = await db.Agreements.Include(a => a.Rates)
            .Where(a => a.CustomerId == customerId && (a.Status == AgreementStatus.Active || a.Status == AgreementStatus.Superseded) && a.EffectiveFrom <= date)
            .ToListAsync(cancellationToken);
        var inForce = candidates.Where(a => a.IsInForce(date)).ToList();
        if (inForce.Count == 0)
        {
            return Error.NotFound("OPS_NO_AGREEMENT", $"The customer has no agreement in force on {date:dd MMM yyyy}.");
        }

        var distinct = inForce.Select(a => a.Number).Distinct().Count();
        if (distinct > 1)
        {
            return Error.Conflict("OPS_AGREEMENT_CONFLICT", "More than one agreement is in force for this customer on that date. End one of them.");
        }

        return inForce.OrderByDescending(a => a.Revision).First();
    }
}
