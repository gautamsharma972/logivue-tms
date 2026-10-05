using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Deliveries.Domain;

/// <summary>One value read from the paper POD. The reading is kept exactly as the provider gave it; a reviewer's correction sits beside it.</summary>
public sealed class PodOcrField : Entity, ITenantScoped
{
    private PodOcrField()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid OcrResultId { get; private set; }

    public string FieldName { get; private set; } = null!;

    public string? RawValue { get; private set; }

    public string? NormalizedValue { get; private set; }

    public decimal Confidence { get; private set; }

    public string? BoundingBoxJson { get; private set; }

    public OcrFieldStatus ValidationStatus { get; private set; }

    public string? ValidationMessage { get; private set; }

    public string? ReviewedValue { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>What counts: the reviewer's value if there is one, otherwise the reading.</summary>
    public string? EffectiveValue => ReviewedValue ?? NormalizedValue ?? RawValue;

    internal static PodOcrField Create(Guid tenantId, Guid resultId, string name, string? raw, string? normalized, decimal confidence, string? box) => new()
    {
        TenantId = tenantId, OcrResultId = resultId, FieldName = name, RawValue = raw, NormalizedValue = normalized, Confidence = Math.Clamp(confidence, 0m, 1m), BoundingBoxJson = box,
    };

    public void MarkValidation(OcrFieldStatus status, string? message)
    {
        ValidationStatus = status;
        ValidationMessage = message;
    }

    internal void Review(string value, Guid? by, DateTimeOffset now)
    {
        ReviewedValue = value.Trim();
        ReviewedBy = by;
        ReviewedAt = now;
    }
}

/// <summary>The outcome of reading one uploaded POD document. Processed in the background so submitting a proof never waits on it.</summary>
public sealed class PodOcrResult : Entity, ITenantScoped
{
    private readonly List<PodOcrField> _fields = [];

    private PodOcrResult()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid PodId { get; private set; }

    public Guid EvidenceId { get; private set; }

    public string Provider { get; private set; } = null!;

    public string DocumentType { get; private set; } = "PodDocument";

    public decimal? OverallConfidence { get; private set; }

    public OcrStatus ProcessingStatus { get; private set; }

    /// <summary>Where the provider's full response is kept, when it is large. Never stored in this table.</summary>
    public string? RawResponseReference { get; private set; }

    public DateTimeOffset QueuedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? Error { get; private set; }

    public IReadOnlyList<PodOcrField> Fields => _fields;

    internal static PodOcrResult Queue(Guid tenantId, Guid podId, Guid evidenceId, string provider, DateTimeOffset now) => new()
    {
        TenantId = tenantId, PodId = podId, EvidenceId = evidenceId, Provider = provider, ProcessingStatus = OcrStatus.Queued, QueuedAt = now,
    };

    public void Start() => ProcessingStatus = OcrStatus.Processing;

    public void Complete(string provider, IEnumerable<(string Name, string? Raw, string? Normalized, decimal Confidence, string? Box)> fields, decimal? overall, string? rawReference, DateTimeOffset now)
    {
        Provider = provider;
        _fields.Clear();
        foreach (var (name, raw, normalized, confidence, box) in fields)
        {
            _fields.Add(PodOcrField.Create(TenantId, Id, name, raw, normalized, confidence, box));
        }

        OverallConfidence = overall;
        RawResponseReference = rawReference;
        ProcessingStatus = OcrStatus.Completed;
        ProcessedAt = now;
        Error = null;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        ProcessingStatus = OcrStatus.Failed;
        Error = error.Length > 500 ? error[..500] : error;
        ProcessedAt = now;
    }

    public Result<PodOcrField> ReviewField(string name, string value, Guid? by, DateTimeOffset now)
    {
        var field = _fields.FirstOrDefault(f => string.Equals(f.FieldName, name, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            return Domain.OcrErrors.FieldNotFound;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return Domain.OcrErrors.ValueRequired;
        }

        field.Review(value, by, now);
        return field;
    }

    public PodOcrField? Field(string name) => _fields.FirstOrDefault(f => string.Equals(f.FieldName, name, StringComparison.OrdinalIgnoreCase));
}

internal static class OcrErrors
{
    public static readonly Error FieldNotFound = Error.NotFound("ocr.field_not_found", "That field was not read from the document.");

    public static readonly Error ValueRequired = Error.Validation("ocr.value_required", "Enter the corrected value.");
}
