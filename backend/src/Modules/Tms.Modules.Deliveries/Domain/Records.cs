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
