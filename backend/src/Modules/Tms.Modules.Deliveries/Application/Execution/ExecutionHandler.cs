using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application.Deliveries;
using Tms.Modules.Deliveries.Application.Exceptions;
using Tms.Modules.Deliveries.Application.Pods;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Deliveries.Application.Execution;

/// <summary>
/// The driver's side of a delivery: start, arrive, try, complete, refuse, fail; and the staff moves around them (reschedule, cancel, close). Every action carries where it
/// happened and from which device, and is checked on the server whatever the app already checked.
/// </summary>
internal sealed class ExecutionHandler(
    DeliveriesDbContext db, DeliveryAccess access, IDeliverySettings settings, DeliveryMapper mapper, ExceptionFactory exceptions, PodEngine engine, OtpService otp, TimeProvider clock)
{
    public Task<Result<DeliveryDto>> StartAsync(Guid id, DeviceContext? context, CancellationToken ct) =>
        RunAsync(id, execute: true, async (d, now) =>
        {
            var started = d.Start(Fix(context), access.Actor(context?.DeviceReference), When(context, now));
            return started;
        }, ct);

    public Task<Result<DeliveryDto>> ArriveAsync(Guid id, DeviceContext? context, CancellationToken ct) =>
        RunAsync(id, execute: true, async (d, now) =>
        {
            var actor = access.Actor(context?.DeviceReference);
            var arrived = d.Arrive(Fix(context), actor, When(context, now));
            if (arrived.IsFailure)
            {
                return arrived;
            }

            // A one-time code is sent as the vehicle arrives, when the tenant asks for it.
            var rules = await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, ct);
            if (rules.OtpRequired)
            {
                await otp.IssueAsync(d, actor, now, ct);
            }

            return arrived;
        }, ct);

    public Task<Result<DeliveryDto>> IssueOtpAsync(Guid id, DeviceContext? context, CancellationToken ct) =>
        RunAsync(id, execute: true, async (d, now) =>
        {
            if (d.Status is not (DeliveryStatus.Arrived or DeliveryStatus.Attempted))
            {
                return Error.Conflict("deliveries.invalid_state", $"A code can only be issued once the vehicle has arrived; this delivery is {d.Status}.");
            }

            await otp.IssueAsync(d, access.Actor(context?.DeviceReference), now, ct);
            return Result.Success();
        }, ct);

    public Task<Result<DeliveryDto>> VerifyOtpAsync(Guid id, VerifyOtpRequest request, CancellationToken ct) =>
        RunAsync(id, execute: true, async (d, now) =>
        {
            var rules = await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, ct);
            var verified = d.VerifyOtp(request.Code, rules.OtpMaxAttempts, access.Actor(request.Context?.DeviceReference), When(request.Context, now));

            // A wrong code must still count as an attempt, so the failure is saved before it is reported.
            if (verified.IsFailure && d.OtpAttempts > 0)
            {
                await db.SaveChangesAsync(ct);
            }

            return verified;
        }, ct);

    public Task<Result<DeliveryDto>> AttemptAsync(Guid id, AttemptRequest request, CancellationToken ct) =>
        RunAsync(id, execute: true, async (d, now) =>
        {
            if (await ReasonErrorAsync(DeliverySettingKeys.AttemptReasons, request.ReasonCode, ct) is { } bad)
            {
                return bad;
            }

            return d.RecordFailedAttempt(request.ReasonCode, request.DriverRemarks, request.CustomerRemarks, request.RecipientName, Fix(request.Context), access.Actor(request.Context?.DeviceReference), When(request.Context, now));
        }, ct);

    public Task<Result<DeliveryDto>> FailAsync(Guid id, FailDeliveryRequest request, CancellationToken ct) =>
        RunAsync(id, execute: true, async (d, now) =>
        {
            if (await ReasonErrorAsync(DeliverySettingKeys.AttemptReasons, request.ReasonCode, ct) is { } bad)
            {
                return bad;
            }

            var failed = d.Fail(request.ReasonCode, request.Remarks, Fix(request.Context), access.Actor(request.Context?.DeviceReference), When(request.Context, now));
            if (failed.IsSuccess)
            {
                var description = $"{d.Number} could not be delivered: {request.ReasonCode.Trim().ToUpperInvariant()}{(string.IsNullOrWhiteSpace(request.Remarks) ? string.Empty : $" ({request.Remarks.Trim()})")}.";
                await exceptions.RaiseAsync(d, null, request.ReasonCode.Trim().Equals("ADDRESS_INCORRECT", StringComparison.OrdinalIgnoreCase) ? ExceptionType.AddressIssue : ExceptionType.DeliveryFailed, description, ct);
            }

            return failed;
        }, ct);

    public Task<Result<DeliveryDto>> RefuseAsync(Guid id, RefuseDeliveryRequest request, CancellationToken ct) =>
        RunAsync(id, execute: true, async (d, now) =>
        {
            if (await ReasonErrorAsync(DeliverySettingKeys.RefusalReasons, request.ReasonCode, ct) is { } bad)
            {
                return bad;
            }

            var refused = d.Refuse(request.ReasonCode, request.RecipientName, request.Remarks, Fix(request.Context), access.Actor(request.Context?.DeviceReference), When(request.Context, now));
            if (refused.IsFailure)
            {
                return refused;
            }

            var rules = await settings.GetAsync<DiscrepancyRulesSetting>(DeliverySettingKeys.Discrepancy, ct);
            if (rules.RefusalAcknowledgementRequired && !request.CustomerAcknowledged)
            {
                return Error.Validation("deliveries.acknowledgement_required", "The customer must acknowledge the refusal.");
            }

            if (request.CustomerAcknowledged)
            {
                d.AcknowledgeDiscrepancies();
            }

            await exceptions.RaiseAsync(d, null, ExceptionType.CustomerRefusal,
                $"{request.RecipientName ?? d.CustomerName} refused {d.Number}: {request.ReasonCode.Trim().ToUpperInvariant()}{(string.IsNullOrWhiteSpace(request.Remarks) ? string.Empty : $" ({request.Remarks.Trim()})")}.", ct);
            return refused;
        }, ct);

    public Task<Result<DeliveryDto>> CompleteAsync(Guid id, CompleteDeliveryRequest request, CancellationToken ct) =>
        RunAsync(id, execute: true, async (d, now) =>
        {
            var quantity = await settings.GetAsync<QuantityRulesSetting>(DeliverySettingKeys.Quantity, ct);
            var podRules = await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, ct);
            var fix = Fix(request.Context);
            var actor = access.Actor(request.Context?.DeviceReference);

            if (await db.Pods.AnyAsync(p => p.DeliveryId == d.Id && p.IsCurrent, ct))
            {
                return Error.Conflict("deliveries.already_completed", "This delivery already has a proof of delivery.");
            }

            var completed = d.Complete(
                request.Outcome, request.Items.Select(i => i.ToQuantities()).ToList(), request.RemainingDisposition, When(request.Context, now), request.DriverRemarks, request.Proof.RecipientName,
                fix, quantity, actor, now);
            if (completed.IsFailure)
            {
                return completed.Error;
            }

            // A damage type or shortage reason must be one the tenant recognises.
            var damageTypes = await settings.GetAsync<List<ReasonSetting>>(DeliverySettingKeys.DamageTypes, ct);
            var shortageReasons = await settings.GetAsync<List<ReasonSetting>>(DeliverySettingKeys.ShortageReasons, ct);
            foreach (var item in d.Items)
            {
                if (item.ShortQuantity > 0 && shortageReasons.All(r => !string.Equals(r.Code, item.ShortageReasonCode, StringComparison.OrdinalIgnoreCase)))
                {
                    return Error.Validation("deliveries.shortage_reason_unknown", $"'{item.ShortageReasonCode}' is not a known shortage reason.");
                }

                if (item.DamagedQuantity > 0 && damageTypes.All(r => !string.Equals(r.Code, item.DamageType, StringComparison.OrdinalIgnoreCase)))
                {
                    return Error.Validation("deliveries.damage_type_unknown", $"'{item.DamageType}' is not a known damage type.");
                }
            }

            var geofence = Geo.Check(d.CustomerLatitude, d.CustomerLongitude, d.GeofenceRadiusM, fix.Latitude, fix.Longitude, fix.AccuracyM, podRules.MaxGpsAccuracyM);
            var pod = PodRecord.Create(d, $"POD-{d.Number["DLV-".Length..]}", fix, geofence, request.DriverRemarks, now);
            var proof = request.Proof;
            if (proof.Method == ProofMethod.Contactless && !podRules.ContactlessAllowed)
            {
                return Error.Validation("pods.contactless_not_allowed", "Contactless delivery is not allowed for this tenant.");
            }

            var set = pod.SetProof(proof.Method, proof.RecipientName, proof.RecipientDesignation, proof.RecipientPhone, proof.RecipientRemarks, proof.DriverConfirmed);
            if (set.IsFailure)
            {
                return set.Error;
            }

            pod.CopyOtp(d);
            if (proof.CustomerAcknowledged)
            {
                pod.AcknowledgeByCustomer();
                d.AcknowledgeDiscrepancies();
            }

            await engine.RefreshAsync(pod, ct);
            db.Pods.Add(pod);
            await exceptions.AfterCompletionAsync(d, pod.Id, ct);
            return Result.Success();
        }, ct);

    public Task<Result<DeliveryDto>> RescheduleAsync(Guid id, RescheduleRequest request, CancellationToken ct) =>
        RunAsync(id, execute: false, (d, now) => Task.FromResult(access.CanManage
            ? d.Reschedule(request.PlannedDeliveryAt, request.WindowStart, request.WindowEnd, access.Actor(null), now)
            : Result.Failure(DeliveryAccess.Forbidden)), ct);

    public Task<Result<DeliveryDto>> CancelAsync(Guid id, ReasonRequest request, CancellationToken ct) =>
        RunAsync(id, execute: false, (d, now) => Task.FromResult(access.CanManage ? d.Cancel(request.Reason, access.Actor(null), now) : Result.Failure(DeliveryAccess.Forbidden)), ct);

    public Task<Result<DeliveryDto>> CloseAsync(Guid id, ReasonRequest request, CancellationToken ct) =>
        RunAsync(id, execute: false, (d, now) => Task.FromResult(access.CanManage ? d.Close(request.Reason, access.Actor(null), now) : Result.Failure(DeliveryAccess.Forbidden)), ct);

    private async Task<Result<DeliveryDto>> RunAsync(Guid id, bool execute, Func<Delivery, DateTimeOffset, Task<Result>> act, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.WithAll().AsSplitQuery().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (delivery is null || !access.CanSee(delivery))
        {
            return DeliveryAccess.DeliveryNotFound;
        }

        if (execute && !access.CanExecute)
        {
            return DeliveryAccess.Forbidden;
        }

        var result = await act(delivery, clock.GetUtcNow());
        if (result.IsFailure)
        {
            db.ChangeTracker.Clear(); // a refused action must leave nothing half-done behind, even if more work follows in the same request (a sync batch)
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(delivery, cancellationToken);
    }

    private async Task<Error?> ReasonErrorAsync(string key, string code, CancellationToken cancellationToken)
    {
        var reasons = await settings.GetAsync<List<ReasonSetting>>(key, cancellationToken);
        return reasons.Any(r => string.Equals(r.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase))
            ? null
            : Error.Validation("deliveries.reason_unknown", $"'{code}' is not a recognised reason.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["reasonCode"] = ["Choose a reason from the list."] },
            };
    }

    private static GeoFix Fix(DeviceContext? context) => context?.Fix?.ToFix() ?? GeoFix.None;

    /// <summary>When it happened: the device's clock if it gave one (it may have been offline), never later than now.</summary>
    private static DateTimeOffset When(DeviceContext? context, DateTimeOffset now) => context?.At is { } at && at < now ? at : now;
}
