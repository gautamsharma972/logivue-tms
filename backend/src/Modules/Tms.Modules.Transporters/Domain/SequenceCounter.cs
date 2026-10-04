namespace Tms.Modules.Transporters.Domain;

/// <summary>Per-tenant running number behind references like TR-00042. Incremented with an atomic SQL upsert, never via tracking.</summary>
public sealed class SequenceCounter
{
    public Guid TenantId { get; init; }

    public string Name { get; init; } = null!;

    public long Value { get; init; }
}
