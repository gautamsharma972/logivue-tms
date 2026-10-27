using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

public sealed record CalcLineDto(string Code, string Description, decimal Amount, string Basis);

public sealed record DetentionDto(
    DetentionStatus Status, string? HoldReason, DateTimeOffset? Arrival, DateTimeOffset? Unloading, double ElapsedHours, DateTimeOffset? FreeUntil, double ChargeableHours, decimal Units, string? ChargeUnit, decimal Rate,
    string? Rounding, decimal? Cap, bool CapApplied, decimal Amount);

public sealed record CalculationDto(
    Guid Id, Guid LrId, string LrNumber, Guid CustomerId, int Version, CalculationStatus Status, Guid? AgreementId, int? AgreementRevision, decimal BillableWeightKg, string? WeightBasisUsed, decimal BasicFreight,
    decimal OtherCharges, decimal DetentionAmount, decimal? DetentionOverride, string? DetentionOverrideReason, decimal TotalPreTax, IReadOnlyList<string> Holds, IReadOnlyList<CalcLineDto> Lines, DetentionDto? Detention,
    IReadOnlyList<string> Trace, bool PodAccepted, bool BilledWithoutPod, DateTimeOffset CalculatedAt, Guid? ReviewedBy, DateTimeOffset? ReviewedAt, string? ReviewNote, Guid? InvoiceId, long Version2);

public sealed record BillingRowDto(
    Guid LrId, string LrNumber, Guid CustomerId, string CustomerName, string? VehicleRegistration, string DestinationName, DateTimeOffset? ArrivedAt, DateTimeOffset? UnloadedAt, PodStatus PodStatus,
    decimal? BillableWeightKg, decimal? BasicFreight, decimal? Detention, decimal? OtherCharges, decimal? TotalPreTax, Guid? CalcId, CalculationStatus? CalcStatus, int? CalcVersion, IReadOnlyList<string> Holds,
    bool Eligible, Guid? InvoiceId, string? InvoiceNumber);

public sealed record CalculateRequest(IReadOnlyList<Guid> LrIds);

public sealed record CalculateResultDto(IReadOnlyList<CalculationDto> Calculations, IReadOnlyList<CalculateFailureDto> Failures);

public sealed record CalculateFailureDto(Guid LrId, string Code, string Message);

public sealed record ReviewCalculationRequest(string? Note);

public sealed record ResolveHoldRequest(decimal DetentionAmount, string Reason);

internal sealed class CalculateRequestValidator : AbstractValidator<CalculateRequest>
{
    public CalculateRequestValidator() => RuleFor(x => x.LrIds).NotEmpty().Must(l => l.Count <= 100).WithMessage("Calculate up to 100 LRs at a time.");
}

