using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.TransportOps.Application;

public static class QrActions
{
    public const string None = "None";
    public const string ConfirmArrival = "ConfirmArrival";
    public const string ConfirmUnloading = "ConfirmUnloading";
    public const string UploadPod = "UploadPod";
}

/// <summary>Everything the driver's page shows. Nothing about price, other customers or other deliveries.</summary>
public sealed record QrViewDto(
    string LrNumber, string CustomerName, string? VehicleRegistration, string CfaName, string DestinationName, string DestinationCity, string Material, DeliveryStatus Status, string StatusText, PodStatus PodStatus,
    DateTimeOffset? ArrivedAt, DateTimeOffset? UnloadedAt, string NextAction, string Message, string? PodRejectionReason, int MaxFiles, int MaxFileMb, bool CanMarkPodPending, DateTimeOffset ServerTime);

/// <summary>The phone's position, sent only when the driver taps Confirm Arrival. <see cref="LocationError"/> is "denied" or "unavailable" when the phone could not provide one.</summary>
public sealed record ArrivalRequest(double? Latitude, double? Longitude, double? AccuracyM, DateTimeOffset? CapturedAt, string? LocationError, string? ClientKey);

/// <summary>Unloading carries no location at all.</summary>
public sealed record UnloadingRequest(string? ClientKey);

public sealed record PodPendingRequest(string? ClientKey);

public sealed record UploadedFile(string FileName, Stream Content);

internal sealed class ArrivalRequestValidator : AbstractValidator<ArrivalRequest>
{
    public ArrivalRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
        RuleFor(x => x.AccuracyM).GreaterThanOrEqualTo(0).When(x => x.AccuracyM.HasValue);
        RuleFor(x => x.LocationError).MaximumLength(30);
        RuleFor(x => x.ClientKey).MaximumLength(100);
    }
}

/// <summary>
/// What a driver can do with the QR code printed on an LR. There is no sign-in: possession of the secret is the permission, and it allows exactly these steps on exactly this LR, each
/// checked against the LR's current state on the server. The arrival step is the only one that looks at a location. Every failure of the code itself looks the same.
/// </summary>
internal sealed class QrHandler(TransportOpsDbContext db, IAmbientUserContext ambient, IOpsSettings settings, FileIntake files, TimeProvider clock)
{
    private static readonly TimeSpan StaleLocation = TimeSpan.FromMinutes(2);

    public async Task<Result<QrViewDto>> ViewAsync(string token, CancellationToken cancellationToken)
    {
        var lr = await OpenAsync(token, cancellationToken);
        return lr.IsFailure ? lr.Error : await ViewOfAsync(lr.Value, null, cancellationToken);
    }

    public async Task<Result<QrViewDto>> ArrivalAsync(string token, ArrivalRequest request, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(token, cancellationToken);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var lr = opened.Value;
        var now = clock.GetUtcNow();
        if (await Replayed(lr, request.ClientKey, cancellationToken))
        {
            return await ViewOfAsync(lr, null, cancellationToken);
        }

        if (lr.ArrivedAt is not null)
        {
            return await ViewOfAsync(lr, "Arrival was already recorded.", cancellationToken);
        }

        if (lr.Status != DeliveryStatus.InTransit)
        {
            return Error.Conflict("ops.arrival_state", lr.Status == DeliveryStatus.Cancelled ? "This LR is cancelled." : "Arrival can only be confirmed once the vehicle has left the CFA.");
        }

        var destination = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == lr.DestinationLocationId, cancellationToken);
        var rules = await settings.GetAsync(OpsSettingKeys.Arrival, new ArrivalSetting(), cancellationToken);
        var (decision, verification) = Decide(destination, rules, request, now);

        var evidence = DeliveryEvent.Create(lr.TenantId, lr.TripId, lr.Id, decision.Accepted ? EventType.DestinationArrival : EventType.ArrivalRejected, now, now, EventSource.QrWeb, null, null,
                Clean(request.ClientKey), lr.TokenRef)
            .WithArrivalEvidence(request.Latitude, request.Longitude, request.AccuracyM, request.CapturedAt, destination.Latitude, destination.Longitude, destination.GeofenceRadiusM, decision.DistanceM, verification);

