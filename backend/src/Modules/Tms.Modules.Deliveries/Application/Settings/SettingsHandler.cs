using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Settings;

public sealed record SettingDto(string Key, JsonElement Value, bool IsCustomised);

/// <summary>Delivery and proof-of-delivery policy per tenant: required evidence, reasons, OCR thresholds, SLA, auto-accept. Defaults apply until a tenant changes one.</summary>
internal sealed class SettingsHandler(DeliveriesDbContext db, DeliveryAccess access, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<SettingDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!access.CanRead && !access.CanExecute)
        {
            return DeliveryAccess.Forbidden;
        }

        var rows = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.ValueJson, cancellationToken);
        return DeliverySettingDefaults.Keys.Order().Select(key =>
            rows.TryGetValue(key, out var json)
                ? new SettingDto(key, JsonDocument.Parse(json).RootElement.Clone(), true)
                : new SettingDto(key, JsonSerializer.SerializeToElement(DeliverySettingDefaults.For(key), DeliverySettings.Json), false)).ToList();
    }

    public async Task<Result<SettingDto>> SaveAsync(string key, JsonElement value, CancellationToken cancellationToken)
    {
        if (!access.CanConfigure || user.TenantId is not { } tenantId)
        {
            return DeliveryAccess.Forbidden;
        }

        if (DeliverySettingDefaults.TypeOf(key) is not { } type)
        {
            return Error.NotFound("settings.unknown", "There is no such setting.");
        }

        object? parsed;
        try
        {
            parsed = value.Deserialize(type, DeliverySettings.Json);
        }
        catch (JsonException)
        {
            return Error.Validation("settings.invalid", $"The value is not valid for '{key}'.");
        }

        if (parsed is null || Validate(key, parsed) is { } problem)
        {
            return Error.Validation("settings.invalid", Validate(key, parsed ?? new object()) ?? $"The value is not valid for '{key}'.");
        }

        var json = JsonSerializer.Serialize(parsed, DeliverySettings.Json);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (row is null)
        {
            db.Settings.Add(DeliverySetting.Create(tenantId, key, json));
        }
        else
        {
            row.Change(json);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SettingDto(key, JsonDocument.Parse(json).RootElement.Clone(), true);
    }

    /// <summary>Returns what is wrong with a value, or null when it is sensible.</summary>
    internal static string? Validate(string key, object value) => value switch
    {
        PodRulesSetting p when p.MinPhotos < 0 || p.MinPhotos > 20 => "The number of photos must be between 0 and 20.",
        PodRulesSetting p when p.MaxGpsAccuracyM is <= 0 or > 10_000 => "The GPS accuracy limit must be between 1 and 10,000 metres.",
        PodRulesSetting p when p.OtpValidityMinutes is < 1 or > 1440 || p.OtpMaxAttempts is < 1 or > 20 => "The code needs a validity of 1–1440 minutes and 1–20 attempts.",
        QuantityRulesSetting q when q.OverDeliveryPct is < 0 or > 100 => "Over-delivery must be between 0 and 100 per cent.",
        OcrSetting o when new[] { o.CriticalThreshold, o.StandardThreshold, o.OptionalThreshold, o.ReviewBelow }.Any(t => t is < 0 or > 1) => "Confidence thresholds must be between 0 and 1.",
        AgeingSetting a when a.UpperDays.Count is < 1 or > 12 || a.UpperDays.Any(d => d is < 0 or > 365) || a.UpperDays.Distinct().Count() != a.UpperDays.Count => "Give between 1 and 12 different bucket limits, each 0–365 days.",
        SlaSetting s when s.PodSubmissionHours < 1 || s.PodReviewHours < 1 || s.ResubmissionHours < 1 => "SLA hours must be at least 1.",
        ExceptionRulesSetting e when e.DueHours < 1 || e.EscalateAfterHours < 1 => "Exception hours must be at least 1.",
        ImageRulesSetting i when i.MaxBytes is < 10_000 or > 10 * 1024 * 1024 || i.MinWidth < 0 || i.MinHeight < 0 => "Image limits are out of range.",
        List<ReasonSetting> r when r.Count == 0 || r.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name)) || r.Select(x => x.Code.ToUpperInvariant()).Distinct().Count() != r.Count => "Every reason needs a unique code and a name.",
        _ => null,
    };
}
