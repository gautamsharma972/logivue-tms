using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Tracking.Application.Engine;

/// <summary>Raises and clears alerts, and turns the ones that need an owner into exceptions. An alert can clear on its own; an exception has to be closed by someone.</summary>
internal interface ITrackingAlertService
{
    /// <summary>The standing conditions of the trip: how late it is projected to be.</summary>
    Task EvaluateAsync(TrackingContext context, CancellationToken cancellationToken);

    Task RaiseAsync(TrackingContext context, AlertType type, string key, string message, Severity? severity, CancellationToken cancellationToken);

    Task ResolveAsync(TrackingContext context, string keyPrefix, string note, CancellationToken cancellationToken);
}

internal sealed class TrackingAlertService(TrackingDbContext db, ISequenceGenerator sequences, TimeProvider clock) : ITrackingAlertService
{
    public async Task EvaluateAsync(TrackingContext context, CancellationToken cancellationToken)
    {
        var shipment = context.Shipment;
        if (!shipment.IsActive)
        {
            return;
        }

        // How late it is projected to be, as one condition at a time: an earlier, milder alert is closed when a worse one replaces it, and all of them when the trip is back on time.
        var prefix = $"eta:{shipment.Id:N}:";
        var current = shipment.Risk;
        var (type, level) = current switch
        {
            RiskStatus.AtRisk => (AlertType.EtaAtRisk, "atrisk"),
            RiskStatus.Delayed => (AlertType.EtaDelayed, "delayed"),
            RiskStatus.SeverelyDelayed => (AlertType.DeliverySlaRisk, "severe"),
            _ => (default(AlertType), string.Empty),
        };

        foreach (var alert in context.Alerts.Where(a => a.DedupeKey.StartsWith(prefix, StringComparison.Ordinal) && a.Status != AlertStatus.Resolved && !a.DedupeKey.StartsWith($"{prefix}{level}:", StringComparison.Ordinal)))
        {
            ResolveAlert(context, alert, "The projected delay has changed.");
        }

        if (level.Length == 0)
        {
            return;
        }

        var delay = shipment.DelayMinutes;
        var eta = shipment.CurrentEtaAt is { } e ? e.ToOffset(Clock.India).ToString("dd MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "unknown";
        var message = current switch
        {
            RiskStatus.AtRisk => $"{shipment.ShipmentReference} may be late: expected {eta}, {delay} min after plan.",
            RiskStatus.Delayed => $"{shipment.ShipmentReference} is projected {delay} min late: expected {eta}.",
            _ => $"The delivery commitment for {shipment.ShipmentReference} is now projected to be missed by {delay} min: expected {eta}.",
        };

        var episode = context.Alerts.Count(a => a.DedupeKey.StartsWith($"{prefix}{level}:", StringComparison.Ordinal));
        var open = context.Alerts.FirstOrDefault(a => a.DedupeKey.StartsWith($"{prefix}{level}:", StringComparison.Ordinal) && a.Status != AlertStatus.Resolved);
        await RaiseAsync(context, type, open?.DedupeKey ?? $"{prefix}{level}:{episode}", message, null, cancellationToken);
    }

    public async Task RaiseAsync(TrackingContext context, AlertType type, string key, string message, Severity? severity, CancellationToken cancellationToken)
    {
        var rule = context.Settings.Alerts.RuleFor(type);
        var level = severity ?? rule.Severity;
        var existing = context.Alerts.FirstOrDefault(a => a.DedupeKey == key);
        if (existing is not null)
        {
            if (existing.Status != AlertStatus.Resolved)
            {
                existing.Worsen(level, message);
            }

            return;
        }

        var now = clock.GetUtcNow();
        var alert = TrackingAlert.Raise(context.Shipment, type, level, message, key, now, rule.DueMinutes);
        db.Alerts.Add(alert);
        context.Alerts.Add(alert);

        if (rule.CreatesException)
        {
            var number = $"TEX-{await sequences.NextAsync(context.TenantId, "tracking_exception", cancellationToken):D5}";
            var exception = TrackingException.Raise(number, context.Shipment, alert, now, rule.DueMinutes);
            alert.LinkException(exception.Id);
            db.Exceptions.Add(exception);
            context.Exceptions.Add(exception);
        }

        context.Notifications.Add(new TrackingNotification(context.TenantId, type.ToString(), $"{type}: {context.Shipment.ShipmentReference}", message, level, context.Shipment.ShipmentId, context.Shipment.TripReference, context.Shipment.TransporterId));
    }

    public Task ResolveAsync(TrackingContext context, string keyPrefix, string note, CancellationToken cancellationToken)
    {
        foreach (var alert in context.Alerts.Where(a => a.DedupeKey.StartsWith(keyPrefix, StringComparison.Ordinal) && a.Status != AlertStatus.Resolved).ToList())
        {
            ResolveAlert(context, alert, note);
        }

        return Task.CompletedTask;
    }

    private void ResolveAlert(TrackingContext context, TrackingAlert alert, string note)
    {
        var now = clock.GetUtcNow();
        alert.Resolve(note, now);
        if (alert.ExceptionId is { } id && context.Exceptions.FirstOrDefault(e => e.Id == id) is { } exception)
        {
            exception.ConditionCleared(now);
        }
    }
}
