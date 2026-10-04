using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

public enum OwnerKind
{
    Transporter = 1,
    Vehicle = 2,
    Driver = 3,
}

public enum DocumentKind
{
    // Transporter
    PanCard = 1,
    GstCertificate = 2,
    CancelledCheque = 3,
    MsmeCertificate = 4,
    TransportLicense = 5,

    // Vehicle
    RegistrationCertificate = 10,
    Insurance = 11,
    Fitness = 12,
    Permit = 13,
    Puc = 14,

    // Driver
    DrivingLicense = 20,
}

public enum ExpiryStatus
{
    NoExpiry = 0,
    Valid = 1,
    ExpiringSoon = 2,
    Expired = 3,
}

/// <summary>A compliance paper (insurance, fitness, PAN card…) with its file and validity.</summary>
public sealed class ComplianceDocument : AggregateRoot, ITenantScoped
{
    public const int ExpiringSoonDays = 30;

    private static readonly Dictionary<OwnerKind, DocumentKind[]> KindsByOwner = new()
    {
        [OwnerKind.Transporter] = [DocumentKind.PanCard, DocumentKind.GstCertificate, DocumentKind.CancelledCheque, DocumentKind.MsmeCertificate, DocumentKind.TransportLicense],
        [OwnerKind.Vehicle] = [DocumentKind.RegistrationCertificate, DocumentKind.Insurance, DocumentKind.Fitness, DocumentKind.Permit, DocumentKind.Puc],
        [OwnerKind.Driver] = [DocumentKind.DrivingLicense],
    };

    private ComplianceDocument()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>The transporter this belongs to (the owner itself, or its vehicle's / driver's transporter). Used for vendor-portal scoping.</summary>
    public Guid TransporterId { get; private set; }

    public OwnerKind OwnerKind { get; private set; }

    public Guid OwnerId { get; private set; }

    public DocumentKind Kind { get; private set; }

    public string? Number { get; private set; }

    public DateOnly? IssuedOn { get; private set; }

    public DateOnly? ExpiresOn { get; private set; }

    public string FileKey { get; private set; } = null!;

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    /// <summary>Set when a newer document of the same kind was uploaded for the same owner. Superseded papers are kept for history.</summary>
    public DateTimeOffset? SupersededAt { get; private set; }

    public bool IsCurrent => SupersededAt is null;

    public static IReadOnlyList<DocumentKind> KindsFor(OwnerKind owner) => KindsByOwner[owner];

    public static Result<ComplianceDocument> Create(
        Guid tenantId,
        Guid transporterId,
        OwnerKind ownerKind,
        Guid ownerId,
        DocumentKind kind,
        string? number,
        DateOnly? issuedOn,
        DateOnly? expiresOn,
        string fileKey,
        string fileName,
        string contentType,
        long sizeBytes,
        DocumentPolicy? policy = null)
    {
        var errors = new FieldErrors();
        if (!KindsByOwner[ownerKind].Contains(kind))
        {
            errors.Add("kind", $"{kind} is not a valid document for a {ownerKind.ToString().ToLowerInvariant()}.");
        }

        if ((policy ?? DocumentPolicy.Default).For(kind) is { IsActive: true, ExpiryRequired: true } && expiresOn is null)
        {
            errors.Add("expiresOn", "An expiry date is required for this document.");
        }

        if (issuedOn is { } issued && expiresOn is { } expires && expires < issued)
        {
            errors.Add("expiresOn", "The expiry date cannot be before the issue date.");
        }

        if (number?.Length > 64)
        {
            errors.Add("number", "Document number must be at most 64 characters.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        return new ComplianceDocument
        {
            TenantId = tenantId,
            TransporterId = transporterId,
            OwnerKind = ownerKind,
            OwnerId = ownerId,
            Kind = kind,
            Number = string.IsNullOrWhiteSpace(number) ? null : number.Trim(),
            IssuedOn = issuedOn,
            ExpiresOn = expiresOn,
            FileKey = fileKey,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
        };
    }

    public void Supersede(DateTimeOffset now) => SupersededAt ??= now;

    /// <summary>A document is valid through its expiry date inclusive.</summary>
    public ExpiryStatus StatusOn(DateOnly today, int? reminderDays = null)
    {
        if (ExpiresOn is not { } expires)
        {
            return ExpiryStatus.NoExpiry;
        }

        if (expires < today)
        {
            return ExpiryStatus.Expired;
        }

        return expires <= today.AddDays(reminderDays ?? ExpiringSoonDays) ? ExpiryStatus.ExpiringSoon : ExpiryStatus.Valid;
    }
}

public enum ComplianceStatus
{
    Compliant = 1,
    ExpiringSoon = 2,
    NonCompliant = 3,
}

public sealed record ComplianceResult(ComplianceStatus Status, IReadOnlyList<string> Issues);

/// <summary>Decides whether a vehicle or driver may be put to work, from the papers on file and the tenant's document rules.</summary>
public static class ComplianceEvaluator
{
    public static ComplianceResult ForVehicle(IEnumerable<ComplianceDocument> documents, DateOnly today, DocumentPolicy? policy = null) =>
        Evaluate(documents, OwnerKind.Vehicle, today, policy ?? DocumentPolicy.Default);

    public static ComplianceResult ForDriver(IEnumerable<ComplianceDocument> documents, DateOnly today, DocumentPolicy? policy = null) =>
        Evaluate(documents, OwnerKind.Driver, today, policy ?? DocumentPolicy.Default);

    private static ComplianceResult Evaluate(IEnumerable<ComplianceDocument> documents, OwnerKind owner, DateOnly today, DocumentPolicy policy)
    {
        var current = documents.Where(d => d.IsCurrent).GroupBy(d => d.Kind).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.CreatedAt).First());
        var blocking = new List<string>();
        var warnings = new List<string>();

        foreach (var kind in policy.RequiredFor(owner))
        {
            var rule = policy.For(kind);
            if (!current.TryGetValue(kind, out var document))
            {
                blocking.Add($"{Label(kind)} missing");
                continue;
            }

            switch (document.StatusOn(today, rule.RenewalReminderDays))
            {
                case ExpiryStatus.Expired:
                    (rule.BlockWhenExpired ? blocking : warnings).Add($"{Label(kind)} expired on {document.ExpiresOn:dd MMM yyyy}");
                    break;
                case ExpiryStatus.ExpiringSoon:
                    warnings.Add($"{Label(kind)} expires on {document.ExpiresOn:dd MMM yyyy}");
                    break;
            }
        }

        var status = blocking.Count > 0 ? ComplianceStatus.NonCompliant : warnings.Count > 0 ? ComplianceStatus.ExpiringSoon : ComplianceStatus.Compliant;
        return new ComplianceResult(status, [.. blocking, .. warnings]);
    }

    public static string Label(DocumentKind kind) => kind switch
    {
        DocumentKind.PanCard => "PAN card",
        DocumentKind.GstCertificate => "GST certificate",
        DocumentKind.CancelledCheque => "Cancelled cheque",
        DocumentKind.MsmeCertificate => "MSME certificate",
        DocumentKind.TransportLicense => "Transport licence",
        DocumentKind.RegistrationCertificate => "Registration certificate (RC)",
        DocumentKind.Insurance => "Insurance",
        DocumentKind.Fitness => "Fitness certificate",
        DocumentKind.Permit => "Permit",
        DocumentKind.Puc => "PUC certificate",
        DocumentKind.DrivingLicense => "Driving licence",
        _ => kind.ToString(),
    };
}
