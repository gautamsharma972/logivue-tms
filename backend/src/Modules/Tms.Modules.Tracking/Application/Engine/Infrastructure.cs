using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;

namespace Tms.Modules.Tracking.Application.Engine;

/// <summary>Where events wait, within one request, until they are attached to the trip and saved with it through the outbox.</summary>
internal sealed class PendingEvents
{
    private readonly List<IDomainEvent> _events = [];

    public void Add(IDomainEvent trackingEvent) => _events.Add(trackingEvent);

    public IReadOnlyList<IDomainEvent> Drain()
    {
        var all = _events.ToList();
        _events.Clear();
        return all;
    }
}

/// <summary>Publishes tracking events. Initially in-process through the transactional outbox; the day it moves to a broker, only this class changes.</summary>
public interface ITrackingEventPublisher
{
    Task PublishAsync(TrackingEvent trackingEvent, CancellationToken cancellationToken);
}

internal sealed class OutboxTrackingEventPublisher(PendingEvents pending) : ITrackingEventPublisher
{
    public Task PublishAsync(TrackingEvent trackingEvent, CancellationToken cancellationToken)
    {
        pending.Add(trackingEvent);
        return Task.CompletedTask;
    }
}

internal sealed class OutboxTrackingTransporterIntegration(PendingEvents pending) : ITrackingTransporterIntegration
{
    public Task PublishTrackingPerformanceEventAsync(TrackingPerformanceEvent eventData, CancellationToken cancellationToken)
    {
        pending.Add(eventData);
        return Task.CompletedTask;
    }
}

internal sealed class OutboxTrackingDeliveryIntegration(PendingEvents pending) : ITrackingDeliveryIntegration
{
    public Task PublishDeliveryMilestoneAsync(DeliveryTrackingEvent eventData, CancellationToken cancellationToken)
    {
        pending.Add(eventData);
        return Task.CompletedTask;
    }
}

/// <summary>Pushes a change to people who have the control tower open. Failing to push must never fail the work that was already saved.</summary>
public interface ITrackingLiveNotifier
{
    Task PushAsync(Guid tenantId, string kind, object payload, Guid? transporterId, CancellationToken cancellationToken);
}

internal sealed class NullTrackingLiveNotifier : ITrackingLiveNotifier
{
    public Task PushAsync(Guid tenantId, string kind, object payload, Guid? transporterId, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Tells people about something: in-app first (pushed to whoever is watching), with room for email, SMS, WhatsApp and push to be added behind the same interface.</summary>
public interface ITrackingNotificationService
{
    Task NotifyAsync(TrackingNotification notification, CancellationToken cancellationToken);
}

internal sealed class InAppTrackingNotificationService(ITrackingLiveNotifier live) : ITrackingNotificationService
{
    public Task NotifyAsync(TrackingNotification notification, CancellationToken cancellationToken) =>
        live.PushAsync(notification.TenantId, "notification", new
        {
            kind = notification.Kind, title = notification.Title, message = notification.Message, severity = notification.Severity.ToString(), shipmentId = notification.ShipmentId,
            tripReference = notification.TripReference,
        }, notification.TransporterId, cancellationToken);
}
