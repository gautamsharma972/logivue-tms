using Tms.SharedKernel.Domain;

namespace Tms.Modules.Deliveries.Domain;

/// <summary>A tenant's own value for one setting. Absent means the default in <see cref="DeliverySettingDefaults"/> applies.</summary>
public sealed class DeliverySetting : AggregateRoot, ITenantScoped
{
    private DeliverySetting()
    {
    }

    public Guid TenantId { get; private set; }

    public string Key { get; private set; } = null!;

    public string ValueJson { get; private set; } = "{}";

    public static DeliverySetting Create(Guid tenantId, string key, string json) => new() { TenantId = tenantId, Key = key, ValueJson = json };

    public void Change(string json) => ValueJson = json;
}

/// <summary>
/// One command from a mobile device, remembered by the key the device chose. A retry of the same command returns what the first one returned and changes
/// nothing, so a flaky connection cannot complete a delivery twice or add a second proof.
/// </summary>
public sealed class SyncRecord : AggregateRoot, ITenantScoped
{
    private SyncRecord()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>The device's own id for this command (or the Idempotency-Key header of an upload).</summary>
    public string ClientRecordId { get; private set; } = null!;

    public string? DeviceId { get; private set; }

    public string Operation { get; private set; } = null!;

    public Guid? DeliveryId { get; private set; }

    public Guid? ServerRecordId { get; private set; }

    public SyncStatus SyncStatus { get; private set; }

    public int SyncAttempt { get; private set; }

    public DateTimeOffset? ClientCreatedAt { get; private set; }

    public DateTimeOffset? ClientUpdatedAt { get; private set; }

    public DateTimeOffset LastAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>The response given the first time, replayed on a retry.</summary>
    public string? ResultJson { get; private set; }

    public static SyncRecord Begin(Guid tenantId, string clientRecordId, string? deviceId, string operation, Guid? deliveryId, DateTimeOffset? clientCreatedAt, DateTimeOffset? clientUpdatedAt, DateTimeOffset now) => new()
    {
        TenantId = tenantId, ClientRecordId = clientRecordId, DeviceId = deviceId, Operation = operation, DeliveryId = deliveryId, ClientCreatedAt = clientCreatedAt, ClientUpdatedAt = clientUpdatedAt,
        SyncStatus = SyncStatus.Pending, SyncAttempt = 1, LastAttemptAt = now,
    };

    public void Retry(DateTimeOffset now)
    {
        SyncAttempt++;
        LastAttemptAt = now;
    }

    public void Succeed(Guid? serverRecordId, string? resultJson)
    {
        SyncStatus = SyncStatus.Synced;
        ServerRecordId = serverRecordId;
        ResultJson = resultJson;
        LastError = null;
    }

    public void Fail(string error, SyncStatus status = SyncStatus.Failed)
    {
        SyncStatus = status;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}

public enum NotificationKind
{
    PodOverdue = 1,
    PodReviewOverdue = 2,
    PodRejected = 3,
    ResubmissionOverdue = 4,
    DeliveryFailed = 5,
    ShortageRecorded = 6,
    DamageRecorded = 7,
    CustomerRefusal = 8,
    DeliveryDelayed = 9,
    ExceptionEscalated = 10,
}

/// <summary>
/// Something a person should see: aimed at one transporter's users, or at staff holding a permission. The dedupe key means a condition that stays true
/// (a proof that is still overdue) notifies once. Read state is per user.
/// </summary>
public sealed class DeliveryNotification : Entity, ITenantScoped
{
    private DeliveryNotification()
    {
    }

    public Guid TenantId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public Guid? DeliveryId { get; private set; }

    public Guid? PodId { get; private set; }

    public Guid? ExceptionId { get; private set; }

    /// <summary>Set: the users of this transporter. Null: staff, by <see cref="AudiencePermission"/>.</summary>
    public Guid? AudienceTransporterId { get; private set; }

    public string? AudiencePermission { get; private set; }

    public string DedupeKey { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static DeliveryNotification Create(
        Guid tenantId, NotificationKind kind, string title, string body, Guid? deliveryId, Guid? podId, Guid? exceptionId, Guid? transporterId, string? permission, string dedupeKey, DateTimeOffset now) => new()
    {
        TenantId = tenantId, Kind = kind, Title = title, Body = body, DeliveryId = deliveryId, PodId = podId, ExceptionId = exceptionId, AudienceTransporterId = transporterId,
        AudiencePermission = transporterId is null ? permission : null, DedupeKey = dedupeKey, CreatedAt = now,
    };
}

public sealed class NotificationRead : Entity, ITenantScoped
{
    private NotificationRead()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid NotificationId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset ReadAt { get; private set; }

    public static NotificationRead Create(Guid tenantId, Guid notificationId, Guid userId, DateTimeOffset now) => new() { TenantId = tenantId, NotificationId = notificationId, UserId = userId, ReadAt = now };
}

/// <summary>What was handed to the claims system for one discrepancy, kept so a claims module (or a person) can pick it up and nobody retypes it.</summary>
public sealed class ClaimHandoff : Entity, ITenantScoped
{
    private ClaimHandoff()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid DeliveryId { get; private set; }

    public Guid? DiscrepancyId { get; private set; }

    public string Reference { get; private set; } = null!;

    public string System { get; private set; } = null!;

    public string PayloadJson { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static ClaimHandoff Create(Guid tenantId, Guid deliveryId, Guid? discrepancyId, string reference, string system, string json, DateTimeOffset now) =>
        new() { TenantId = tenantId, DeliveryId = deliveryId, DiscrepancyId = discrepancyId, Reference = reference, System = system, PayloadJson = json, CreatedAt = now };
}

/// <summary>A message to another system (freight audit) that the local adapter keeps until a real one is connected, so nothing is lost and eligibility can be read.</summary>
public sealed class IntegrationMessage : Entity, ITenantScoped
{
    private IntegrationMessage()
    {
    }

    public Guid TenantId { get; private set; }

    public string Target { get; private set; } = null!;

    public string Kind { get; private set; } = null!;

    public Guid DeliveryId { get; private set; }

    public Guid? PodId { get; private set; }

    public string PayloadJson { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static IntegrationMessage Create(Guid tenantId, string target, string kind, Guid deliveryId, Guid? podId, string json, DateTimeOffset now) =>
        new() { TenantId = tenantId, Target = target, Kind = kind, DeliveryId = deliveryId, PodId = podId, PayloadJson = json, CreatedAt = now };
}
