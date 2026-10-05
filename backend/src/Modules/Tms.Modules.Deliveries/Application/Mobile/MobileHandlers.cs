using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application.Deliveries;
using Tms.Modules.Deliveries.Application.Execution;
using Tms.Modules.Deliveries.Application.Pods;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Mobile;

/// <summary>What a device needs to work without a connection: the rules and reason lists, and the deliveries to be done with their proofs.</summary>
public sealed record MobileConfigDto(
    PodRulesSetting Pod, IReadOnlyList<ReasonSetting> AttemptReasons, IReadOnlyList<ReasonSetting> ShortageReasons, IReadOnlyList<ReasonSetting> DamageTypes,
    IReadOnlyList<ReasonSetting> RefusalReasons, QuantityRulesSetting Quantity, DiscrepancyRulesSetting Discrepancy, ImageRulesSetting Images);

public sealed record MobileDeliveryDto(DeliveryDto Delivery, PodDto? Pod);

public sealed record MobileBundleDto(IReadOnlyList<MobileDeliveryDto> Deliveries, MobileConfigDto Config, DateTimeOffset DownloadedAt);

/// <summary>One thing the driver did on the device. <c>ClientRecordId</c> is the device's own key: sending it again changes nothing.</summary>
public sealed record SyncCommand(string ClientRecordId, string Type, Guid DeliveryId, DateTimeOffset? ClientCreatedAt, DateTimeOffset? ClientUpdatedAt, JsonElement? Payload);

public sealed record MobileSyncRequest(string? DeviceId, IReadOnlyList<SyncCommand> Commands);

public sealed record SyncResultDto(string ClientRecordId, SyncStatus Status, bool Duplicate, int Attempt, string? Error, string? ErrorCode, DeliveryStatus? DeliveryStatus, Guid? PodId);

public sealed record MobileSyncResponse(IReadOnlyList<SyncResultDto> Results);

public static class SyncOperations
{
    public const string Start = "start";
    public const string Arrive = "arrive";
    public const string Attempt = "attempt";
    public const string Complete = "complete";
    public const string Fail = "fail";
    public const string Refuse = "refuse";
    public const string OtpVerify = "otp-verify";
    public const string OtpIssue = "otp-issue";

    public static IReadOnlyList<string> All { get; } = [Start, Arrive, Attempt, Complete, Fail, Refuse, OtpVerify, OtpIssue];
}

internal sealed class MobileSyncRequestValidator : AbstractValidator<MobileSyncRequest>
{
    public MobileSyncRequestValidator()
    {
        RuleFor(x => x.Commands).NotNull().Must(c => c is { Count: > 0 and <= 100 }).WithMessage("Send between 1 and 100 commands.");
        RuleForEach(x => x.Commands).ChildRules(c =>
        {
            c.RuleFor(x => x.ClientRecordId).NotEmpty().MaximumLength(100);
            c.RuleFor(x => x.Type).Must(t => SyncOperations.All.Contains(t)).WithMessage("Unknown command type.");
            c.RuleFor(x => x.DeliveryId).NotEmpty();
        });
    }
}

