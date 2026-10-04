namespace Tms.SharedKernel.Persistence;

public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
}

/// <summary>
/// Append-only record of a data change. Mapped into every module's DbContext so it is written in the
/// same transaction as the change itself; the table is owned (migrated) by exactly one module.
/// </summary>
public sealed class AuditLog
{
    public long Id { get; private set; }

    public Guid? TenantId { get; init; }

    public Guid? UserId { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public AuditAction Action { get; init; }

    /// <summary>JSON object: property name → { old, new }.</summary>
    public string? Changes { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string? TraceId { get; init; }

    public string? IpAddress { get; init; }
}
