using System.Reflection;
using Tms.SharedKernel.Domain;

namespace Tms.SharedKernel.Messaging;

/// <summary>Contributes one assembly's domain-event types to the registry used to rehydrate outbox messages.</summary>
public sealed record DomainEventTypeSource(Assembly Assembly);

/// <summary>Maps the stored event type name back to its CLR type.</summary>
public sealed class DomainEventRegistry
{
    private readonly Dictionary<string, Type> _types;

    public DomainEventRegistry(IEnumerable<DomainEventTypeSource> sources) =>
        _types = sources
            .SelectMany(s => s.Assembly.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .DistinctBy(t => t.FullName)
            .ToDictionary(t => t.FullName!, StringComparer.Ordinal);

    public bool TryGet(string name, out Type type) => _types.TryGetValue(name, out type!);
}