internal sealed class ResolveHoldRequestValidator : AbstractValidator<ResolveHoldRequest>
{
    public ResolveHoldRequestValidator()
    {
        RuleFor(x => x.DetentionAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>Prices delivered LRs from the customer's agreement. Freight and detention are office work done here, never in the driver's QR journey.</summary>
internal sealed class BillingHandler(TransportOpsDbContext db, OpsAccess access, TimeProvider clock)
{
    public async Task<Result<PagedResult<BillingRowDto>>> WorkbenchAsync(
        Guid? customerId, DateOnly? from, DateOnly? to, string? search, PodStatus? podStatus, CalculationStatus? calcStatus, bool? invoiced, int page, int pageSize, CancellationToken cancellationToken)
    {
        // Delivered LRs only: billing starts once unloading has been confirmed.
        var query = db.Lrs.AsNoTracking().Where(l => l.Status == DeliveryStatus.UnloadingCompleted);
        if (customerId is { } c)
        {
            query = query.Where(l => l.CustomerId == c);
        }

        if (from is { } f)
        {
            var start = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
            query = query.Where(l => l.UnloadedAt >= start);
        }

        if (to is { } t)
        {
            var end = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
            query = query.Where(l => l.UnloadedAt < end);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(l => l.Number.Contains(s) || (l.CustomerReference != null && l.CustomerReference.Contains(s)));
        }

        if (podStatus is { } ps)
        {
            query = query.Where(l => db.Pods.Any(p => p.LrId == l.Id && p.Status == ps));
        }

        if (calcStatus is { } cs)
        {
            query = query.Where(l => db.Calculations.Any(x => x.LrId == l.Id && x.Status == cs));
        }

        if (invoiced is { } inv)
        {
            query = inv
                ? query.Where(l => db.Calculations.Any(x => x.LrId == l.Id && x.Status == CalculationStatus.Invoiced))
                : query.Where(l => !db.Calculations.Any(x => x.LrId == l.Id && x.Status == CalculationStatus.Invoiced));
        }

        var paged = await query.OrderBy(l => l.UnloadedAt).ToPagedAsync(page, pageSize, cancellationToken);
        var lrs = paged.Items.ToList();
        var lookups = await new Lookups(db).LoadAsync(lrs, cancellationToken);
        var ids = lrs.Select(l => l.Id).ToList();
        var calcs = (await db.Calculations.AsNoTracking().Where(x => ids.Contains(x.LrId) && x.Status != CalculationStatus.Superseded).ToListAsync(cancellationToken)).ToDictionary(x => x.LrId);
        var invoiceIds = calcs.Values.Where(x => x.InvoiceId is not null).Select(x => x.InvoiceId!.Value).Distinct().ToList();
        var numbers = await db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, cancellationToken);
        var items = lrs.Select(l =>
        {
            calcs.TryGetValue(l.Id, out var x);
            var pod = lookups.Pods.GetValueOrDefault(l.Id, PodStatus.NotYetDue);
            return new BillingRowDto(l.Id, l.Number, l.CustomerId, lookups.Customers.GetValueOrDefault(l.CustomerId, "?"), l.VehicleRegistration,
                lookups.Locations.TryGetValue(l.DestinationLocationId, out var d) ? d.Name : "?", l.ArrivedAt, l.UnloadedAt, pod, x?.BillableWeightKg, x?.BasicFreight, x?.EffectiveDetention, x?.OtherCharges,
                x?.TotalPreTax, x?.Id, x?.Status, x?.CalcVersion, x?.Holds ?? (pod != PodStatus.Accepted ? ["POD is not accepted yet."] : []),
                x is { Status: CalculationStatus.Reviewed, InvoiceId: null }, x?.InvoiceId, x?.InvoiceId is { } i && numbers.TryGetValue(i, out var n) ? n : null);
        }).ToList();
        return new PagedResult<BillingRowDto>(items, paged.Page, paged.PageSize, paged.TotalCount);
    }

    private static Result<FreightResult> CrossBilled(Lr lr)
    {
        if (lr.CrossFreight is not { } amount || lr.CrossMode is not { } mode)
        {
            return Error.NotFound("OPS_CROSS_BILLING_PRICE", "This cross-billing LR has no price. Enter the lumpsum or the kg and rate.");
        }

        var basis = mode == CrossBillingMode.Lumpsum ? "lumpsum" : $"{lr.CrossKg:0.###} kg x {lr.CrossRatePerKg:0.####}";
        return new FreightResult(null, mode == CrossBillingMode.PerKg ? lr.CrossKg ?? 0 : lr.TotalWeightKg, "Cross billing", amount, 0, amount,
            [new FreightLine("CROSS_BILLING", "Cross-billing freight", amount, basis)], [$"Cross billing ({basis}): {amount:0.00}. The customer agreement's rates were not used."]);
    }

    public async Task<Result<CalculateResultDto>> CalculateAsync(CalculateRequest request, CancellationToken cancellationToken)
    {
        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        var results = new List<CalculationDto>();
        var failures = new List<CalculateFailureDto>();
        foreach (var lrId in request.LrIds.Distinct())
        {
            var one = await CalculateOneAsync(lrId, cancellationToken);
            if (one.IsSuccess)
            {
                results.Add(one.Value);
            }
            else
            {
                failures.Add(new CalculateFailureDto(lrId, one.Error.Code, one.Error.Description));
            }
        }

        return new CalculateResultDto(results, failures);
    }

    public async Task<Result<CalculationDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var calc = await db.Calculations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return calc is null ? OpsErrors.CalcNotFound : ToDto(calc);
    }

    public async Task<Result<IReadOnlyList<CalculationDto>>> ForLrAsync(Guid lrId, CancellationToken cancellationToken)
    {
        var rows = await db.Calculations.AsNoTracking().Where(c => c.LrId == lrId).OrderByDescending(c => c.CalcVersion).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result<CalculationDto>> ReviewAsync(Guid id, ReviewCalculationRequest request, CancellationToken cancellationToken)
    {
        var calc = await db.Calculations.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (calc is null)
        {
            return OpsErrors.CalcNotFound;
        }

        var reviewed = calc.Review(access.UserId, clock.GetUtcNow(), request.Note);
        if (reviewed.IsFailure)
        {
            return reviewed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(calc);
    }

    public async Task<Result<CalculationDto>> ResolveHoldAsync(Guid id, ResolveHoldRequest request, CancellationToken cancellationToken)
    {
        var calc = await db.Calculations.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (calc is null)
        {
            return OpsErrors.CalcNotFound;
        }

        var resolved = calc.ResolveDetentionHold(request.DetentionAmount, request.Reason, access.UserId, clock.GetUtcNow());
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(calc);
    }

    private async Task<Result<CalculationDto>> CalculateOneAsync(Guid lrId, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.AsNoTracking().Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == lrId, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        if (lr.Status != DeliveryStatus.UnloadingCompleted)
        {
            return Error.Conflict("ops.billing_not_delivered", $"{lr.Number} has not been delivered yet, so it cannot be billed.");
        }

        var previous = await db.Calculations.Where(c => c.LrId == lrId && c.Status != CalculationStatus.Superseded).ToListAsync(cancellationToken);
        if (previous.Any(c => c.Status == CalculationStatus.Invoiced))
        {
            return Error.Conflict("ops.billing_invoiced", $"{lr.Number} has already been invoiced. Cancel the invoice first if it has to be recalculated.");
        }

        var pod = await db.Pods.AsNoTracking().FirstAsync(p => p.LrId == lrId, cancellationToken);
        var origin = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == lr.CfaLocationId, cancellationToken);
        var destination = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == lr.DestinationLocationId, cancellationToken);
        var date = lr.DeliveryDate ?? Clock.BusinessDate(lr.UnloadedAt ?? lr.CreatedAt);
        var facts = new LrFacts(date, origin.Id, origin.City, origin.State, destination.Id, destination.City, destination.State, lr.Lines.FirstOrDefault()?.MaterialName, lr.TotalWeightKg, lr.TotalVolumeCbm);

        var holds = new List<string>();
        string? failure = null;
        FreightResult? freight = null;
        DetentionResult? detention = null;
        var selected = await AgreementSelector.InForceAsync(db, lr.CustomerId, date, cancellationToken);
        CustomerAgreement? agreement = null;
        var billedWithoutPod = false;
        if (selected.IsFailure)
        {
            failure = selected.Error.Description;
        }
        else
        {
            agreement = selected.Value;
            // Cross billing: the price was agreed outside the agreement and typed on the LR. The agreement still supplies tax, detention and the POD rule.
            var priced = lr.Movement == MovementType.CrossBilling ? CrossBilled(lr) : CustomerFreightCalculator.Calculate(agreement, facts);
            if (priced.IsSuccess)
            {
                freight = priced.Value;
                if (lr.Movement == MovementType.Return)
                {
                    freight = freight with { Trace = [.. freight.Trace, "Return movement (distributor to CFA): priced on the agreed CFA to distributor lane."] };
                }
            }
            else
            {
                failure = priced.Error.Description;
            }

            detention = DetentionCalculator.Calculate(agreement.Detention, lr.ArrivedAt, lr.UnloadedAt);
            if (detention.Status == DetentionStatus.OnHold && detention.HoldReason is { } why)
            {
                holds.Add(why);
            }

            var needsPod = agreement.Billing.RequireAcceptedPod && !agreement.Billing.AllowBillingWithoutPod;
            if (pod.Status != PodStatus.Accepted)
            {
                if (needsPod)
                {
                    holds.Add("The proof of delivery has not been accepted yet.");
                }
                else
                {
                    billedWithoutPod = true;
                }
            }
        }

        var version = (await db.Calculations.Where(c => c.LrId == lrId).MaxAsync(c => (int?)c.CalcVersion, cancellationToken) ?? 0) + 1;
        var calc = FreightCalculation.Create(lr.TenantId, lr, version, agreement, freight, detention, facts, pod.Status == PodStatus.Accepted, billedWithoutPod, holds, failure, clock.GetUtcNow(), access.UserId);

        // Nothing changed since the charges were reviewed (for example after an invoice was cancelled): keep that approval instead of asking for a second review.
        if (previous.FirstOrDefault(c => c.Status == CalculationStatus.Reviewed && c.InvoiceId is null) is { } approved && approved.SameOutcomeAs(calc))
        {
            return ToDto(approved);
        }

        foreach (var old in previous)
        {
            old.Supersede();
        }

        db.Calculations.Add(calc);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(calc);
    }

    internal static CalculationDto ToDto(FreightCalculation c) =>
        new(c.Id, c.LrId, c.LrNumber, c.CustomerId, c.CalcVersion, c.Status, c.AgreementId, c.AgreementRevision, c.BillableWeightKg, c.WeightBasisUsed, c.BasicFreight, c.OtherCharges, c.DetentionAmount,
            c.DetentionOverride, c.DetentionOverrideReason, c.TotalPreTax, c.Holds, c.Lines.Select(l => new CalcLineDto(l.Code, l.Description, l.Amount, l.Basis)).ToList(),
            c.Detention is { } d ? new DetentionDto(d.Status, d.HoldReason, d.Arrival, d.Unloading, d.ElapsedHours, d.FreeUntil, d.ChargeableHours, d.Units, d.ChargeUnit?.ToString(), d.Rate, d.Rounding?.ToString(), d.Cap, d.CapApplied, d.Amount) : null,
            c.Trace, c.PodAccepted, c.BilledWithoutPod, c.CalculatedAt, c.ReviewedBy, c.ReviewedAt, c.ReviewNote, c.InvoiceId, c.Version);
}