internal sealed class MobileHandler(
    DeliveriesDbContext db, DeliveryAccess access, IDeliverySettings settings, DeliveryMapper mapper, ExecutionHandler execution, ICurrentUser user, TimeProvider clock)
{
    // The device sends the same JSON the API takes: enums as names.
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly DeliveryStatus[] Open = [DeliveryStatus.Assigned, DeliveryStatus.EnRoute, DeliveryStatus.Arrived, DeliveryStatus.Attempted];

    private static readonly PodStatus[] Unfinished = [PodStatus.Draft, PodStatus.Captured, PodStatus.Rejected, PodStatus.ResubmissionRequired];

    /// <summary>The deliveries to be done (or whose proof still needs work) for the transporter, with everything needed to capture them offline.</summary>
    public async Task<Result<MobileBundleDto>> DownloadAsync(Guid? transporterId, CancellationToken cancellationToken)
    {
        var scope = access.IsVendor ? access.VendorTransporterId : transporterId;
        if (scope is not { } mine || !access.CanSeeTransporter(mine) || !access.CanExecute)
        {
            return access.CanExecute ? Error.Validation("mobile.transporter_required", "Say which transporter's deliveries to download.") : DeliveryAccess.Forbidden;
        }

        var deliveries = await db.Deliveries.AsNoTracking().WithAll().AsSplitQuery()
            .Where(d => d.TransporterId == mine && (Open.Contains(d.Status)
                || ((d.Status == DeliveryStatus.Delivered || d.Status == DeliveryStatus.PartiallyDelivered) && db.Pods.Any(p => p.DeliveryId == d.Id && p.IsCurrent && Unfinished.Contains(p.Status)))))
            .OrderBy(d => d.PlannedDeliveryAt).Take(200).ToListAsync(cancellationToken);

        var ids = deliveries.Select(d => d.Id).ToList();
        var pods = (await db.Pods.AsNoTracking().WithAll().AsSplitQuery().Where(p => ids.Contains(p.DeliveryId) && p.IsCurrent).ToListAsync(cancellationToken)).ToDictionary(p => p.DeliveryId);
        var result = new List<MobileDeliveryDto>();
        foreach (var d in deliveries)
        {
            result.Add(new MobileDeliveryDto(await mapper.ToDtoAsync(d, cancellationToken), pods.TryGetValue(d.Id, out var pod) ? await mapper.ToDtoAsync(pod, d, cancellationToken) : null));
        }

        return new MobileBundleDto(result, await ConfigAsync(cancellationToken), clock.GetUtcNow());
    }

    public async Task<MobileConfigDto> ConfigAsync(CancellationToken cancellationToken) => new(
        await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, cancellationToken),
        await settings.GetAsync<List<ReasonSetting>>(DeliverySettingKeys.AttemptReasons, cancellationToken),
        await settings.GetAsync<List<ReasonSetting>>(DeliverySettingKeys.ShortageReasons, cancellationToken),
        await settings.GetAsync<List<ReasonSetting>>(DeliverySettingKeys.DamageTypes, cancellationToken),
        await settings.GetAsync<List<ReasonSetting>>(DeliverySettingKeys.RefusalReasons, cancellationToken),
        await settings.GetAsync<QuantityRulesSetting>(DeliverySettingKeys.Quantity, cancellationToken),
        await settings.GetAsync<DiscrepancyRulesSetting>(DeliverySettingKeys.Discrepancy, cancellationToken),
        await settings.GetAsync<ImageRulesSetting>(DeliverySettingKeys.Images, cancellationToken));

    /// <summary>
    /// Applies what the device did while offline, in order. Each command is remembered by its key: one the server already applied is answered from memory, one that
    /// failed is tried again, and a command that no longer fits (the delivery moved on elsewhere) is reported as a conflict rather than forced.
    /// </summary>
    public async Task<Result<MobileSyncResponse>> SyncAsync(MobileSyncRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanExecute || user.TenantId is not { } tenantId)
        {
            return DeliveryAccess.Forbidden;
        }

        var results = new List<SyncResultDto>();
        foreach (var command in request.Commands)
        {
            results.Add(await ApplyAsync(tenantId, request.DeviceId, command, cancellationToken));
        }

        return new MobileSyncResponse(results);
    }

    private async Task<SyncResultDto> ApplyAsync(Guid tenantId, string? deviceId, SyncCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var record = await db.SyncRecords.FirstOrDefaultAsync(s => s.ClientRecordId == command.ClientRecordId, cancellationToken);
        if (record is { SyncStatus: SyncStatus.Synced })
        {
            return new SyncResultDto(command.ClientRecordId, SyncStatus.Synced, true, record.SyncAttempt, null, null, null, record.ServerRecordId);
        }

        if (record is null)
        {
            record = SyncRecord.Begin(tenantId, command.ClientRecordId, deviceId, command.Type, command.DeliveryId, command.ClientCreatedAt, command.ClientUpdatedAt, now);
            db.SyncRecords.Add(record);
        }
        else
        {
            record.Retry(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        var recordId = record.Id;

        var outcome = await DispatchAsync(command, deviceId, cancellationToken);
        var saved = await db.SyncRecords.FirstAsync(s => s.Id == recordId, cancellationToken);
        if (outcome.IsSuccess)
        {
            var podId = await db.Pods.AsNoTracking().Where(p => p.DeliveryId == command.DeliveryId && p.IsCurrent).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
            saved.Succeed(podId ?? command.DeliveryId, JsonSerializer.Serialize(new { status = outcome.Value.Summary.Status.ToString() }));
            await db.SaveChangesAsync(cancellationToken);
            return new SyncResultDto(command.ClientRecordId, SyncStatus.Synced, false, saved.SyncAttempt, null, null, outcome.Value.Summary.Status, podId);
        }

        var status = outcome.Error.Type == ErrorType.Conflict ? SyncStatus.Conflict : SyncStatus.Failed;
        saved.Fail(outcome.Error.Description, status);
        await db.SaveChangesAsync(cancellationToken);
        return new SyncResultDto(command.ClientRecordId, status, false, saved.SyncAttempt, outcome.Error.Description, outcome.Error.Code, null, null);
    }

    private async Task<Result<DeliveryDto>> DispatchAsync(SyncCommand command, string? deviceId, CancellationToken ct)
    {
        T? Read<T>() where T : class => command.Payload is { ValueKind: JsonValueKind.Object } p ? p.Deserialize<T>(PayloadJson) : null;

        // The device's own clock and id travel with every command, so the timeline shows when it really happened.
        DeviceContext Context(DeviceContext? given) => new(given?.Fix, given?.DeviceReference ?? deviceId, given?.At ?? command.ClientCreatedAt);

        try
        {
            switch (command.Type)
            {
                case SyncOperations.Start:
                    return await execution.StartAsync(command.DeliveryId, Context(Read<DeviceContext>()), ct);
                case SyncOperations.Arrive:
                    return await execution.ArriveAsync(command.DeliveryId, Context(Read<DeviceContext>()), ct);
                case SyncOperations.OtpIssue:
                    return await execution.IssueOtpAsync(command.DeliveryId, Context(Read<DeviceContext>()), ct);
                case SyncOperations.OtpVerify:
                {
                    var body = Read<VerifyOtpRequest>();
                    return body is null ? Bad() : await execution.VerifyOtpAsync(command.DeliveryId, body with { Context = Context(body.Context) }, ct);
                }

                case SyncOperations.Attempt:
                {
                    var body = Read<AttemptRequest>();
                    return body is null ? Bad() : await execution.AttemptAsync(command.DeliveryId, body with { Context = Context(body.Context) }, ct);
                }

                case SyncOperations.Fail:
                {
                    var body = Read<FailDeliveryRequest>();
                    return body is null ? Bad() : await execution.FailAsync(command.DeliveryId, body with { Context = Context(body.Context) }, ct);
                }

                case SyncOperations.Refuse:
                {
                    var body = Read<RefuseDeliveryRequest>();
                    return body is null ? Bad() : await execution.RefuseAsync(command.DeliveryId, body with { Context = Context(body.Context) }, ct);
                }

                case SyncOperations.Complete:
                {
                    var body = Read<CompleteDeliveryRequest>();
                    return body is null || body.Items is null || body.Proof is null ? Bad() : await execution.CompleteAsync(command.DeliveryId, body with { Context = Context(body.Context) }, ct);
                }

                default:
                    return Bad();
            }
        }
        catch (JsonException)
        {
            return Bad();
        }
    }

    private static Error Bad() => Error.Validation("mobile.payload_invalid", "The command could not be read.");
}
