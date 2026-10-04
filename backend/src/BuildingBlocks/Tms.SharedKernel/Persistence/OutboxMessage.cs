using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tms.SharedKernel.Persistence;

/// <summary>Serializer settings shared by the writer (interceptor) and reader (worker) of outbox payloads.</summary>
public static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// A domain event persisted in the same transaction as the change that raised it. It is delivered immediately after
/// commit; if that fails, a background worker retries with backoff until it succeeds or is dead-lettered.
/// Subscribers must therefore be idempotent.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? UserId { get; init; }

    /// <summary>Full CLR name of the event type; resolved through the event registry.</summary>
    public required string Type { get; init; }

    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Not eligible for the worker before this time (gives the inline delivery a head start).</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? DeadLetteredAt { get; set; }
}

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollSeconds { get; init; } = 5;

    /// <summary>How long a new message waits before the worker may pick it up.</summary>
    public int MinAgeSeconds { get; init; } = 10;

    public int FirstRetrySeconds { get; init; } = 30;

    public int MaxAttempts { get; init; } = 8;

    public int BatchSize { get; init; } = 25;

    /// <summary>Backoff after the Nth failed attempt: 30s, 2m, 10m, 1h, 6h… (first value is configurable).</summary>
    public TimeSpan RetryDelay(int attempt)
    {
        var first = TimeSpan.FromSeconds(Math.Max(1, FirstRetrySeconds));
        return TimeSpan.FromTicks(first.Ticks * (long)Math.Pow(4, Math.Clamp(attempt - 1, 0, 6)));
    }
}
