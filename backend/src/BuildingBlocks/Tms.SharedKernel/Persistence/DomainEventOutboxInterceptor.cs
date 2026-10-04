using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Messaging;
using Tms.SharedKernel.Security;
using Tms.SharedKernel.Telemetry;

namespace Tms.SharedKernel.Persistence;

/// <summary>
/// Transactional outbox for domain events. Inside SaveChanges it records every raised event as an
/// <see cref="OutboxMessage"/> (atomic with the data change); after commit it delivers them right away and marks
/// them processed. Anything that fails is left for the background worker.
/// </summary>
internal sealed class DomainEventOutboxInterceptor(
    IDomainEventDispatcher dispatcher,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<DomainEventOutboxInterceptor> logger) : SaveChangesInterceptor
{
    private sealed record Pending(OutboxMessage Message, IDomainEvent Event);

    private readonly ConditionalWeakTable<DbContext, List<Pending>> _pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        DeliverAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await DeliverAsync(eventData.Context);
        return result;
    }

    private void Forget(DbContext? context)
    {
        if (context is not null)
        {
            _pending.Remove(context);
        }
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var already = context.ChangeTracker.Entries<OutboxMessage>().Select(e => e.Entity.Id).ToHashSet();
        var captured = new List<Pending>();
        var now = clock.GetUtcNow();
        var minAge = TimeSpan.FromSeconds(options.Value.MinAgeSeconds);

        foreach (var source in context.ChangeTracker.Entries<IHasDomainEvents>().Select(e => e.Entity).Where(e => e.DomainEvents.Count > 0).ToList())
        {
            foreach (var domainEvent in source.DomainEvents)
            {
                if (already.Contains(domainEvent.EventId))
                {
                    continue; // left over from an earlier failed save of the same context
                }

                var message = new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    TenantId = (source as ITenantScoped)?.TenantId ?? currentUser.TenantId,
                    UserId = currentUser.UserId,
                    Type = domainEvent.GetType().FullName!,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), OutboxJson.Options),
                    OccurredAt = domainEvent.OccurredAt,
                    NextAttemptAt = now + minAge,
                };
                context.Set<OutboxMessage>().Add(message);
                captured.Add(new Pending(message, domainEvent));
            }
        }

        if (captured.Count > 0)
        {
            _pending.AddOrUpdate(context, captured);
        }
    }

    private async Task DeliverAsync(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending))
        {
            return;
        }

        _pending.Remove(context);
        context.ChangeTracker.Entries<IHasDomainEvents>().Select(e => e.Entity).ToList().ForEach(e => e.ClearDomainEvents());

        foreach (var item in pending)
        {
            try
            {
                await dispatcher.DispatchAsync([item.Event]);
                await context.Set<OutboxMessage>().Where(m => m.Id == item.Message.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, clock.GetUtcNow()));
                TmsTelemetry.OutboxDispatched.Add(1);
            }
            catch (Exception ex)
            {
                // The change is already committed and the user's request must not fail because of a subscriber;
                // the worker will retry. Record the attempt so backoff and dead-lettering apply.
                logger.LogError(ex, "Inline delivery of {EventType} {EventId} failed; the outbox worker will retry", item.Message.Type, item.Message.Id);
                TmsTelemetry.OutboxFailed.Add(1);
                var retryAt = clock.GetUtcNow() + options.Value.RetryDelay(1);
                var error = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
                await context.Set<OutboxMessage>().Where(m => m.Id == item.Message.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Attempts, 1).SetProperty(m => m.LastError, error).SetProperty(m => m.NextAttemptAt, retryAt));
            }
        }
    }
}
