using System.Globalization;

namespace Tms.Modules.Deliveries.Domain;

public sealed record PodCheck(string Type, string Check, ValidationOutcome Status, string Message);

/// <param name="DuplicateHashes">Hashes of this proof's files that already exist on another proof: a re-used photo is flagged, never silently accepted.</param>
public sealed record PodValidationInput(
    PodRecord Pod, Delivery Delivery, PodRulesSetting Rules, QuantityRulesSetting Quantity, DiscrepancyRulesSetting Discrepancy, IReadOnlyCollection<string> DamageTypesNeedingPhoto,
    IReadOnlySet<string> DuplicateHashes, DateTimeOffset Now);

/// <summary>
/// Judges a proof against the delivery it claims to prove. Four kinds of check: structural (is it the right delivery), quantity (do the numbers add up),
/// evidence (is the required proof there) and business (location, timing, duplicates). A check that does not apply says so; it is never counted as a failure.
/// </summary>
public static class PodValidator
{
    public static IReadOnlyList<PodCheck> Validate(PodValidationInput input)
    {
        var results = new List<PodCheck>();
        Structural(input, results);
        Quantity(input, results);
        Evidence(input, results);
        Business(input, results);
        return results;
    }

    private static void Structural(PodValidationInput i, List<PodCheck> r)
    {
        const string t = "Structural";
        r.Add(i.Pod.DeliveryId == i.Delivery.Id
            ? new(t, "Delivery", ValidationOutcome.Valid, "The proof belongs to this delivery.")
            : new(t, "Delivery", ValidationOutcome.Invalid, "The proof belongs to a different delivery."));
        r.Add(i.Delivery.IsCompleted || i.Delivery.Status == DeliveryStatus.Closed
            ? new(t, "Completed", ValidationOutcome.Valid, "The delivery has been completed.")
            : new(t, "Completed", ValidationOutcome.Invalid, $"The delivery is {i.Delivery.Status}, so there is nothing to prove yet."));
        r.Add(string.IsNullOrWhiteSpace(i.Pod.RecipientName)
            ? new(t, "Recipient", ValidationOutcome.Invalid, "Nobody is named as having received the goods.")
            : new(t, "Recipient", ValidationOutcome.Valid, $"Received by {i.Pod.RecipientName}."));
    }

    private static void Quantity(PodValidationInput i, List<PodCheck> r)
    {
        const string t = "Quantity";
        var byItem = i.Delivery.Items.ToDictionary(x => x.Id);
        var problems = new List<string>();
        var mismatches = new List<string>();

        foreach (var p in i.Pod.Items)
        {
            if (!byItem.TryGetValue(p.DeliveryItemId, out var item))
            {
                problems.Add($"{p.SkuReference} is not on the delivery.");
                continue;
            }

            if (p.DeliveredQuantity < 0 || p.ShortQuantity < 0 || p.DamagedQuantity < 0 || p.RejectedQuantity < 0)
            {
                problems.Add($"{p.SkuReference} has a negative quantity.");
            }

            var allowed = item.DispatchedQuantity * (1 + i.Quantity.OverDeliveryPct / 100m);
            if (p.DeliveredQuantity > allowed)
            {
                problems.Add($"{p.SkuReference}: delivered {p.DeliveredQuantity:0.##} is more than the {allowed:0.##} allowed.");
            }

            var diff = item.DispatchedQuantity - (p.DeliveredQuantity + p.ShortQuantity + p.DamagedQuantity + p.RejectedQuantity);
            if (diff != 0)
            {
                mismatches.Add($"{p.SkuReference}: dispatched {item.DispatchedQuantity:0.##}, accounted for {item.DispatchedQuantity - diff:0.##} (difference {diff:+0.##;-0.##}).");
            }

            if (item.IsReported && (item.DeliveredQuantity != p.DeliveredQuantity || item.ShortQuantity != p.ShortQuantity || item.DamagedQuantity != p.DamagedQuantity || item.RejectedQuantity != p.RejectedQuantity))
            {
                problems.Add($"{p.SkuReference}: the proof's quantities differ from the delivery's.");
            }
        }

        if (problems.Count > 0)
        {
            r.Add(new(t, "Quantities", ValidationOutcome.Invalid, string.Join(" ", problems)));
        }
        else if (mismatches.Count > 0)
        {
            r.Add(new(t, "Reconciliation", ValidationOutcome.RequiresReview, "Quantities do not reconcile. " + string.Join(" ", mismatches)));
        }
        else
        {
            r.Add(new(t, "Reconciliation", ValidationOutcome.Valid, "Delivered, short, damaged and rejected quantities add up to what was dispatched."));
        }
    }

