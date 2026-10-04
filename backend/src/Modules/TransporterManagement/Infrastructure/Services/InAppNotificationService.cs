using LogiVue.Tms.TransporterManagement.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Services;

/// <summary>
/// MVP notification channel: writes to the structured log. Replace with a persisted in-app inbox
/// and provider adapters (email, SMS, WhatsApp) behind the same <see cref="INotificationService"/> contract.
/// </summary>
internal sealed class LoggingNotificationService(ILogger<LoggingNotificationService> logger) : INotificationService
{
    public Task SendAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Notification {Channel} to {Recipient} for {EntityType} {EntityId}: {Subject}",
            request.Channel, request.Recipient, request.EntityType, request.EntityId, request.Subject);

        return Task.CompletedTask;
    }
}
