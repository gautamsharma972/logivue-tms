using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application;

/// <summary>
/// Staff read and manage; a vendor-portal user (tied to a transporter, holding the execute permission) sees and runs only its own company's deliveries.
/// Another company's delivery is reported as not found, never as forbidden.
/// </summary>
internal sealed class DeliveryAccess(ICurrentUser user)
{
    public bool IsVendor => user.TransporterId is not null;

    public Guid? VendorTransporterId => user.TransporterId;

    public Guid? UserId => user.UserId;

    private bool Has(string permission) => user.Permissions.Contains(permission);

    public bool CanRead => !IsVendor && (Has(DeliveryPermissions.Read) || Has(DeliveryPermissions.Manage) || Has(DeliveryPermissions.PodReview));

    public bool CanManage => !IsVendor && Has(DeliveryPermissions.Manage);

    public bool CanReview => !IsVendor && Has(DeliveryPermissions.PodReview);

    public bool CanManageExceptions => !IsVendor && Has(DeliveryPermissions.ExceptionsManage);

    public bool CanConfigure => !IsVendor && Has(DeliveryPermissions.Configure);

    /// <summary>Vendors with the execute permission, or staff acting on a transporter's behalf (a phone call from the driver).</summary>
    public bool CanExecute => IsVendor ? Has(DeliveryPermissions.Execute) : Has(DeliveryPermissions.Manage);

    public bool CanSee(Delivery delivery) =>
        IsVendor ? Has(DeliveryPermissions.Execute) && delivery.TransporterId == VendorTransporterId : CanRead;

    public bool CanSeeTransporter(Guid? transporterId) =>
        IsVendor ? Has(DeliveryPermissions.Execute) && transporterId == VendorTransporterId : CanRead;

    public Actor Actor(string? device) => new(user.UserId, device);

    public static readonly Error Forbidden = Error.Forbidden("deliveries.forbidden", "You are not allowed to do that.");

    public static readonly Error DeliveryNotFound = Error.NotFound("deliveries.not_found", "Delivery not found.");

    public static readonly Error PodNotFound = Error.NotFound("pods.not_found", "Proof of delivery not found.");

    public static readonly Error ExceptionNotFound = Error.NotFound("exceptions.not_found", "Exception not found.");
}

internal interface IDeliverySettings
{
    Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);
}

internal sealed class DeliverySettings(DeliveriesDbContext db) : IDeliverySettings
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var json = await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.ValueJson).FirstOrDefaultAsync(cancellationToken);
        if (json is not null)
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException($"Setting '{key}' could not be read as {typeof(T).Name}.");
        }

        return DeliverySettingDefaults.For(key) is T value ? value : throw new InvalidOperationException($"Setting '{key}' has no default of type {typeof(T).Name}.");
    }
}

internal static class Clock
{
    public static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    public static DateOnly TodayInIndia(this TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(India).DateTime);
}
