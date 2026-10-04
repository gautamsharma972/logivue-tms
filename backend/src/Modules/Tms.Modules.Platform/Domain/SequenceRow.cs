namespace Tms.Modules.Platform.Domain;

/// <summary>Shared per-tenant running number (see <c>ISequenceGenerator</c>). Incremented with an atomic SQL upsert, never via tracking.</summary>
public sealed class SequenceRow
{
    public Guid TenantId { get; init; }

    public string Name { get; init; } = null!;

    public long Value { get; init; }
}
