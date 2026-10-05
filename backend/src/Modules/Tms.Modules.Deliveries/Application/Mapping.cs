using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;

namespace Tms.Modules.Deliveries.Application;

/// <summary>Shapes deliveries, proofs and exceptions for the screen.</summary>
internal sealed class DeliveryMapper(DeliveriesDbContext db, IDeliverySettings settings)
{
    public async Task<IReadOnlyList<DeliverySummaryDto>> SummariesAsync(IReadOnlyList<Delivery> deliveries, CancellationToken cancellationToken)
    {
        var ids = deliveries.Select(d => d.Id).ToList();
        var pods = (await db.Pods.AsNoTracking().Where(p => ids.Contains(p.DeliveryId) && p.IsCurrent).Select(p => new { p.Id, p.DeliveryId, p.Status }).ToListAsync(cancellationToken))
            .ToDictionary(p => p.DeliveryId);
        var open = (await db.Exceptions.AsNoTracking().Where(e => ids.Contains(e.DeliveryId) && e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed)
            .GroupBy(e => e.DeliveryId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).ToDictionary(x => x.Key, x => x.Count);
        var withDiscrepancy = (await db.Discrepancies.AsNoTracking().Where(x => ids.Contains(x.DeliveryId)).Select(x => x.DeliveryId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        return deliveries.Select(d =>
        {
            pods.TryGetValue(d.Id, out var pod);
            var podStatus = pod?.Status ?? PodStatus.Pending;
            return new DeliverySummaryDto(
                d.Id, d.Number, d.ShipmentReference, d.CustomerName, d.DestinationReference, d.TransporterId, d.TransporterReference, d.VehicleReference, d.PlannedDeliveryAt, d.ActualDeliveryAt,
                d.Status, d.Outcome, podStatus, pod?.Id, withDiscrepancy.Contains(d.Id), open.GetValueOrDefault(d.Id), d.ServiceType);
        }).ToList();
    }

    public async Task<DeliveryDto> ToDtoAsync(Delivery d, CancellationToken cancellationToken)
    {
        var summary = (await SummariesAsync([d], cancellationToken))[0];
        var rules = await settings.GetAsync<QuantityRulesSetting>(DeliverySettingKeys.Quantity, cancellationToken);
        var items = d.Items.Select(i => new DeliveryItemDto(
            i.Id, i.SkuReference, i.Description, i.OrderedQuantity, i.DispatchedQuantity, i.DeliveredQuantity, i.ShortQuantity, i.DamagedQuantity, i.RejectedQuantity, i.UnitOfMeasure,
            i.Remarks, i.ShortageReasonCode, i.DamageType, i.DamageReason, i.DamageDescription,
            i.IsReported ? i.DispatchedQuantity - (i.DeliveredQuantity!.Value + i.ShortQuantity + i.DamagedQuantity + i.RejectedQuantity) : null)).ToList();

        // Reported lines are reconciled again from what is stored, so the screen shows the exact difference whatever produced it.
        var reconciliation = d.Items.Where(i => i.IsReported).Select(i =>
            QuantityReconciliation.Check(i, new ItemQuantities(i.Id, i.DeliveredQuantity!.Value, i.ShortQuantity, i.DamagedQuantity, i.RejectedQuantity), rules))
            .Select(r => new ReconciliationDto(r.ItemId, r.Sku, r.Dispatched, r.Accounted, r.Unaccounted, r.Reconciled, r.Problems)).ToList();

        var skus = d.Items.ToDictionary(i => i.Id, i => i.SkuReference);
        return new DeliveryDto(
            summary, d.OrderReference, d.LoadReference, d.TripReference, d.LrNumber, d.Sequence, d.DriverName, d.CustomerReference, d.CustomerPhone, d.CustomerEmail,
            d.OriginReference, d.DestinationAddress, d.CustomerLatitude, d.CustomerLongitude, d.GeofenceRadiusM, d.WindowStart, d.WindowEnd, d.ActualArrivalAt, d.RemainingDisposition,
            d.HasQuantityMismatch, d.HasOtpChallenge, d.OtpVerified,
            items,
            d.Attempts.OrderBy(a => a.AttemptNumber).Select(a => new AttemptDto(a.AttemptNumber, a.AttemptedAt, a.Result, a.ReasonCode, a.RecipientName, a.DriverRemarks, a.CustomerRemarks, a.Latitude, a.Longitude)).ToList(),
            d.Events.OrderBy(e => e.EventAt).ThenBy(e => e.EventType).Select(e => new DeliveryEventDto(e.EventAt, e.EventType, e.Latitude, e.Longitude, e.DeviceReference, e.Remarks)).ToList(),
            d.Discrepancies.Select(x => new DiscrepancyDto(x.Id, x.DeliveryItemId, skus.GetValueOrDefault(x.DeliveryItemId, "?"), x.Type, x.Quantity, x.ReasonCode, x.Description, x.CustomerAcknowledged, x.ClaimReference)).ToList(),
            reconciliation, d.Version);
    }

    public static EvidenceDto ToDto(PodEvidence e) =>
        new(e.Id, e.EvidenceType, e.FileName, e.ContentType, e.SizeBytes, e.FileHash, e.CapturedAt, e.Latitude, e.Longitude, e.DeviceReference, e.Width, e.Height, e.Warnings, !e.IsActive, e.RemovedReason);

    public async Task<PodDto> ToDtoAsync(PodRecord p, Delivery d, CancellationToken cancellationToken)
    {
        var rules = await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, cancellationToken);
        var discrepancy = await settings.GetAsync<DiscrepancyRulesSetting>(DeliverySettingKeys.Discrepancy, cancellationToken);
        var damageTypes = (await settings.GetAsync<List<ReasonSetting>>(DeliverySettingKeys.DamageTypes, cancellationToken)).Where(t => t.EvidenceRequired).Select(t => t.Code).ToList();
        var missing = p.IsEditable ? p.CheckRequirements(rules, discrepancy, damageTypes, DateTimeOffset.UtcNow).Missing : [];
        var ocrSettings = await settings.GetAsync<OcrSetting>(DeliverySettingKeys.Ocr, cancellationToken);
        var ocr = p.OcrResults.OrderBy(o => o.QueuedAt).Select(o => ToDto(o, d, p, ocrSettings)).ToList();

        return new PodDto(
            Summary(p, d, ocr.LastOrDefault()?.Status), p.Method, p.RecipientName, p.RecipientDesignation, p.RecipientPhone, p.ArrivalAt, p.CapturedAt, p.ReviewedAt, p.Latitude, p.Longitude,
            p.GpsAccuracy, p.Geofence, p.DriverRemarks, p.RecipientRemarks, p.DriverConfirmed, p.OtpVerified, p.CustomerAcknowledged, p.RejectionReason, p.RejectionCount, p.AutoAccepted,
            p.Items.Select(i => new PodItemDto(i.DeliveryItemId, i.SkuReference, i.OrderedQuantity, i.DispatchedQuantity, i.DeliveredQuantity, i.ShortQuantity, i.DamagedQuantity, i.RejectedQuantity, i.Remarks)).ToList(),
            p.Evidence.OrderBy(e => e.CapturedAt).Select(ToDto).ToList(),
            p.Signatures.Select(s => new SignatureDto(s.Id, s.SignerName, s.SignerDesignation, s.CapturedAt, s.Latitude, s.Longitude, s.VerificationMethod)).ToList(),
            p.Validations.OrderBy(v => v.ValidationType).ThenBy(v => v.Check).Select(v => new ValidationDto(v.ValidationType, v.Check, v.Status, v.Message, v.ValidatedAt)).ToList(),
            p.Reviews.OrderBy(r => r.PerformedAt).Select(r => new ReviewActionDto(r.PerformedAt, r.Action, r.FieldName, r.OldValue, r.NewValue, r.Reason, r.PerformedBy)).ToList(),
            ocr, missing, p.Version);
    }

    public static PodSummaryDto Summary(PodRecord p, Delivery d, OcrStatus? ocr) =>
        new(p.Id, p.PodNumber, p.PodVersion, p.IsCurrent, d.Id, d.Number, d.CustomerName, d.TransporterReference, p.Status, d.ActualDeliveryAt, p.SubmittedAt, p.ApprovedAt,
            p.SubmittedAt is { } at && p.Status is PodStatus.Submitted or PodStatus.UnderReview ? (decimal)Math.Round((DateTimeOffset.UtcNow - at).TotalHours, 1) : null,
            p.ValidationSummary, d.Discrepancies.Count > 0, ocr);

    public static OcrResultDto ToDto(PodOcrResult o, Delivery d, PodRecord p, OcrSetting settings) =>
        new(o.Id, o.EvidenceId, o.Provider, o.ProcessingStatus, o.OverallConfidence, o.QueuedAt, o.ProcessedAt, o.Error,
            o.Fields.Select(f => new OcrFieldDto(
                f.FieldName, f.RawValue, f.NormalizedValue, f.Confidence, f.ValidationStatus, f.ValidationMessage, f.ReviewedValue, f.EffectiveValue, ExpectedFor(f.FieldName, d, p),
                OcrReconciler.ThresholdFor(f.FieldName, settings))).ToList());

    private static string? ExpectedFor(string field, Delivery d, PodRecord p) => field switch
    {
        OcrReconciler.ShipmentNumber => d.ShipmentReference,
        OcrReconciler.DeliveryNumber => d.Number,
        OcrReconciler.InvoiceNumber => d.OrderReference,
        OcrReconciler.Customer => d.CustomerName,
        OcrReconciler.Transporter => d.TransporterReference,
        OcrReconciler.VehicleNumber => d.VehicleReference,
        OcrReconciler.DeliveryDate => d.ActualDeliveryAt?.ToOffset(Clock.India).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        OcrReconciler.RecipientName => p.RecipientName,
        OcrReconciler.DeliveredQuantity => p.Items.Sum(i => i.DeliveredQuantity).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        OcrReconciler.ShortQuantity => p.Items.Sum(i => i.ShortQuantity).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };
}
