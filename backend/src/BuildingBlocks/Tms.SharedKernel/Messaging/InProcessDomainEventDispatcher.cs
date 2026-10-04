using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Tms.SharedKernel.Domain;

namespace Tms.SharedKernel.Messaging;

internal sealed class InProcessDomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, (Type HandlerType, Func<object, object, CancellationToken, Task> Invoke)> Cache = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in events)
        {
            var (handlerType, invoke) = Cache.GetOrAdd(domainEvent.GetType(), BuildInvoker);
            foreach (var handler in services.GetServices(handlerType))
            {
                await invoke(handler!, domainEvent, cancellationToken);
            }
        }
    }

    private static (Type, Func<object, object, CancellationToken, Task>) BuildInvoker(Type eventType)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
        return (handlerType, async (handler, evt, ct) =>
        {
            try
            {
                await (Task)method.Invoke(handler, [evt, ct])!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                // Reflection wraps what the handler threw; surface the real exception so logs and the outbox keep the real message.
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        });
    }
}
