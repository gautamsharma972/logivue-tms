using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

/// <summary>
/// One invoiced delivery whose freight would be different under the agreement revision that is in force for its delivery date today.
/// Positive differences are arrears (under-billed); negative ones are over-billed.
/// </summary>
public sealed record ArrearRowDto(
    Guid InvoiceId, string InvoiceNumber, DateOnly InvoiceDate, Guid CustomerId, string CustomerName, PaymentState PaymentState, string LrNumber, DateOnly DeliveryDate,
    int? InvoicedRevision, int CurrentRevision, decimal InvoicedFreight, decimal? RevisedFreight, decimal? FreightDifference, decimal? TaxDifference, decimal? TotalDifference, string? Note);

public sealed record ArrearCustomerDto(Guid CustomerId, string CustomerName, int Invoices, int Lines, decimal FreightDifference, decimal TaxDifference, decimal TotalDifference);

public sealed record ArrearsReportDto(
    IReadOnlyList<ArrearRowDto> Rows, IReadOnlyList<ArrearCustomerDto> Customers, decimal FreightDifference, decimal TaxDifference, decimal TotalDifference, int NotPriced);

/// <summary>
/// The arrears report. An invoice is never recalculated once issued, so when an agreement is revised with an effective date that reaches back over deliveries already invoiced,
/// this reprices each of those deliveries on the revision now in force for its delivery date and shows the difference. It reads only; correcting the customer's account
/// (a supplementary invoice or credit note) is a decision for the accountant.
/// </summary>
internal sealed class ArrearsHandler(TransportOpsDbContext db)
{
    public async Task<Result<ArrearsReportDto>> ReportAsync(Guid? customerId, DateOnly? from, DateOnly? to, bool includeUnchanged, CancellationToken cancellationToken)
    {
        var invoices = db.Invoices.AsNoTracking().Where(i => i.Status == InvoiceStatus.Issued);
        if (customerId is { } c)
        {
            invoices = invoices.Where(i => i.CustomerId == c);
        }

        if (from is { } f)
        {
            invoices = invoices.Where(i => i.InvoiceDate >= f);
        }

        if (to is { } t)
        {
            invoices = invoices.Where(i => i.InvoiceDate <= t);
        }

        var headers = await invoices.ToListAsync(cancellationToken);
        var ids = headers.Select(i => i.Id).ToList();
        var lines = await db.InvoiceLines.AsNoTracking().Where(l => ids.Contains(l.InvoiceId)).ToListAsync(cancellationToken);
        var calcIds = lines.Select(l => l.FreightCalculationId).ToList();
        var facts = await db.Calculations.AsNoTracking().Where(x => calcIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Input, cancellationToken);

        var rows = new List<ArrearRowDto>();
        var inForce = new Dictionary<(Guid Customer, DateOnly Date), Result<CustomerAgreement>>();
        foreach (var invoice in headers.OrderBy(i => i.InvoiceDate).ThenBy(i => i.Number, StringComparer.Ordinal))
        {
            foreach (var line in lines.Where(l => l.InvoiceId == invoice.Id).OrderBy(l => l.LineNumber))
            {
                if (!facts.TryGetValue(line.FreightCalculationId, out var input) || input is null)
                {
                    continue;
                }

                var key = (invoice.CustomerId, input.Date);
                if (!inForce.TryGetValue(key, out var found))
                {
                    found = await AgreementSelector.InForceAsync(db, invoice.CustomerId, input.Date, cancellationToken);
                    inForce[key] = found;
                }

                var invoiced = line.BasicFreight + line.OtherCharges;
                if (found.IsFailure)
                {
                    rows.Add(new ArrearRowDto(invoice.Id, invoice.Number, invoice.InvoiceDate, invoice.CustomerId, invoice.CustomerName, PaymentOf(invoice), line.LrNumber, input.Date,
                        line.AgreementRevision, 0, invoiced, null, null, null, null, found.Error.Description));
                    continue;
                }

                var agreement = found.Value;
                if (agreement.Id == line.AgreementId && agreement.Revision == line.AgreementRevision)
                {
                    continue; // priced on the revision that still applies
                }

                var priced = CustomerFreightCalculator.Calculate(agreement, input);
                if (priced.IsFailure)
                {
                    rows.Add(new ArrearRowDto(invoice.Id, invoice.Number, invoice.InvoiceDate, invoice.CustomerId, invoice.CustomerName, PaymentOf(invoice), line.LrNumber, input.Date,
                        line.AgreementRevision, agreement.Revision, invoiced, null, null, null, null, $"Revision {agreement.Revision} cannot price this delivery: {priced.Error.Description}"));
                    continue;
                }

                var revised = priced.Value.BasicFreight + priced.Value.OtherCharges;
                var difference = Money.Round(revised - invoiced);
                if (difference == 0 && !includeUnchanged)
                {
                    continue;
                }

                var tax = invoice.TaxMode == TaxMode.Forward ? Money.Round(difference * invoice.TaxRatePercent / 100m) : 0m;
                rows.Add(new ArrearRowDto(invoice.Id, invoice.Number, invoice.InvoiceDate, invoice.CustomerId, invoice.CustomerName, PaymentOf(invoice), line.LrNumber, input.Date,
                    line.AgreementRevision, agreement.Revision, invoiced, Money.Round(revised), difference, tax, difference + tax, null));
            }
        }

        var priced2 = rows.Where(r => r.FreightDifference is not null).ToList();
        var customers = priced2.GroupBy(r => (r.CustomerId, r.CustomerName))
            .Select(g => new ArrearCustomerDto(g.Key.CustomerId, g.Key.CustomerName, g.Select(r => r.InvoiceId).Distinct().Count(), g.Count(), g.Sum(r => r.FreightDifference!.Value), g.Sum(r => r.TaxDifference!.Value), g.Sum(r => r.TotalDifference!.Value)))
            .OrderBy(c => c.CustomerName, StringComparer.OrdinalIgnoreCase).ToList();
        return new ArrearsReportDto(rows, customers, priced2.Sum(r => r.FreightDifference!.Value), priced2.Sum(r => r.TaxDifference!.Value), priced2.Sum(r => r.TotalDifference!.Value), rows.Count - priced2.Count);
    }

    private static PaymentState PaymentOf(Invoice invoice) =>
        invoice.AmountPaid <= 0 ? PaymentState.Unpaid : invoice.AmountPaid >= invoice.GrandTotal ? PaymentState.Paid : PaymentState.PartiallyPaid;
}
