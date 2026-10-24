using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.TransportOps.Application;

internal static class OpsErrors
{
    public static readonly Error Forbidden = Error.Forbidden("ops.forbidden", "You are not allowed to do that.");

    public static readonly Error NoTenant = Error.Forbidden("ops.no_tenant", "No organisation is selected.");

    public static readonly Error CustomerNotFound = Error.NotFound("ops.customer_not_found", "Customer not found.");

    public static readonly Error LocationNotFound = Error.NotFound("ops.location_not_found", "Location not found.");

    public static readonly Error AgreementNotFound = Error.NotFound("ops.agreement_not_found", "Agreement not found.");

    public static readonly Error RequestNotFound = Error.NotFound("ops.request_not_found", "Delivery request not found.");

    public static readonly Error LrNotFound = Error.NotFound("ops.lr_not_found", "LR not found.");

    public static readonly Error TripNotFound = Error.NotFound("ops.trip_not_found", "Trip not found.");

    public static readonly Error PodNotFound = Error.NotFound("ops.pod_not_found", "POD not found.");

    public static readonly Error CalcNotFound = Error.NotFound("ops.calc_not_found", "Calculation not found.");

    public static readonly Error InvoiceNotFound = Error.NotFound("ops.invoice_not_found", "Invoice not found.");

    public static readonly Error FileNotFound = Error.NotFound("ops.file_not_found", "File not found.");

    /// <summary>Every failure on the public QR page looks the same: a person holding a bad or old code learns nothing.</summary>
    public static readonly Error QrInvalid = Error.NotFound("ops.qr_invalid", "This code is not valid. Please ask the office for a new LR.");

    public static Error ConcurrencyConflict => Error.Conflict("ops.concurrency", "Someone else changed this record. Reload it and try again.");
}

/// <summary>Who is calling and what they may do. The endpoints already check the permission; this is for the few rules that depend on who the caller is.</summary>
internal sealed class OpsAccess(ICurrentUser user)
{
    public Guid? UserId => user.UserId;

    public Guid? TenantId => user.TenantId;

    public bool Has(string permission) => user.Permissions.Contains(permission);

    public Result<Guid> Tenant() => TenantId is { } t ? t : OpsErrors.NoTenant;
}

internal interface IOpsSettings
{
    Task<T> GetAsync<T>(string key, T fallback, CancellationToken cancellationToken = default);

    Task SaveAsync<T>(string key, T value, Guid tenantId, CancellationToken cancellationToken = default);
}

internal sealed class OpsSettings(TransportOpsDbContext db) : IOpsSettings
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string key, T fallback, CancellationToken cancellationToken = default)
    {
        var json = await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.ValueJson).FirstOrDefaultAsync(cancellationToken);
        if (json is null)
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public async Task SaveAsync<T>(string key, T value, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, Json);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (row is null)
        {
            db.Settings.Add(OpsSetting.Create(tenantId, key, json));
        }
        else
        {
            row.Set(json);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static class Tokens
{
    /// <summary>A long random secret for the printed QR code. 32 random bytes, 43 URL-safe characters.</summary>
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool LooksValid(string token) => token.Length is >= 32 and <= 64 && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

internal static class Clock
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    /// <summary>Today's business date in India, for effective dates and invoice dates.</summary>
    public static DateOnly TodayInIndia(this TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(Ist).DateTime);

    public static DateOnly BusinessDate(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(Ist).DateTime);
}

internal static class Numbers
{
    public static string Format(string prefix, long sequence, int digits) => $"{prefix}-{sequence.ToString("D" + digits, System.Globalization.CultureInfo.InvariantCulture)}";
}
