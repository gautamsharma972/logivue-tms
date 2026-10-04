using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.India;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

public enum TransporterStatus
{
    /// <summary>Being set up; fully editable. Not yet usable for freight.</summary>
    Draft = 1,

    /// <summary>Locked while the onboarding approval runs.</summary>
    PendingApproval = 2,

    Active = 3,

    /// <summary>Approval was refused. Editable again so it can be corrected and resubmitted.</summary>
    Rejected = 4,

    /// <summary>Temporarily barred from new work (compliance lapse, performance, dispute).</summary>
    Suspended = 5,
}

[Flags]
public enum ServiceModes
{
    None = 0,
    Ftl = 1,
    Ptl = 2,
    Dedicated = 4,
}

public sealed record Address(string Line1, string? Line2, string City, string State, string Pincode);

public sealed record BankAccount(string AccountHolder, string AccountNumber, string Ifsc, string BankName);

/// <summary>Everything an operator types when registering a transporter, other than bank details.</summary>
public sealed record TransporterProfile(
    string LegalName,
    string? TradeName,
    string Pan,
    string? Gstin,
    string ContactPerson,
    string Phone,
    string Email,
    Address Address,
    ServiceModes ServiceModes,
    string? TypeCode = null);

/// <summary>
/// A freight vendor. Moves Draft → PendingApproval → Active through the approval engine; identity details
/// (legal name, PAN, GSTIN) are frozen once approved so a vendor cannot be silently swapped for another.
/// </summary>
public sealed class Transporter : AggregateRoot, ITenantScoped
{
    private Transporter()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Human-friendly tenant-unique reference, e.g. <c>TR-00042</c>.</summary>
    public string Code { get; private set; } = null!;

    public TransporterStatus Status { get; private set; }

    public string LegalName { get; private set; } = null!;

    public string? TradeName { get; private set; }

    public string Pan { get; private set; } = null!;

    public string? Gstin { get; private set; }

    public string ContactPerson { get; private set; } = null!;

    public string Phone { get; private set; } = null!;

    public string Email { get; private set; } = null!;

    public string AddressLine1 { get; private set; } = null!;

    public string? AddressLine2 { get; private set; }

    public string City { get; private set; } = null!;

    public string State { get; private set; } = null!;

    public string Pincode { get; private set; } = null!;

    public ServiceModes ServiceModes { get; private set; }

    /// <summary>What sort of operator this is (a code from the transporter-type list); optional.</summary>
    public string? TypeCode { get; private set; }

    public string? BankAccountHolder { get; private set; }

    [AuditMask]
    public string? BankAccountNumber { get; private set; }

    public string? BankIfsc { get; private set; }

    public string? BankName { get; private set; }

    /// <summary>The onboarding approval currently (or last) attached to this transporter.</summary>
    public Guid? ApprovalRequestId { get; private set; }

