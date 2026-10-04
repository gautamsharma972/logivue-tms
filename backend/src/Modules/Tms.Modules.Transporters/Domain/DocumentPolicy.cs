using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

/// <summary>How one kind of paper is treated: whether it is required, needs a date, how early renewal is flagged, and whether a lapse stops work.</summary>
/// <param name="IsMandatory">Required before a transporter is submitted (transporter papers) or before a vehicle / driver may work.</param>
/// <param name="RenewalReminderDays">A paper expiring within this many days is flagged "expiring soon".</param>
/// <param name="BlockWhenExpired">An expired paper makes the vehicle or driver non-compliant (cannot be allocated); otherwise it is only a warning.</param>
/// <param name="IsActive">An inactive kind is not checked at all.</param>
public sealed record DocumentRuleValues(bool IsMandatory, bool ExpiryRequired, int RenewalReminderDays, bool BlockWhenExpired, bool IsActive);

/// <summary>
/// The rules for every kind of compliance paper: built-in defaults (the behaviour before rules became configurable), overridden per tenant by
/// <see cref="DocumentRule"/> rows. Pure; loaded once per request by <c>DocumentPolicyProvider</c>.
/// </summary>
public sealed class DocumentPolicy
{
    public const int MaxReminderDays = 365;

    private static readonly Dictionary<DocumentKind, DocumentRuleValues> Defaults = new()
    {
        [DocumentKind.PanCard] = new(true, false, 30, true, true),
        [DocumentKind.GstCertificate] = new(false, false, 30, true, true),
        [DocumentKind.CancelledCheque] = new(true, false, 30, true, true),
        [DocumentKind.MsmeCertificate] = new(false, false, 30, true, true),
        [DocumentKind.TransportLicense] = new(false, false, 30, true, true),
        [DocumentKind.RegistrationCertificate] = new(true, false, 30, true, true),
        [DocumentKind.Insurance] = new(true, true, 30, true, true),
        [DocumentKind.Fitness] = new(true, true, 30, true, true),
        [DocumentKind.Permit] = new(true, true, 30, true, true),
        [DocumentKind.Puc] = new(false, true, 15, false, true),
        [DocumentKind.DrivingLicense] = new(true, true, 30, true, true),
    };

    private readonly Dictionary<DocumentKind, DocumentRuleValues> _overrides;

    public DocumentPolicy(IEnumerable<DocumentRule> rules) => _overrides = rules.ToDictionary(r => r.Kind, r => r.Values);

    public static DocumentPolicy Default { get; } = new([]);

    public static DocumentRuleValues DefaultFor(DocumentKind kind) => Defaults[kind];

    public DocumentRuleValues For(DocumentKind kind) => _overrides.TryGetValue(kind, out var values) ? values : Defaults[kind];

    public bool IsOverridden(DocumentKind kind) => _overrides.ContainsKey(kind);

    /// <summary>The kinds that must be on file for this owner, in the order the screen lists them.</summary>
    public IReadOnlyList<DocumentKind> RequiredFor(OwnerKind owner) =>
        ComplianceDocument.KindsFor(owner).Where(k => For(k) is { IsMandatory: true, IsActive: true }).ToList();
}

/// <summary>A tenant's own rule for one kind of paper. Absent means the built-in default applies.</summary>
public sealed class DocumentRule : AggregateRoot, ITenantScoped
{
    private DocumentRule()
    {
    }

    public Guid TenantId { get; private set; }

    public DocumentKind Kind { get; private set; }

    public bool IsMandatory { get; private set; }

    public bool ExpiryRequired { get; private set; }

    public int RenewalReminderDays { get; private set; }

    public bool BlockWhenExpired { get; private set; }

    public bool IsActive { get; private set; }

    public DocumentRuleValues Values => new(IsMandatory, ExpiryRequired, RenewalReminderDays, BlockWhenExpired, IsActive);

    public static Result<DocumentRule> Create(Guid tenantId, DocumentKind kind, DocumentRuleValues values)
    {
        var rule = new DocumentRule { TenantId = tenantId, Kind = kind };
        var set = rule.Set(values);
        return set.IsFailure ? set.Error : rule;
    }

    public Result Set(DocumentRuleValues values)
    {
        if (values.RenewalReminderDays is < 0 or > DocumentPolicy.MaxReminderDays)
        {
            return Error.Validation("documents.rule_invalid", $"The reminder must be between 0 and {DocumentPolicy.MaxReminderDays} days.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["renewalReminderDays"] = [$"Enter 0 to {DocumentPolicy.MaxReminderDays} days."] },
            };
        }

        if (values.IsMandatory && !values.IsActive)
        {
            return Error.Validation("documents.rule_invalid", "A paper that is switched off cannot also be mandatory.");
        }

        IsMandatory = values.IsMandatory;
        ExpiryRequired = values.ExpiryRequired;
        RenewalReminderDays = values.RenewalReminderDays;
        BlockWhenExpired = values.BlockWhenExpired;
        IsActive = values.IsActive;
        return Result.Success();
    }
}
