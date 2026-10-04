namespace LogiVue.Tms.Shared.Audit;

/// <summary>
/// Records business-significant changes. Implementations persist to the module's audit store.
/// Callers describe what changed; they never write audit rows directly.
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <param name="EntityType">Logical entity name, e.g. "Transporter".</param>
/// <param name="EntityId">Identifier of the affected entity.</param>
/// <param name="Action">Business action, e.g. "Approved", "DocumentVerified".</param>
/// <param name="OldValueJson">Serialised state before the change, when relevant.</param>
/// <param name="NewValueJson">Serialised state after the change, when relevant.</param>
/// <param name="Reason">Mandatory for approvals, rejections, overrides and rule changes.</param>
public sealed record AuditEntry(
    string EntityType,
    string EntityId,
    string Action,
    string? OldValueJson = null,
    string? NewValueJson = null,
    string? Reason = null);
