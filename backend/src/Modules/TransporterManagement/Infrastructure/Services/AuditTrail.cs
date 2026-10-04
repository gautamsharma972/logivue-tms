using System.Diagnostics;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.TransporterManagement.Domain.Audit;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Services;

/// <summary>
/// Queues audit rows into <c>tm_transporter_audit_logs</c>, stamped with the caller and trace id.
/// The row is written by the caller's <c>SaveChangesAsync</c>, so audit and business change commit together.
/// </summary>
internal sealed class AuditTrail(TransporterDbContext db, ICurrentUser currentUser, ILogger<AuditTrail> logger) : IAuditTrail
{
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        db.AuditLogs.Add(new TransporterAuditLog
        {
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            OldValueJson = entry.OldValueJson,
            NewValueJson = entry.NewValueJson,
            Reason = entry.Reason,
            PerformedBy = currentUser.UserId,
            PerformedAt = DateTime.UtcNow,
            CorrelationId = Activity.Current?.TraceId.ToString()
        });

        logger.LogInformation("Audit {EntityType} {EntityId} {Action} by {UserId}",
            entry.EntityType, entry.EntityId, entry.Action, currentUser.UserId);

        return Task.CompletedTask;
    }
}
