using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Exceptions;

/// <summary>Raises delivery exceptions with the severity and due date the tenant's rules set. One open exception per type per delivery: a second sighting adds nothing.</summary>
internal sealed class ExceptionFactory(DeliveriesDbContext db, IDeliverySettings settings, ISequenceGenerator sequences, ICurrentUser user, TimeProvider clock)
{
    public async Task<DeliveryException?> RaiseAsync(Delivery delivery, Guid? podId, ExceptionType type, string description, CancellationToken cancellationToken, ExceptionSeverity? severity = null)
    {
        if (user.TenantId is not { } tenantId)
        {
            return null;
        }

        var already = db.Exceptions.Local.Any(e => e.DeliveryId == delivery.Id && e.ExceptionType == type && e.IsOpen)
            || await db.Exceptions.AnyAsync(e => e.DeliveryId == delivery.Id && e.ExceptionType == type && e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed, cancellationToken);
        if (already)
        {
            return null;
        }

        var rules = await settings.GetAsync<ExceptionRulesSetting>(DeliverySettingKeys.Exceptions, cancellationToken);
        var level = severity ?? (rules.Severity.TryGetValue(type.ToString(), out var configured) && Enum.TryParse<ExceptionSeverity>(configured, true, out var parsed) ? parsed : ExceptionSeverity.Medium);
        var now = clock.GetUtcNow();
        var number = $"EXC-{await sequences.NextAsync(tenantId, "delivery-exception", cancellationToken):D5}";
        var exception = DeliveryException.Raise(tenantId, number, delivery, podId, type, level, description, now.AddHours(rules.DueHours), now);
        db.Exceptions.Add(exception);
        return exception;
    }

    /// <summary>What a completed delivery leaves behind: a shortage, damage, goods not accepted, quantities that do not add up, a late arrival.</summary>
    public async Task AfterCompletionAsync(Delivery d, Guid? podId, CancellationToken cancellationToken)
    {
        if (d.Items.Sum(i => i.ShortQuantity) > 0)
        {
            await RaiseAsync(d, podId, ExceptionType.Shortage, $"Short by {d.Items.Sum(i => i.ShortQuantity):0.##} on {d.Number}: {Detail(d, i => i.ShortQuantity)}.", cancellationToken);
        }

        if (d.Items.Sum(i => i.DamagedQuantity) > 0)
        {
            await RaiseAsync(d, podId, ExceptionType.Damage, $"Damaged {d.Items.Sum(i => i.DamagedQuantity):0.##} on {d.Number}: {Detail(d, i => i.DamagedQuantity)}.", cancellationToken);
        }

        if (d.Items.Sum(i => i.RejectedQuantity) > 0)
        {
            await RaiseAsync(d, podId, ExceptionType.PartialDelivery,
                $"{d.Items.Sum(i => i.RejectedQuantity):0.##} not delivered on {d.Number}; to be handled as: {d.RemainingDisposition}.", cancellationToken);
        }

        if (d.HasQuantityMismatch)
        {
            await RaiseAsync(d, podId, ExceptionType.QuantityMismatch, $"The quantities reported for {d.Number} do not add up to what was dispatched.", cancellationToken);
        }

        if (d.WindowEnd is { } end && d.ActualDeliveryAt is { } at && at > end)
        {
            await RaiseAsync(d, podId, ExceptionType.LateDelivery, $"{d.Number} was delivered {(at - end).TotalMinutes:0} minutes after its window closed.", cancellationToken);
        }
    }

    private static string Detail(Delivery d, Func<DeliveryItem, decimal> pick) =>
        string.Join(", ", d.Items.Where(i => pick(i) > 0).Select(i => $"{i.SkuReference} {pick(i):0.##}"));
}