    public string? SuspensionReason { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public bool HasBankDetails => !string.IsNullOrEmpty(BankAccountNumber);

    public static Result<Transporter> Create(Guid tenantId, string code, TransporterProfile profile)
    {
        var transporter = new Transporter { TenantId = tenantId, Code = code, Status = TransporterStatus.Draft };
        var applied = transporter.Apply(profile, includeIdentity: true);
        return applied.IsFailure ? applied.Error : transporter;
    }

    public Result UpdateProfile(TransporterProfile profile)
    {
        if (Status == TransporterStatus.PendingApproval)
        {
            return Error.Conflict("transporters.locked", "This transporter is awaiting approval and cannot be edited.");
        }

        var identityEditable = Status is TransporterStatus.Draft or TransporterStatus.Rejected;
        if (!identityEditable && IdentityChanged(profile))
        {
            return Error.Conflict("transporters.identity_frozen", "Legal name, PAN and GSTIN cannot be changed after approval.");
        }

        return Apply(profile, includeIdentity: identityEditable);
    }

    public Result UpdateBank(BankAccount bank)
    {
        if (Status == TransporterStatus.PendingApproval)
        {
            return Error.Conflict("transporters.locked", "This transporter is awaiting approval and cannot be edited.");
        }

        var errors = new FieldErrors()
            .Require("accountHolder", bank.AccountHolder, "Account holder", 200)
            .Require("bankName", bank.BankName, "Bank name", 100);

        var number = new string((bank.AccountNumber ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (number.Length is < 6 or > 24)
        {
            errors.Add("accountNumber", "Account number must be 6 to 24 characters.");
        }

        if (!IndianIdentifiers.IsValidIfsc(bank.Ifsc))
        {
            errors.Add("ifsc", "Enter a valid IFSC code, e.g. HDFC0001234.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        BankAccountHolder = bank.AccountHolder.Trim();
        BankAccountNumber = number;
        BankIfsc = IndianIdentifiers.Normalise(bank.Ifsc);
        BankName = bank.BankName.Trim();
        return Result.Success();
    }

    /// <summary>What is still missing before this transporter can be sent for approval. Empty means ready.</summary>
    public IReadOnlyList<string> MissingForSubmission(IReadOnlyCollection<DocumentKind> documentsOnFile, DocumentPolicy? policy = null)
    {
        policy ??= DocumentPolicy.Default;
        var missing = new List<string>();
        if (!HasBankDetails)
        {
            missing.Add("Bank details");
        }

        if (ServiceModes == ServiceModes.None)
        {
            missing.Add("At least one service mode (FTL / PTL / Dedicated)");
        }

        // A GST certificate is expected whenever the transporter has a GSTIN, unless the tenant has switched that paper off.
        var required = policy.RequiredFor(OwnerKind.Transporter).ToList();
        if (Gstin is not null && policy.For(DocumentKind.GstCertificate).IsActive && !required.Contains(DocumentKind.GstCertificate))
        {
            required.Add(DocumentKind.GstCertificate);
        }

        missing.AddRange(required.Where(k => !documentsOnFile.Contains(k)).Select(k => k == DocumentKind.GstCertificate ? "GST registration certificate" : ComplianceEvaluator.Label(k)));
        return missing;
    }

    /// <param name="requestId">The approval request created for this submission.</param>
    /// <param name="status">Its immediate state: Approved if no approval step applied, otherwise Pending.</param>
    public Result MarkSubmitted(Guid requestId, ApprovalStatus status, DateTimeOffset now)
    {
        if (Status is not (TransporterStatus.Draft or TransporterStatus.Rejected))
        {
            return Error.Conflict("transporters.not_submittable", "Only a draft or rejected transporter can be submitted for approval.");
        }

        ApprovalRequestId = requestId;
        if (status == ApprovalStatus.Approved)
        {
            Activate(now);
        }
        else
        {
            Status = TransporterStatus.PendingApproval;
        }

        return Result.Success();
    }

    /// <summary>
    /// Applies the final decision of the approval attached to this transporter. Outcomes for any other request
    /// (stale, cancelled earlier) are ignored. Returns whether anything changed.
    /// </summary>
    public bool ApplyApprovalOutcome(Guid requestId, ApprovalStatus outcome, DateTimeOffset now)
    {
        if (Status != TransporterStatus.PendingApproval || ApprovalRequestId != requestId)
        {
            return false;
        }

        switch (outcome)
        {
            case ApprovalStatus.Approved:
                Activate(now);
                break;
            case ApprovalStatus.Rejected:
                Status = TransporterStatus.Rejected;
                break;
            case ApprovalStatus.Cancelled:
                Status = TransporterStatus.Draft;
                break;
            default:
                return false;
        }

        return true;
    }

    public Result Suspend(string reason)
    {
        if (Status != TransporterStatus.Active)
        {
            return Error.Conflict("transporters.not_active", "Only an active transporter can be suspended.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("transporters.reason_required", "Say why the transporter is being suspended.");
        }

        Status = TransporterStatus.Suspended;
        SuspensionReason = reason.Trim();
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (Status != TransporterStatus.Suspended)
        {
            return Error.Conflict("transporters.not_suspended", "Only a suspended transporter can be reactivated.");
        }

        Status = TransporterStatus.Active;
        SuspensionReason = null;
        return Result.Success();
    }

    private void Activate(DateTimeOffset now)
    {
        Status = TransporterStatus.Active;
        ActivatedAt = now;
    }

    private bool IdentityChanged(TransporterProfile p) =>
        !string.Equals(LegalName, p.LegalName.Trim(), StringComparison.Ordinal)
        || !string.Equals(Pan, IndianIdentifiers.Normalise(p.Pan), StringComparison.Ordinal)
        || !string.Equals(Gstin, string.IsNullOrWhiteSpace(p.Gstin) ? null : IndianIdentifiers.Normalise(p.Gstin), StringComparison.Ordinal);

    private Result Apply(TransporterProfile p, bool includeIdentity)
    {
        var errors = new FieldErrors()
            .Require("legalName", p.LegalName, "Legal name", 200)
            .Require("contactPerson", p.ContactPerson, "Contact person", 150)
            .Require("addressLine1", p.Address.Line1, "Address", 200)
            .Require("city", p.Address.City, "City", 100)
            .Require("state", p.Address.State, "State", 100);

        if (p.TradeName?.Length > 200)
        {
            errors.Add("tradeName", "Trade name must be at most 200 characters.");
        }

        var pan = IndianIdentifiers.Normalise(p.Pan);
        var gstin = string.IsNullOrWhiteSpace(p.Gstin) ? null : IndianIdentifiers.Normalise(p.Gstin);
        if (!IndianIdentifiers.IsValidPan(pan))
        {
            errors.Add("pan", "Enter a valid 10-character PAN, e.g. ABCDE1234F.");
        }

        if (gstin is not null)
        {
            if (!IndianIdentifiers.IsValidGstin(gstin))
            {
                errors.Add("gstin", "Enter a valid 15-character GSTIN.");
            }
            else if (IndianIdentifiers.IsValidPan(pan) && IndianIdentifiers.PanFromGstin(gstin) != pan)
            {
                errors.Add("gstin", "The GSTIN does not belong to this PAN.");
            }
        }

        if (!IndianIdentifiers.IsValidMobile(p.Phone))
        {
            errors.Add("phone", "Enter a valid 10-digit mobile number.");
        }

        if (string.IsNullOrWhiteSpace(p.Email) || !p.Email.Contains('@', StringComparison.Ordinal) || p.Email.Trim().Length > 254)
        {
            errors.Add("email", "Enter a valid email address.");
        }

        if (!IndianIdentifiers.IsValidPincode(p.Address.Pincode))
        {
            errors.Add("pincode", "Enter a valid 6-digit pincode.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        if (includeIdentity)
        {
            LegalName = p.LegalName.Trim();
            Pan = pan;
            Gstin = gstin;
        }

        TradeName = string.IsNullOrWhiteSpace(p.TradeName) ? null : p.TradeName.Trim();
        ContactPerson = p.ContactPerson.Trim();
        Phone = IndianIdentifiers.NormaliseMobile(p.Phone);
        Email = p.Email.Trim().ToLowerInvariant();
        AddressLine1 = p.Address.Line1.Trim();
        AddressLine2 = string.IsNullOrWhiteSpace(p.Address.Line2) ? null : p.Address.Line2.Trim();
        City = p.Address.City.Trim();
        State = p.Address.State.Trim();
        Pincode = p.Address.Pincode.Trim();
        ServiceModes = p.ServiceModes;
        TypeCode = string.IsNullOrWhiteSpace(p.TypeCode) ? null : MasterItem.NormaliseCode(p.TypeCode);
        return Result.Success();
    }
}