    private static void Evidence(PodValidationInput i, List<PodCheck> r)
    {
        const string t = "Evidence";
        var pod = i.Pod;
        var rules = i.Rules;
        var photos = pod.ActiveEvidence.Count(e => e.EvidenceType != EvidenceType.PodDocument);

        bool needsSignature = rules.SignatureRequired || pod.Method == ProofMethod.Signature;
        r.Add(needsSignature
            ? pod.Signatures.Count > 0
                ? new(t, "Signature", ValidationOutcome.Valid, "A signature was captured.")
                : new(t, "Signature", ValidationOutcome.Invalid, "A signature is required and was not captured.")
            : new(t, "Signature", ValidationOutcome.Valid, pod.Signatures.Count > 0 ? "A signature was captured." : "Not required."));

        bool needsOtp = rules.OtpRequired || pod.Method is ProofMethod.Otp or ProofMethod.Qr;
        r.Add(needsOtp
            ? pod.OtpVerified
                ? new(t, "Otp", ValidationOutcome.Valid, "The customer's one-time code was verified.")
                : new(t, "Otp", ValidationOutcome.Invalid, "A one-time code is required and was not verified.")
            : new(t, "Otp", ValidationOutcome.Valid, "Not required."));

        r.Add(rules.PhotoRequired
            ? photos >= rules.MinPhotos
                ? new(t, "Photos", ValidationOutcome.Valid, $"{photos} photo(s) captured.")
                : new(t, "Photos", ValidationOutcome.Invalid, $"At least {rules.MinPhotos} photo(s) are required; {photos} captured.")
            : new(t, "Photos", ValidationOutcome.Valid, photos > 0 ? $"{photos} photo(s) captured." : "Not required."));

        var hasFix = GeoFix.IsKnownValue(pod.Latitude, pod.Longitude);
        r.Add(rules.GpsRequired
            ? hasFix ? new(t, "Gps", ValidationOutcome.Valid, "The location was captured.") : new(t, "Gps", ValidationOutcome.Invalid, "The location is required and was not captured.")
            : new(t, "Gps", ValidationOutcome.Valid, hasFix ? "The location was captured." : "Not applicable."));

        if (pod.Method == ProofMethod.Contactless)
        {
            r.Add(pod.DriverConfirmed && photos > 0 && hasFix
                ? new(t, "Contactless", ValidationOutcome.Valid, "Contactless delivery confirmed by the driver with a photo and the location.")
                : new(t, "Contactless", ValidationOutcome.Invalid, "A contactless delivery needs the driver's confirmation, a photo and the location."));
        }

        if (pod.Items.Any(x => x.DamagedQuantity > 0) && i.DamageTypesNeedingPhoto.Count > 0)
        {
            r.Add(pod.ActiveEvidence.Any(e => e.EvidenceType == EvidenceType.DamagePhoto)
                ? new(t, "DamagePhoto", ValidationOutcome.Valid, "Damage is photographed.")
                : new(t, "DamagePhoto", ValidationOutcome.Invalid, "Damage was reported without a photo of it."));
        }

        var needsAck = (i.Discrepancy.ShortageAcknowledgementRequired && pod.Items.Any(x => x.ShortQuantity > 0)) || (i.Discrepancy.DamageAcknowledgementRequired && pod.Items.Any(x => x.DamagedQuantity > 0));
        if (needsAck)
        {
            r.Add(pod.CustomerAcknowledged
                ? new(t, "Acknowledgement", ValidationOutcome.Valid, "The customer acknowledged the shortage or damage.")
                : new(t, "Acknowledgement", ValidationOutcome.Invalid, "The customer's acknowledgement of the shortage or damage is missing."));
        }
    }

