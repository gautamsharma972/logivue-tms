using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.India;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

public sealed record InvoiceLineDto(
    Guid Id, int LineNumber, Guid LrId, string LrNumber, Guid FreightCalculationId, int CalculationVersion, int? AgreementRevision, string Description, decimal BasicFreight, decimal Detention, decimal OtherCharges,
    decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst);

public sealed record SubmissionDto(Guid Id, int Attempt, SubmissionStatus Status, DateTimeOffset SubmittedAt, Guid? SubmittedBy, string? PortalReference, string? DocumentReference, DateTimeOffset? DecidedAt, string? RejectionReason, string? Notes);

public sealed record PaymentDto(Guid Id, decimal Amount, DateOnly ReceivedOn, string? Mode, string? Reference, string? Remarks, DateTimeOffset RecordedAt);

public sealed record InvoiceSummaryDto(
    Guid Id, string Number, Guid CustomerId, string CustomerName, DateOnly InvoiceDate, DateOnly DueDate, InvoiceStatus Status, SubmissionStatus Submission, PaymentState PaymentState, decimal Subtotal, decimal Tax,
    decimal GrandTotal, decimal AmountPaid, decimal Outstanding, int LineCount, int DaysOverdue);

public sealed record InvoiceDto(
    InvoiceSummaryDto Summary, string? CustomerGstin, string? BillingAddress, string? CustomerReference, TaxMode TaxMode, decimal TaxRatePercent, string? TaxNote, string? Sac, decimal Cgst, decimal Sgst, decimal Igst,
    string? Notes, string? CancelReason, Guid? ApprovalRequestId, DateTimeOffset? IssuedAt, string? SupplierGstin, IReadOnlyList<InvoiceLineDto> Lines, IReadOnlyList<SubmissionDto> Submissions, IReadOnlyList<PaymentDto> Payments, long Version);

public sealed record CreateInvoiceRequest(IReadOnlyList<Guid> CalculationIds, DateOnly? InvoiceDate, string? CustomerReference, string? Notes);

public sealed record SubmitInvoiceRequest(string? PortalReference, string? DocumentReference, string? Notes);

public sealed record AcknowledgeSubmissionRequest(string? PortalReference);

public sealed record RejectSubmissionRequest(string Reason);

public sealed record CancelInvoiceRequest(string Reason);

public sealed record PaymentRequest(decimal Amount, DateOnly ReceivedOn, string? Mode, string? Reference, string? Remarks);

public sealed record ReceivableRowDto(Guid CustomerId, string CustomerName, decimal NotDue, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total, int Invoices);