        if (!decision.Accepted)
        {
            // Kept so a rejected attempt can be seen later; it never moves the LR.
            db.Events.Add(evidence.NotUnique());
            await db.SaveChangesAsync(cancellationToken);
            return Error.Validation(CodeFor(decision.Check, verification), decision.Message);
        }

        var arrived = lr.Arrive(now);
        if (arrived.IsFailure)
        {
            return arrived.Error;
        }

        if (lr.TripStopId is { } stopId && await db.TripStops.FirstOrDefaultAsync(s => s.Id == stopId, cancellationToken) is { } stop)
        {
            stop.Arrived(now);
        }

        db.Events.Add(evidence);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A second arrival for this LR (two taps, two phones) is refused by the database; the first one stands.
            db.ChangeTracker.Clear();
            ambient.RunAs(lr.TenantId, null, "qr-web");
            var current = await db.Lrs.AsNoTracking().Include(l => l.Lines).FirstAsync(l => l.Id == lr.Id, cancellationToken);
            return await ViewOfAsync(current, "Arrival was already recorded.", cancellationToken);
        }

        return await ViewOfAsync(lr, null, cancellationToken);
    }

    public async Task<Result<QrViewDto>> UnloadingAsync(string token, UnloadingRequest request, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(token, cancellationToken);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var lr = opened.Value;
        var now = clock.GetUtcNow();
        if (await Replayed(lr, request.ClientKey, cancellationToken))
        {
            return await ViewOfAsync(lr, null, cancellationToken);
        }

        if (lr.UnloadedAt is not null)
        {
            return await ViewOfAsync(lr, "Unloading was already recorded.", cancellationToken);
        }

        if (lr.Status != DeliveryStatus.Arrived)
        {
            return Error.Conflict("ops.unloading_state", lr.ArrivedAt is null ? "Unloading cannot be confirmed before your arrival has been confirmed." : "Unloading cannot be confirmed now.");
        }

        var done = lr.CompleteUnloading(now);
        if (done.IsFailure)
        {
            return done.Error;
        }

        if (lr.TripStopId is { } stopId && await db.TripStops.FirstOrDefaultAsync(s => s.Id == stopId, cancellationToken) is { } stop)
        {
            stop.Unloaded(now);
            if (lr.TripId is { } tripId && await db.Trips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == tripId, cancellationToken) is { } trip)
            {
                trip.CompleteIfAllUnloaded();
            }
        }

        // The delivery is finished, so the proof is now due.
        var pod = await db.Pods.FirstOrDefaultAsync(p => p.LrId == lr.Id, cancellationToken);
        if (pod is { Status: PodStatus.NotYetDue })
        {
            pod.MarkPending(now);
        }

        db.Events.Add(DeliveryEvent.Create(lr.TenantId, lr.TripId, lr.Id, EventType.UnloadingCompleted, now, now, EventSource.QrWeb, null, null, Clean(request.ClientKey), lr.TokenRef));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            ambient.RunAs(lr.TenantId, null, "qr-web");
            var current = await db.Lrs.AsNoTracking().Include(l => l.Lines).FirstAsync(l => l.Id == lr.Id, cancellationToken);
            return await ViewOfAsync(current, "Unloading was already recorded.", cancellationToken);
        }

        return await ViewOfAsync(lr, null, cancellationToken);
    }

    /// <summary>The driver says the POD is not available yet. No location, no files; the delivery stays complete and the proof stays pending.</summary>
    public async Task<Result<QrViewDto>> PodPendingAsync(string token, PodPendingRequest request, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(token, cancellationToken);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var lr = opened.Value;
        var now = clock.GetUtcNow();
        if (lr.Status != DeliveryStatus.UnloadingCompleted)
        {
            return Error.Conflict("ops.pod_state", "The proof of delivery can only be dealt with after unloading is confirmed.");
        }

        var pod = await db.Pods.FirstAsync(p => p.LrId == lr.Id, cancellationToken);
        if (await Replayed(lr, request.ClientKey, cancellationToken))
        {
            return await ViewOfAsync(lr, null, cancellationToken);
        }

        var marked = pod.MarkPending(now);
        if (marked.IsFailure)
        {
            return await ViewOfAsync(lr, "A proof of delivery has already been received for this LR.", cancellationToken);
        }

        db.Events.Add(DeliveryEvent.Create(lr.TenantId, lr.TripId, lr.Id, EventType.PodPending, now, now, EventSource.QrWeb, null, null, Clean(request.ClientKey), lr.TokenRef));
        await db.SaveChangesAsync(cancellationToken);
        return await ViewOfAsync(lr, null, cancellationToken);
    }

    /// <summary>The driver uploads the POD photos or PDF. Stored and linked to this LR; it is received, never accepted. No location is read.</summary>
    public async Task<Result<QrViewDto>> UploadPodAsync(string token, IReadOnlyList<UploadedFile> uploads, string? clientKey, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(token, cancellationToken);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var lr = opened.Value;
        if (lr.Status != DeliveryStatus.UnloadingCompleted)
        {
            return Error.Conflict("ops.pod_state", "The proof of delivery can only be uploaded after unloading is confirmed.");
        }

        var podRules = await settings.GetAsync(OpsSettingKeys.Pod, new PodSetting(), cancellationToken);
        if (uploads.Count == 0)
        {
            return Error.Validation("ops.pod_no_files", "Choose at least one photo or file.");
        }

        if (uploads.Count > podRules.EffectiveMaxFiles)
        {
            return Error.Validation("ops.pod_too_many", $"You can upload up to {podRules.EffectiveMaxFiles} files at a time.");
        }

        var pod = await db.Pods.Include(p => p.Documents).FirstAsync(p => p.LrId == lr.Id, cancellationToken);
        var saved = await PodUploads.StoreAsync(db, files, clock, lr, pod, uploads, PodSource.QrWeb, null, null, Clean(clientKey), podRules, cancellationToken);
        return saved.IsFailure ? saved.Error : await ViewOfAsync(lr, null, cancellationToken);
    }

    // The arrival decision. The location rules live here and nowhere else.
    private static (ArrivalDecision Decision, string Verification) Decide(OpsLocation destination, ArrivalSetting rules, ArrivalRequest request, DateTimeOffset now)
    {
        if (request.Latitude is null && request.LocationError is "denied")
        {
            return (new ArrivalDecision(ArrivalCheck.NoLocation, null, "Location permission was denied. Allow location access for this page in your browser settings and try again, or ask the office to confirm your arrival."), "PermissionDenied");
        }

        if (request.Latitude is not null && request.CapturedAt is { } captured && (now - captured > StaleLocation || captured - now > TimeSpan.FromMinutes(1)))
        {
            return (new ArrivalDecision(ArrivalCheck.NoLocation, null, "That location reading is too old. Tap Confirm Arrival again to take a fresh one."), "Stale");
        }

        var decision = GeofenceVerifier.Verify(destination.Latitude, destination.Longitude, destination.GeofenceRadiusM, request.Latitude, request.Longitude, request.AccuracyM, rules.MaxAccuracyM);
        return (decision, decision.Check.ToString());
    }

    private static string CodeFor(ArrivalCheck check, string verification) => verification switch
    {
        "PermissionDenied" => "ops.arrival_permission_denied",
        "Stale" => "ops.arrival_location_stale",
        _ => check switch
        {
            ArrivalCheck.Outside => "ops.arrival_outside",
            ArrivalCheck.AccuracyInsufficient => "ops.arrival_accuracy",
            ArrivalCheck.DestinationHasNoCoordinates => "ops.arrival_no_destination",
            _ => "ops.arrival_no_location",
        },
    };

    /// <summary>Finds the LR by the hash of the secret alone, then works as that LR's tenant. Every way the code can be wrong gives the same answer.</summary>
    private async Task<Result<Lr>> OpenAsync(string token, CancellationToken cancellationToken)
    {
        if (!Tokens.LooksValid(token))
        {
            return OpsErrors.QrInvalid;
        }

        var hash = Tokens.Hash(token);
        var found = await db.Lrs.IgnoreQueryFilters().Where(l => l.TokenHash == hash).Select(l => new { l.Id, l.TenantId }).FirstOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return OpsErrors.QrInvalid;
        }

        ambient.RunAs(found.TenantId, null, "qr-web");
        var lr = await db.Lrs.Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == found.Id, cancellationToken);
        return lr is null || !lr.TokenActiveAt(clock.GetUtcNow()) ? OpsErrors.QrInvalid : lr;
    }

    /// <summary>A retry that carries the key of an action already recorded changes nothing and gets the current state back.</summary>
    private async Task<bool> Replayed(Lr lr, string? clientKey, CancellationToken cancellationToken) =>
        Clean(clientKey) is { } key && await db.Events.AnyAsync(e => e.ClientKey == key && e.LrId == lr.Id, cancellationToken);

    private async Task<Result<QrViewDto>> ViewOfAsync(Lr lr, string? note, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == lr.CustomerId).Select(c => c.Name).FirstAsync(cancellationToken);
        var places = await db.Locations.AsNoTracking().Where(l => l.Id == lr.CfaLocationId || l.Id == lr.DestinationLocationId).ToListAsync(cancellationToken);
        var pod = await db.Pods.AsNoTracking().FirstOrDefaultAsync(p => p.LrId == lr.Id, cancellationToken);
        var podRules = await settings.GetAsync(OpsSettingKeys.Pod, new PodSetting(), cancellationToken);
        var podStatus = pod?.Status ?? PodStatus.NotYetDue;
        var (action, message) = Next(lr, podStatus, pod?.RejectionReason);
        var material = string.Join("; ", lr.Lines.Take(3).Select(l => $"{(string.IsNullOrWhiteSpace(l.MaterialName) ? "Goods" : l.MaterialName)} x {l.Quantity:0.##} {l.Unit}")) + (lr.Lines.Count > 3 ? "; …" : string.Empty);
        return new QrViewDto(lr.Number, customer, lr.VehicleRegistration, places.First(p => p.Id == lr.CfaLocationId).Name, places.First(p => p.Id == lr.DestinationLocationId).Name, places.First(p => p.Id == lr.DestinationLocationId).City,
            $"{material} ({lr.TotalWeightKg:0.##} kg, {lr.TotalPackages} packages)", lr.Status, StatusText(lr.Status), podStatus, lr.ArrivedAt, lr.UnloadedAt, action, note ?? message,
            podStatus == PodStatus.Rejected ? pod?.RejectionReason : null, podRules.EffectiveMaxFiles, (int)(Math.Min(podRules.MaxBytes, 10 * 1024 * 1024) / (1024 * 1024)),
            action == QrActions.UploadPod && podStatus is PodStatus.NotYetDue or PodStatus.Pending, clock.GetUtcNow());
    }

    private static (string Action, string Message) Next(Lr lr, PodStatus pod, string? rejection) => lr.Status switch
    {
        DeliveryStatus.Cancelled => (QrActions.None, "This LR has been cancelled."),
        DeliveryStatus.InTransit => (QrActions.ConfirmArrival, "When you reach the delivery point, tap Confirm Arrival. Your phone's location will be checked then."),
        DeliveryStatus.Arrived => (QrActions.ConfirmUnloading, "Tap Confirm Unloading Completed when the goods have been unloaded."),
        DeliveryStatus.UnloadingCompleted => pod switch
        {
            PodStatus.Accepted => (QrActions.None, "Delivery complete. The proof of delivery has been accepted. Thank you."),
            PodStatus.Received or PodStatus.UnderReview => (QrActions.None, "Delivery complete. Your proof of delivery has been received and is being checked."),
            PodStatus.Rejected => (QrActions.UploadPod, $"The proof of delivery was not accepted{(string.IsNullOrWhiteSpace(rejection) ? string.Empty : ": " + rejection)}. Please upload a corrected copy."),
            PodStatus.Pending => (QrActions.UploadPod, "Delivery complete. The proof of delivery is awaited: upload it now, or the office will upload it later."),
            _ => (QrActions.UploadPod, "Delivery complete. Upload the signed proof of delivery now, or tell us it is not available yet."),
        },
        _ => (QrActions.None, "The vehicle has not left the CFA yet. This page will show the next step once it has."),
    };

    private static string StatusText(DeliveryStatus s) => s switch
    {
        DeliveryStatus.Created or DeliveryStatus.Assigned => "Waiting to be loaded",
        DeliveryStatus.AtCfa => "Vehicle at the CFA",
        DeliveryStatus.Loading => "Loading",
        DeliveryStatus.Loaded => "Loaded",
        DeliveryStatus.InTransit => "In transit",
        DeliveryStatus.Arrived => "Arrived at destination",
        DeliveryStatus.UnloadingCompleted => "Unloading completed",
        _ => "Cancelled",
    };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
