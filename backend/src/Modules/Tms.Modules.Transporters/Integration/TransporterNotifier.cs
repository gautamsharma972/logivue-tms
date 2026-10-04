using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Transporters.Integration;

/// <summary>
/// Tells a transporter about something that needs it: an offered load, a closed offer. Goes to the primary contact's email, else the company's. A mail
/// problem is logged and never fails the shipment flow that caused it.
/// </summary>
internal sealed class TransporterNotifier(TransportersDbContext db, IEmailSender email, ILogger<TransporterNotifier> logger)
{
    public async Task NotifyAsync(Guid transporterId, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            var contact = await db.Contacts.AsNoTracking()
                .Where(c => c.TransporterId == transporterId && c.IsActive && c.IsPrimary && c.Email != null)
                .Select(c => new { c.Email, c.Name }).FirstOrDefaultAsync(cancellationToken);
            var transporter = await db.Transporters.AsNoTracking().Where(t => t.Id == transporterId).Select(t => new { t.Email, t.ContactPerson }).FirstOrDefaultAsync(cancellationToken);
            var to = contact?.Email ?? transporter?.Email;
            if (to is null)
            {
                return;
            }

            await email.SendAsync(new EmailMessage(to, subject, $"Hello {contact?.Name ?? transporter?.ContactPerson},\n\n{body}\n\nSign in to the vendor portal to respond."), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not notify transporter {TransporterId}: {Subject}", transporterId, subject);
        }
    }
}