internal sealed class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(x => x.CalculationIds).NotEmpty().WithMessage("Choose at least one delivery to invoice.").Must(l => l.Count <= 200).WithMessage("An invoice can have at most 200 lines.");
        RuleFor(x => x.CustomerReference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

internal sealed class SubmitInvoiceRequestValidator : AbstractValidator<SubmitInvoiceRequest>
{
    public SubmitInvoiceRequestValidator()
    {
        RuleFor(x => x.PortalReference).MaximumLength(100);
        RuleFor(x => x.DocumentReference).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

internal sealed class RejectSubmissionRequestValidator : AbstractValidator<RejectSubmissionRequest>
{
    public RejectSubmissionRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class CancelInvoiceRequestValidator : AbstractValidator<CancelInvoiceRequest>
{
    public CancelInvoiceRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class PaymentRequestValidator : AbstractValidator<PaymentRequest>
{
    public PaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Mode).MaximumLength(30);
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

internal sealed class InvoiceHandler(TransportOpsDbContext db, OpsAccess access, IOpsSettings settings, IApprovalGateway approvals, TimeProvider clock)
{
    public const string DocumentType = "customer_invoice";

    public async Task<Result<PagedResult<InvoiceSummaryDto>>> ListAsync(
        string? search, Guid? customerId, InvoiceStatus? status, SubmissionStatus? submission, PaymentState? payment, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Invoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(i => i.Number.Contains(s) || i.CustomerName.Contains(s) || (i.CustomerReference != null && i.CustomerReference.Contains(s)));
        }

        if (customerId is { } c)
        {
            query = query.Where(i => i.CustomerId == c);
        }

        if (status is { } st)
        {
            query = query.Where(i => i.Status == st);
        }

        if (submission is { } sub)
        {
            query = query.Where(i => i.Submission == sub);
        }

        if (payment is { } pay)
        {
            query = pay switch
            {
                PaymentState.Unpaid => query.Where(i => i.AmountPaid <= 0),
                PaymentState.Paid => query.Where(i => i.AmountPaid >= i.GrandTotal && i.GrandTotal > 0),
                _ => query.Where(i => i.AmountPaid > 0 && i.AmountPaid < i.GrandTotal),
            };
        }

        if (from is { } f)
        {
            query = query.Where(i => i.InvoiceDate >= f);
        }

        if (to is { } t)
        {
            query = query.Where(i => i.InvoiceDate <= t);
        }

        var paged = await query.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Number).Select(i => new { i, Lines = i.Lines.Count }).ToPagedAsync(page, pageSize, cancellationToken);
        var today = clock.TodayInIndia();
        return new PagedResult<InvoiceSummaryDto>(paged.Items.Select(x => Summary(x.i, x.Lines, today)).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).Include(i => i.Submissions).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return invoice is null ? OpsErrors.InvoiceNotFound : await ToDtoAsync(invoice, cancellationToken);
    }

    /// <summary>
    /// Turns reviewed calculations into one customer invoice. Only deliveries whose charges have been reviewed and not yet billed qualify, and the POD is checked again here. The
    /// invoice number is taken in the same transaction as the invoice, so a failed attempt leaves no gap in the numbers.
    /// </summary>
    public async Task<Result<InvoiceDto>> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        var tenant = access.TenantId!.Value;
        var ids = request.CalculationIds.Distinct().ToList();
        var calcs = await db.Calculations.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken);
        if (calcs.Count != ids.Count)
        {
            return OpsErrors.CalcNotFound;
        }

        if (calcs.Select(c => c.CustomerId).Distinct().Count() != 1)
        {
            return Error.Validation("ops.invoice_customers", "One invoice is for one customer. Choose deliveries of the same customer.");
        }

        foreach (var calc in calcs)
        {
            if (calc.Status == CalculationStatus.Invoiced || calc.InvoiceId is not null)
            {
                return Error.Conflict("ops.invoice_duplicate", $"{calc.LrNumber} has already been invoiced.");
            }

            if (calc.Status != CalculationStatus.Reviewed)
            {
                return Error.Conflict("ops.invoice_not_reviewed", calc.Status == CalculationStatus.OnHold
                    ? $"{calc.LrNumber} is on hold: {string.Join(" ", calc.Holds)}"
                    : $"{calc.LrNumber} has not been reviewed yet.");
            }
        }

        var lrIds = calcs.Select(c => c.LrId).ToList();
        if (await db.InvoiceLines.AnyAsync(l => lrIds.Contains(l.LrId) && db.Invoices.Any(i => i.Id == l.InvoiceId && i.Status != InvoiceStatus.Cancelled), cancellationToken))
        {
            return Error.Conflict("ops.invoice_duplicate", "One of these deliveries is already on an invoice.");
        }

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == calcs[0].CustomerId, cancellationToken);
        if (customer is null)
        {
            return OpsErrors.CustomerNotFound;
        }

        var agreementIds = calcs.Where(c => c.AgreementId is not null).Select(c => c.AgreementId!.Value).Distinct().ToList();
        var agreements = await db.Agreements.AsNoTracking().Where(a => agreementIds.Contains(a.Id)).ToListAsync(cancellationToken);
        var taxTerms = agreements.Select(a => a.Tax).Distinct().ToList();
        if (taxTerms.Count != 1)
        {
            return Error.Validation("ops.invoice_tax_terms", "These deliveries have different tax terms. Invoice them separately.");
        }

        // The POD is checked again at the moment of invoicing: it may have been reopened since the charges were reviewed.
        var pods = await db.Pods.AsNoTracking().Where(p => lrIds.Contains(p.LrId)).ToDictionaryAsync(p => p.LrId, p => p.Status, cancellationToken);
        foreach (var calc in calcs)
        {
            var agreement = agreements.First(a => a.Id == calc.AgreementId);
            var needsPod = agreement.Billing.RequireAcceptedPod && !agreement.Billing.AllowBillingWithoutPod;
            if (needsPod && pods.GetValueOrDefault(calc.LrId) != PodStatus.Accepted)
            {
                return Error.Conflict("ops.invoice_pod", $"The POD of {calc.LrNumber} is not accepted any more. Review it before invoicing.");
            }
        }

        var invoiceSettings = await settings.GetAsync(OpsSettingKeys.Invoice, new InvoiceSetting(), cancellationToken);
        var supplierState = invoiceSettings.SupplierStateCode ?? (IndianIdentifiers.IsValidGstin(invoiceSettings.SupplierGstin) ? IndianIdentifiers.StateCodeFromGstin(invoiceSettings.SupplierGstin!) : null);
        var customerState = IndianIdentifiers.IsValidGstin(customer.Gstin) ? IndianIdentifiers.StateCodeFromGstin(customer.Gstin!) : null;
        var tax = taxTerms[0];
        var date = request.InvoiceDate ?? clock.TodayInIndia();
        var financialYear = FinancialYear.Label(date);
        var series = invoiceSettings.SeriesPrefix.Trim().ToUpperInvariant();

        var lrs = await db.Lrs.AsNoTracking().Where(l => lrIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        var placeIds = lrs.Values.SelectMany(l => new[] { l.CfaLocationId, l.DestinationLocationId }).Distinct().ToList();
        var places = await db.Locations.AsNoTracking().Where(l => placeIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        var podIdsByLr = await db.Pods.AsNoTracking().Where(p => lrIds.Contains(p.LrId)).ToDictionaryAsync(p => p.LrId, p => p.Id, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var sequence = await NextNumberAsync(tenant, series, financialYear, cancellationToken);
        var number = $"{series}/{financialYear}/{sequence.ToString("D5", System.Globalization.CultureInfo.InvariantCulture)}";
        var invoice = Invoice.Create(tenant, number, series, financialYear, customer, tax, date, customer.DueDays, request.CustomerReference ?? calcs.Select(c => lrs[c.LrId].CustomerReference).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)), request.Notes);
        var lineNumber = 0;
        foreach (var calc in calcs.OrderBy(c => c.LrNumber, StringComparer.Ordinal))
        {
            var lr = lrs[calc.LrId];
            var taxed = TaxCalculator.Compute(calc.TotalPreTax, tax, supplierState, customerState);
            if (taxed.IsFailure)
            {
                return taxed.Error;
            }

            var description = $"{calc.LrNumber}: {places.GetValueOrDefault(lr.CfaLocationId, "?")} to {places.GetValueOrDefault(lr.DestinationLocationId, "?")}{(lr.VehicleRegistration is null ? string.Empty : $", vehicle {lr.VehicleRegistration}")}";
            invoice.AddLine(InvoiceLine.Create(tenant, invoice.Id, ++lineNumber, calc, podIdsByLr.GetValueOrDefault(calc.LrId), description, taxed.Value));
            calc.MarkInvoiced(invoice.Id);
        }

        foreach (var line in invoice.Lines)
        {
            db.Entry(line).State = EntityState.Added;
        }

        if (invoiceSettings.RequireApproval)
        {
            var submitted = await approvals.SubmitAsync(new SubmitApproval(DocumentType, invoice.Id, $"Invoice {number} - {customer.Name}", invoice.GrandTotal), cancellationToken);
            if (submitted.IsFailure)
            {
                return submitted.Error;
            }

            invoice.AwaitApproval(submitted.Value.RequestId);
            if (submitted.Value.Status == ApprovalStatus.Approved)
            {
                invoice.ApplyApprovalOutcome(submitted.Value.RequestId, true, clock.GetUtcNow());
            }
        }
        else
        {
            invoice.Issue(access.UserId, clock.GetUtcNow());
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ToDtoAsync(invoice, cancellationToken);
    }

    public async Task<Result<InvoiceDto>> CancelAsync(Guid id, CancelInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Submissions).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return OpsErrors.InvoiceNotFound;
        }

        var cancelled = invoice.Cancel(request.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        // The deliveries go back to "reviewed" so they can be invoiced again.
        var calcIds = invoice.Lines.Select(l => l.FreightCalculationId).ToList();
        foreach (var calc in await db.Calculations.Where(c => calcIds.Contains(c.Id)).ToListAsync(cancellationToken))
        {
            calc.ReleaseFromInvoice();
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(invoice, cancellationToken);
    }

    public Task<Result<InvoiceDto>> SubmitAsync(Guid id, SubmitInvoiceRequest request, CancellationToken cancellationToken) =>
        MutateAsync(id, (invoice, now) => invoice.Submit(access.UserId, now, request.PortalReference, request.DocumentReference, request.Notes), cancellationToken);

    public Task<Result<InvoiceDto>> AcknowledgeAsync(Guid id, AcknowledgeSubmissionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(id, (invoice, now) => invoice.Acknowledge(now, request.PortalReference), cancellationToken);

    public Task<Result<InvoiceDto>> RejectSubmissionAsync(Guid id, RejectSubmissionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(id, (invoice, now) => invoice.RejectSubmission(now, request.Reason), cancellationToken);

    public async Task<Result<InvoiceDto>> RecordPaymentAsync(Guid id, PaymentRequest request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Submissions).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return OpsErrors.InvoiceNotFound;
        }

        var applied = invoice.ApplyPayment(request.Amount);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        db.Payments.Add(Payment.Create(invoice.TenantId, invoice.Id, invoice.CustomerId, request.Amount, request.ReceivedOn, request.Mode, request.Reference, request.Remarks, clock.GetUtcNow(), access.UserId));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OpsErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(invoice, cancellationToken);
    }

    /// <summary>What customers owe, by how long it has been overdue. Cancelled and unissued invoices are not receivables.</summary>
    public async Task<Result<IReadOnlyList<ReceivableRowDto>>> ReceivablesAsync(DateOnly? asOf, CancellationToken cancellationToken)
    {
        var today = asOf ?? clock.TodayInIndia();
        var open = await db.Invoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Issued && i.AmountPaid < i.GrandTotal).ToListAsync(cancellationToken);
        return open.GroupBy(i => i.CustomerId).Select(g =>
        {
            decimal Bucket(Func<int, bool> within) => g.Where(i => within(today.DayNumber - i.DueDate.DayNumber)).Sum(i => i.Outstanding);
            return new ReceivableRowDto(g.Key, g.First().CustomerName, Bucket(d => d <= 0), Bucket(d => d is >= 1 and <= 30), Bucket(d => d is >= 31 and <= 60), Bucket(d => d is >= 61 and <= 90), Bucket(d => d > 90),
                g.Sum(i => i.Outstanding), g.Count());
        }).OrderByDescending(r => r.Total).ToList();
    }

    private async Task<Result<InvoiceDto>> MutateAsync(Guid id, Func<Invoice, DateTimeOffset, Result> action, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Submissions).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return OpsErrors.InvoiceNotFound;
        }

        var result = action(invoice, clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error;
        }

        foreach (var submission in invoice.Submissions)
        {
            if (db.Entry(submission).State == EntityState.Detached)
            {
                db.Entry(submission).State = EntityState.Added;
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OpsErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(invoice, cancellationToken);
    }

    /// <summary>Takes the next number of a series. Runs inside the caller's transaction and holds the row until it commits, so numbers are never skipped or repeated.</summary>
    private async Task<long> NextNumberAsync(Guid tenant, string series, string financialYear, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO to_invoice_series (id, tenant_id, series, financial_year, last_number) VALUES ({Guid.CreateVersion7()}, {tenant}, {series}, {financialYear}, 1) ON DUPLICATE KEY UPDATE last_number = last_number + 1",
            cancellationToken);
        return await db.Database.SqlQuery<long>($"SELECT last_number AS `Value` FROM to_invoice_series WHERE tenant_id = {tenant} AND series = {series} AND financial_year = {financialYear}").SingleAsync(cancellationToken);
    }

    private static InvoiceSummaryDto Summary(Invoice i, int lines, DateOnly today) =>
        new(i.Id, i.Number, i.CustomerId, i.CustomerName, i.InvoiceDate, i.DueDate, i.Status, i.Submission, i.PaymentState, i.Subtotal, i.Cgst + i.Sgst + i.Igst, i.GrandTotal, i.AmountPaid, i.Outstanding, lines,
            i.Status == InvoiceStatus.Issued && i.Outstanding > 0 ? Math.Max(0, today.DayNumber - i.DueDate.DayNumber) : 0);

    private async Task<InvoiceDto> ToDtoAsync(Invoice i, CancellationToken cancellationToken)
    {
        var payments = await db.Payments.AsNoTracking().Where(p => p.InvoiceId == i.Id).OrderBy(p => p.ReceivedOn).ThenBy(p => p.RecordedAt).ToListAsync(cancellationToken);
        var invoiceSettings = await settings.GetAsync(OpsSettingKeys.Invoice, new InvoiceSetting(), cancellationToken);
        return new InvoiceDto(Summary(i, i.Lines.Count, clock.TodayInIndia()), i.CustomerGstin, i.BillingAddress, i.CustomerReference, i.TaxMode, i.TaxRatePercent, i.TaxNote, i.Sac, i.Cgst, i.Sgst, i.Igst, i.Notes,
            i.CancelReason, i.ApprovalRequestId, i.IssuedAt, invoiceSettings.SupplierGstin,
            i.Lines.OrderBy(l => l.LineNumber).Select(l => new InvoiceLineDto(l.Id, l.LineNumber, l.LrId, l.LrNumber, l.FreightCalculationId, l.CalculationVersion, l.AgreementRevision, l.Description, l.BasicFreight, l.Detention,
                l.OtherCharges, l.Taxable, l.Cgst, l.Sgst, l.Igst)).ToList(),
            i.Submissions.OrderBy(s => s.Attempt).Select(s => new SubmissionDto(s.Id, s.Attempt, s.Status, s.SubmittedAt, s.SubmittedBy, s.PortalReference, s.DocumentReference, s.DecidedAt, s.RejectionReason, s.Notes)).ToList(),
            payments.Select(p => new PaymentDto(p.Id, p.Amount, p.ReceivedOn, p.Mode, p.Reference, p.Remarks, p.RecordedAt)).ToList(), i.Version);
    }
}
