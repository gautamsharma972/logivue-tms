using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

/// <summary>Everything the printed invoice shows, assembled here so the page only lays it out.</summary>
public sealed record InvoicePrintDto(
    string SupplierName, string? SupplierAddress, string? SupplierGstin, string? SupplierState, bool HasLogo, bool HasSignature, string Declaration,
    string CustomerName, string? CustomerAddress, string? CustomerGstin, string? CustomerState,
    string InvoiceNumber, DateOnly InvoiceDate, string Reference, string Description, IReadOnlyList<string> LrNumbers, string? HsnSac,
    decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, TaxMode TaxMode, decimal TaxRatePercent, string? TaxNote, decimal GrandTotal, string AmountInWords, InvoiceStatus Status);

internal sealed class InvoicePrintHandler(TransportOpsDbContext db, IOpsSettings settings)
{
    public async Task<Result<InvoicePrintDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return OpsErrors.InvoiceNotFound;
        }

        var supplier = await settings.GetAsync(OpsSettingKeys.Invoice, new InvoiceSetting(), cancellationToken);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, cancellationToken);

        // The month of the deliveries billed: the month each LR was delivered, not the month the invoice is raised.
        var calcIds = invoice.Lines.Select(l => l.FreightCalculationId).ToList();
        var dates = (await db.Calculations.AsNoTracking().Where(c => calcIds.Contains(c.Id)).Select(c => c.Input).ToListAsync(cancellationToken))
            .Where(i => i is not null).Select(i => i!.Date).ToList();
        var months = dates.Count == 0
            ? [invoice.InvoiceDate]
            : dates.Select(d => new DateOnly(d.Year, d.Month, 1)).Distinct().Order().ToList();
        var label = string.Join(" to ", new[] { months[0], months[^1] }.Distinct().Select(m => m.ToString("MMMM yyyy", CultureInfo.InvariantCulture)));
        var shortLabel = string.Join(" to ", new[] { months[0], months[^1] }.Distinct().Select(m => m.ToString("MMM-yyyy", CultureInfo.InvariantCulture)));

        // The HSN/SAC codes kept on the customer's CFAs and distributors; the agreement's code is the fallback for a customer with none.
        var codes = await db.Locations.AsNoTracking().Where(l => l.CustomerId == invoice.CustomerId && l.SacCode != null && l.SacCode != string.Empty)
            .Select(l => l.SacCode!).Distinct().OrderBy(c => c).ToListAsync(cancellationToken);
        var hsn = codes.Count > 0 ? string.Join(", ", codes) : invoice.Sac;

        // The customer as the master has it now; the address the invoice was raised with is the fallback. The state is printed on its own line.
        var address = customer is null
            ? invoice.BillingAddress
            : string.Join(", ", new[] { customer.BillingLine1, customer.BillingCity, customer.BillingPincode }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(address))
        {
            address = invoice.BillingAddress;
        }

        var lrs = invoice.Lines.OrderBy(l => l.LineNumber).Select(l => l.LrNumber).Distinct().ToList();
        var taxable = invoice.Lines.Sum(l => l.Taxable);
        return new InvoicePrintDto(
            supplier.SupplierName ?? string.Empty, supplier.SupplierAddress, supplier.SupplierGstin, supplier.SupplierState, supplier.LogoKey is not null, supplier.SignatureKey is not null,
            string.IsNullOrWhiteSpace(supplier.Declaration) ? InvoiceSetting.StandardDeclaration : supplier.Declaration,
            invoice.CustomerName, address, invoice.CustomerGstin, customer?.BillingState,
            invoice.Number, invoice.InvoiceDate, $"Transportation Charges for Month {shortLabel}", $"Transportation Charges For the Month of {label}", lrs, hsn,
            taxable, invoice.Cgst, invoice.Sgst, invoice.Igst, invoice.TaxMode, invoice.TaxRatePercent, invoice.TaxNote, invoice.GrandTotal, AmountInWords.Rupees(invoice.GrandTotal), invoice.Status);
    }
}

/// <summary>The organisation's logo and signature for the printed invoice: stored as files, replaced through the upload and never through the settings form.</summary>
internal sealed class InvoiceBrandingHandler(IOpsSettings settings, FileIntake files, OpsAccess access)
{
    public const long MaxBytes = 512 * 1024;

    public static bool IsKind(string kind) => kind is "logo" or "signature";

    public async Task<Result> UploadAsync(string kind, Stream content, string? fileName, CancellationToken cancellationToken)
    {
        if (!IsKind(kind) || access.Tenant() is not { IsSuccess: true } tenant)
        {
            return Error.NotFound("ops.branding_unknown", "Unknown image.");
        }

        var blob = await files.SaveAsync(content, fileName, "branding", MaxBytes, cancellationToken);
        if (blob.IsFailure)
        {
            return blob.Error;
        }

        if (!blob.Value.ContentType.StartsWith("image/", StringComparison.Ordinal))
        {
            await files.DeleteAsync(blob.Value.Key, cancellationToken);
            return Error.Validation("ops.branding_type", "Upload a PNG or JPEG picture.");
        }

        var current = await settings.GetAsync(OpsSettingKeys.Invoice, new InvoiceSetting(), cancellationToken);
        var old = kind == "logo" ? current.LogoKey : current.SignatureKey;
        await settings.SaveAsync(OpsSettingKeys.Invoice, kind == "logo" ? current with { LogoKey = blob.Value.Key } : current with { SignatureKey = blob.Value.Key }, tenant.Value, cancellationToken);
        if (old is not null)
        {
            await files.DeleteAsync(old, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> RemoveAsync(string kind, CancellationToken cancellationToken)
    {
        if (!IsKind(kind) || access.Tenant() is not { IsSuccess: true } tenant)
        {
            return Error.NotFound("ops.branding_unknown", "Unknown image.");
        }

        var current = await settings.GetAsync(OpsSettingKeys.Invoice, new InvoiceSetting(), cancellationToken);
        var old = kind == "logo" ? current.LogoKey : current.SignatureKey;
        await settings.SaveAsync(OpsSettingKeys.Invoice, kind == "logo" ? current with { LogoKey = null } : current with { SignatureKey = null }, tenant.Value, cancellationToken);
        if (old is not null)
        {
            await files.DeleteAsync(old, cancellationToken);
        }

        return Result.Success();
    }

    public async Task<(Stream Stream, string ContentType)?> OpenAsync(string kind, CancellationToken cancellationToken)
    {
        if (!IsKind(kind))
        {
            return null;
        }

        var current = await settings.GetAsync(OpsSettingKeys.Invoice, new InvoiceSetting(), cancellationToken);
        var key = kind == "logo" ? current.LogoKey : current.SignatureKey;
        if (key is null || await files.OpenAsync(key, cancellationToken) is not { } stream)
        {
            return null;
        }

        return (stream, key.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
    }
}