    private static void Business(PodValidationInput i, List<PodCheck> r)
    {
        const string t = "Business";
        var d = i.Delivery;
        r.Add(i.Pod.Geofence switch
        {
            GeofenceStatus.Inside => new(t, "Geofence", ValidationOutcome.Valid, "Delivered inside the customer's geofence."),
            GeofenceStatus.Outside => DistanceMessage(i),
            GeofenceStatus.GpsUnavailable => new(t, "Geofence", i.Rules.GeofenceRequired ? ValidationOutcome.RequiresReview : ValidationOutcome.Warning, "No location was captured to check against the geofence."),
            GeofenceStatus.AccuracyInsufficient => new(t, "Geofence", i.Rules.GeofenceRequired ? ValidationOutcome.RequiresReview : ValidationOutcome.Warning, "The location was too vague to check against the geofence."),
            _ => new(t, "Geofence", ValidationOutcome.Valid, "Not applicable."),
        });

        if (d.WindowEnd is { } end && d.ActualDeliveryAt is { } at && at > end)
        {
            r.Add(new(t, "Timing", ValidationOutcome.Warning, $"Delivered {(at - end).TotalMinutes:0} minutes after the delivery window closed."));
        }
        else
        {
            r.Add(new(t, "Timing", ValidationOutcome.Valid, d.WindowEnd is null ? "No delivery window to check." : "Delivered within the window."));
        }

        var duplicates = i.Pod.ActiveEvidence.Where(e => i.DuplicateHashes.Contains(e.FileHash)).ToList();
        r.Add(duplicates.Count > 0
            ? new(t, "Duplicate", ValidationOutcome.RequiresReview, $"{duplicates.Count} file(s) already appear on another proof: a potential duplicate POD.")
            : new(t, "Duplicate", ValidationOutcome.Valid, "No file is repeated from another proof."));

        if (i.Pod.Items.Any(x => x.ShortQuantity > 0 || x.DamagedQuantity > 0 || x.RejectedQuantity > 0))
        {
            r.Add(new(t, "Discrepancy", ValidationOutcome.RequiresReview, "A shortage, damage or rejected quantity was recorded: a reviewer confirms it."));
        }
    }

    private static PodCheck DistanceMessage(PodValidationInput i)
    {
        var d = i.Delivery;
        var metres = d.CustomerLatitude is { } clat && d.CustomerLongitude is { } clon && i.Pod.Latitude is { } lat && i.Pod.Longitude is { } lon ? Geo.DistanceMetres(clat, clon, lat, lon) : (double?)null;
        var text = metres is { } m ? string.Create(CultureInfo.InvariantCulture, $"Delivered {m:0} m from the customer's location (allowed {d.GeofenceRadiusM} m).") : "Delivered outside the customer's geofence.";
        // Outside the fence is flagged for a person to look at, not rejected: a gate on a side street is not fraud.
        return new("Business", "Geofence", ValidationOutcome.RequiresReview, text);
    }
}

/// <summary>Decides whether a submitted proof may be accepted without a reviewer. Off for any discrepancy unless the tenant explicitly allows it.</summary>
public static class AutoAcceptPolicy
{
    public static bool CanAutoAccept(PodRecord pod, Delivery delivery, AutoAcceptSetting rules, bool ocrCompletedAndMatched)
    {
        if (!rules.Enabled || pod.ValidationSummary != ValidationOutcome.Valid)
        {
            return false;
        }

        var hasDiscrepancy = delivery.Discrepancies.Count > 0 || delivery.HasQuantityMismatch;
        if (hasDiscrepancy && !rules.AllowWithDiscrepancy)
        {
            return false;
        }

        return !rules.RequireOcr || ocrCompletedAndMatched;
    }
}
