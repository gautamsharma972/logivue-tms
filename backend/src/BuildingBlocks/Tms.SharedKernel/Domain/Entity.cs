namespace Tms.SharedKernel.Domain;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

/// <summary>Row belongs to exactly one tenant. Enforced by a global query filter and on save.</summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}

/// <summary>Creation / modification stamps, populated centrally by the audit interceptor.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    Guid? CreatedBy { get; }

    DateTimeOffset? ModifiedAt { get; }

    Guid? ModifiedBy { get; }
}

public abstract class Entity : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public override bool Equals(object? obj) =>
        obj is Entity other && GetType() == other.GetType() && Id == other.Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// Consistency boundary. Carries audit stamps and an optimistic-concurrency <see cref="Version"/>
/// that the audit interceptor increments on every update.
/// </summary>
public abstract class AggregateRoot : Entity, IAuditable
{
    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public long Version { get; private set; }
}
