using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Application.Exceptions;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Deliveries.Application.Pods;

/// <summary>
/// Runs a proof through its checks and decides what happens next: accepted by itself when every rule is met and the tenant allows it, otherwise to a reviewer.
/// The same routine serves a fresh submission and a late OCR result, so both end the same way.
/// </summary>
internal sealed class PodEngine(DeliveriesDbContext db, IDeliverySettings settings, ExceptionFactory exceptions, TimeProvider clock)
{
    public async Task<PodRequirements> RequirementsAsync(PodRecord pod, CancellationToken cancellationToken)
    {
        var rules = await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, cancellationToken);
        var discrepancy = await settings.GetAsync<DiscrepancyRulesSetting>(DeliverySettingKeys.Discrepancy, cancellationToken);
        var damageTypes = (await settings.GetAsync<List<ReasonSetting>>(DeliverySettingKeys.DamageTypes, cancellationToken)).Where(t => t.EvidenceRequired).Select(t => t.Code).ToList();
        return pod.CheckRequirements(rules, discrepancy, damageTypes, clock.GetUtcNow());
    }

    /// <summary>Recomputes Draft / Captured after the evidence or proof changed.</summary>
    public async Task RefreshAsync(PodRecord pod, CancellationToken cancellationToken) => pod.Refresh(await RequirementsAsync(pod, cancellationToken));

    public async Task<IReadOnlyList<PodCheck>> ValidateAsync(PodRecord pod, Delivery delivery, CancellationToken cancellationToken)
    {
        var rules = await settings.GetAsync<PodRulesSetting>(DeliverySettingKeys.PodRules, cancellationToken);
        var quantity = await settings.GetAsync<QuantityRulesSetting>(DeliverySettingKeys.Quantity, cancellationToken);
        var discrepancy = await settings.GetAsync<DiscrepancyRulesSetting>(DeliverySettingKeys.Discrepancy, cancellationToken);
        var damageTypes = (await settings.GetAsync<List<ReasonSetting>>(DeliverySettingKeys.DamageTypes, cancellationToken)).Where(t => t.EvidenceRequired).Select(t => t.Code).ToList();

        var hashes = pod.ActiveEvidence.Select(e => e.FileHash).Distinct().ToList();
        var duplicates = hashes.Count == 0
            ? new HashSet<string>()
            : (await db.Evidence.AsNoTracking().Where(e => e.PodId != pod.Id && e.RemovedAt == null && hashes.Contains(e.FileHash)
                && db.Pods.Any(p => p.Id == e.PodId && p.DeliveryId != pod.DeliveryId)).Select(e => e.FileHash).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        var checks = PodValidator.Validate(new PodValidationInput(pod, delivery, rules, quantity, discrepancy, damageTypes, duplicates, clock.GetUtcNow())).ToList();
        checks.AddRange(await OcrChecksAsync(pod, delivery, cancellationToken));
        pod.ApplyValidation(checks.Select(c => (c.Type, c.Check, c.Status, c.Message)), clock.GetUtcNow());
        return checks;
    }

    /// <summary>Compares the latest finished reading with the delivery, marks each field, and says what it means for the proof.</summary>
    private async Task<IReadOnlyList<PodCheck>> OcrChecksAsync(PodRecord pod, Delivery delivery, CancellationToken cancellationToken)
    {
        var latest = pod.OcrResults.OrderByDescending(o => o.QueuedAt).FirstOrDefault();
        if (latest is null)
        {
            return [];
        }

        if (latest.ProcessingStatus == OcrStatus.Failed)
        {
            return [new("Ocr", "Document", ValidationOutcome.Warning, "The paper POD could not be read; a reviewer reads it.")];
        }

        if (latest.ProcessingStatus != OcrStatus.Completed)
        {
            return [new("Ocr", "Document", ValidationOutcome.Warning, "The paper POD is still being read.")];
        }

        var settingsOcr = await settings.GetAsync<OcrSetting>(DeliverySettingKeys.Ocr, cancellationToken);
        var readings = latest.Fields.Select(f => new OcrReading(f.FieldName, f.RawValue, f.EffectiveValue, f.ReviewedValue is null ? f.Confidence : 1m)).ToList();
        var results = OcrReconciler.Reconcile(readings, delivery, pod, settingsOcr);
        foreach (var r in results)
        {
            latest.Field(r.Name)?.MarkValidation(r.Status, r.Message);
        }

        var checks = new List<PodCheck>();
        foreach (var r in results.Where(r => r.Status is OcrFieldStatus.Mismatch or OcrFieldStatus.LowConfidence))
        {
            var critical = settingsOcr.CriticalFields.Contains(r.Name, StringComparer.OrdinalIgnoreCase);
            checks.Add(new("Ocr", r.Name, critical || r.Status == OcrFieldStatus.Mismatch ? ValidationOutcome.RequiresReview : ValidationOutcome.Warning, $"{r.Name}: {r.Message}"));
        }

        if (latest.OverallConfidence is { } overall && overall < settingsOcr.ReviewBelow)
        {
            checks.Add(new("Ocr", "Confidence", ValidationOutcome.RequiresReview, $"The document was read with only {overall:P0} confidence overall."));
        }

        if (checks.Count == 0)
        {
            checks.Add(new("Ocr", "Document", ValidationOutcome.Valid, "The paper POD agrees with the system."));
        }

        return checks;
    }

    public static bool OcrAcceptable(PodRecord pod)
    {
        var latest = pod.OcrResults.OrderByDescending(o => o.QueuedAt).FirstOrDefault();
        return latest is { ProcessingStatus: OcrStatus.Completed } && latest.Fields.All(f => f.ValidationStatus is OcrFieldStatus.Matched or OcrFieldStatus.NotChecked)
            && !pod.Validations.Any(v => v.ValidationType == "Ocr" && v.Status is ValidationOutcome.RequiresReview or ValidationOutcome.Invalid);
    }

    public static bool OcrPending(PodRecord pod) => pod.OcrResults.Any(o => o.ProcessingStatus is OcrStatus.Queued or OcrStatus.Processing);

    /// <summary>After a submission or a finished reading: accept it, or hand it to a reviewer, and raise whatever the checks found.</summary>
    public async Task DecideAsync(PodRecord pod, Delivery delivery, CancellationToken cancellationToken)
    {
        if (pod.Status != PodStatus.Submitted || OcrPending(pod))
        {
            return;
        }

        await ValidateAsync(pod, delivery, cancellationToken);
        await RaiseFindingsAsync(pod, delivery, cancellationToken);

        var auto = await settings.GetAsync<AutoAcceptSetting>(DeliverySettingKeys.AutoAccept, cancellationToken);
        if (AutoAcceptPolicy.CanAutoAccept(pod, delivery, auto, OcrAcceptable(pod)) && pod.Accept(null, true, clock.GetUtcNow()).IsSuccess)
        {
            await AcceptedAsync(pod, delivery, cancellationToken);
            return;
        }

        pod.SendToReview(clock.GetUtcNow());
    }

    /// <summary>The proof was accepted: the delivery closes and the rest of the system hears of it. Open exceptions stay open.</summary>
    public async Task AcceptedAsync(PodRecord pod, Delivery delivery, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        pod.RaiseAccepted(delivery, now, (await settings.GetAsync<SlaSetting>(DeliverySettingKeys.Sla, cancellationToken)).PodSubmissionHours);
        if (delivery.IsCompleted)
        {
            delivery.Close(null, new Actor(null, null), now);
        }
    }

    private async Task RaiseFindingsAsync(PodRecord pod, Delivery delivery, CancellationToken cancellationToken)
    {
        if (pod.Validations.Any(v => v.Check == "Duplicate" && v.Status == ValidationOutcome.RequiresReview))
        {
            await exceptions.RaiseAsync(delivery, pod.Id, ExceptionType.DuplicatePod, "A file on this proof already appears on another delivery's proof.", cancellationToken);
        }

        if (pod.Validations.Any(v => v.Check == "Geofence" && v.Status == ValidationOutcome.RequiresReview))
        {
            await exceptions.RaiseAsync(delivery, pod.Id, ExceptionType.GpsException, pod.Validations.First(v => v.Check == "Geofence").Message, cancellationToken);
        }

        if (pod.Validations.Any(v => v.ValidationType == "Ocr" && v.Status == ValidationOutcome.RequiresReview))
        {
            await exceptions.RaiseAsync(delivery, pod.Id, ExceptionType.OcrValidationFailed, "The paper POD does not agree with the system or was read with low confidence.", cancellationToken);
        }
    }
}
