using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Tms.SharedKernel.Telemetry;

/// <summary>Application-level metrics and traces. Exported via OpenTelemetry when an endpoint is configured.</summary>
public static class TmsTelemetry
{
    public const string Name = "Tms";

    public static readonly ActivitySource Activities = new(Name);

    private static readonly Meter Meter = new(Name);

    public static readonly Counter<long> OutboxDispatched = Meter.CreateCounter<long>("tms.outbox.dispatched", description: "Domain events delivered to subscribers");

    public static readonly Counter<long> OutboxFailed = Meter.CreateCounter<long>("tms.outbox.failed", description: "Failed delivery attempts");

    public static readonly Counter<long> OutboxDeadLettered = Meter.CreateCounter<long>("tms.outbox.dead_lettered", description: "Events given up on after the maximum attempts");

    public static readonly Counter<long> LoginFailures = Meter.CreateCounter<long>("tms.auth.login_failures", description: "Rejected sign-in attempts");

    public static readonly Counter<long> LoginSuccesses = Meter.CreateCounter<long>("tms.auth.logins", description: "Successful sign-ins");
}
