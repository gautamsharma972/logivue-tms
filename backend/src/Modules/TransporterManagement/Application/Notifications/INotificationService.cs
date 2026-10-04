namespace LogiVue.Tms.TransporterManagement.Application.Notifications;

/// <summary>
/// Channel-agnostic notification abstraction. MVP ships with an in-app/log implementation;
/// email, SMS and WhatsApp channels plug in behind this interface.
/// </summary>
public interface INotificationService
{
    Task SendAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

public enum NotificationChannel { InApp, Email, Sms, WhatsApp }

public record NotificationRequest(
    NotificationChannel Channel,
    string Recipient,
    string Subject,
    string Body,
    string? EntityType = null,
    string? EntityId = null);
