using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Messaging;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;
using Tms.SharedKernel.Telemetry;

namespace Tms.Modules.Platform.Infrastructure.Messaging;

/// <summary>
/// Delivers outbox messages that inline delivery missed or that failed: claims a batch, runs each message as the
/// tenant/user that caused it, and applies exponential backoff, dead-lettering after the configured attempts.
/// </summary>
internal sealed class OutboxProcessor(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, TimeProvider clock, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    // Messages are claimed (their next attempt pushed out) so a second instance or a slow handler cannot double-deliver.
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)));
        do
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox batch failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        List<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var now = clock.GetUtcNow();
            due = await db.OutboxMessages.AsNoTracking()
                .Where(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= now)
                .OrderBy(m => m.OccurredAt)
                .Select(m => m.Id)
                .Take(options.Value.BatchSize)
                .ToListAsync(cancellationToken);
        }

        foreach (var id in due)
        {
            await DeliverAsync(id, cancellationToken);
        }
    }

    private async Task DeliverAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<PlatformDbContext>();
        var now = clock.GetUtcNow();

        // Claim: only one worker wins the update.
        var claimed = await db.OutboxMessages
            .Where(m => m.Id == id && m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, now + Lease).SetProperty(m => m.Attempts, m => m.Attempts + 1), cancellationToken);
        if (claimed == 0)
        {
            return;
        }

        var message = await db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id, cancellationToken);
        try
        {
            var registry = services.GetRequiredService<DomainEventRegistry>();
            if (!registry.TryGet(message.Type, out var type))
            {
                await DeadLetterAsync(db, id, $"Unknown event type {message.Type}", cancellationToken);
                return;
            }

            var domainEvent = (IDomainEvent)JsonSerializer.Deserialize(message.Payload, type, OutboxJson.Options)!;
            services.GetRequiredService<IAmbientUserContext>().RunAs(message.TenantId, message.UserId, $"outbox:{id:N}");
            await services.GetRequiredService<IDomainEventDispatcher>().DispatchAsync([domainEvent], cancellationToken);

            await db.OutboxMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, clock.GetUtcNow()).SetProperty(m => m.LastError, (string?)null), cancellationToken);
            TmsTelemetry.OutboxDispatched.Add(1);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            TmsTelemetry.OutboxFailed.Add(1);
            var error = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            // message.Attempts already includes the attempt that just failed (it was counted when the message was claimed).
            if (message.Attempts >= options.Value.MaxAttempts)
            {
                logger.LogError(ex, "Outbox message {Id} ({Type}) dead-lettered after {Attempts} attempts", id, message.Type, message.Attempts);
                await DeadLetterAsync(db, id, error, CancellationToken.None);
                return;
            }

            var next = clock.GetUtcNow() + options.Value.RetryDelay(message.Attempts);
            logger.LogWarning(ex, "Outbox message {Id} ({Type}) failed on attempt {Attempt}; retrying at {Next:u}", id, message.Type, message.Attempts, next);
            await db.OutboxMessages.Where(m => m.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastError, error).SetProperty(m => m.NextAttemptAt, next), CancellationToken.None);
        }
    }

    private async Task DeadLetterAsync(PlatformDbContext db, Guid id, string error, CancellationToken cancellationToken)
    {
        TmsTelemetry.OutboxDeadLettered.Add(1);
        await db.OutboxMessages.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DeadLetteredAt, clock.GetUtcNow()).SetProperty(m => m.LastError, error), cancellationToken);
    }
}
