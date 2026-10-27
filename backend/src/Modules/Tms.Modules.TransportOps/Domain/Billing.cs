using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Domain;

/// <summary>
/// The result of pricing one LR: a frozen snapshot of what was used (agreement revision, rate, times, weights) and what came out, line by line. A recalculation makes a new version
/// and marks the old one superseded, so charges are never counted twice and an old figure can always be explained.
/// </summary>
public sealed class FreightCalculation : AggregateRoot, ITenantScoped
{
    private FreightCalculation()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid LrId { get; private set; }

    public string LrNumber { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public int CalcVersion { get; private set; }

    public CalculationStatus Status { get; private set; }

    public Guid? AgreementId { get; private set; }

    public int? AgreementRevision { get; private set; }

    public Guid? RateId { get; private set; }

    public decimal BillableWeightKg { get; private set; }

    public string? WeightBasisUsed { get; private set; }

    public decimal BasicFreight { get; private set; }

    public decimal OtherCharges { get; private set; }

    /// <summary>Detention from the rule and the recorded times.</summary>
    public decimal DetentionAmount { get; private set; }

    /// <summary>A reviewer's replacement for detention when the times were missing or wrong; the reason is kept.</summary>
    public decimal? DetentionOverride { get; private set; }

    public string? DetentionOverrideReason { get; private set; }

    public decimal EffectiveDetention => DetentionOverride ?? DetentionAmount;

    public decimal TotalPreTax => BasicFreight + OtherCharges + EffectiveDetention;

    public List<string> Holds { get; private set; } = [];

    public List<FreightLine> Lines { get; private set; } = [];

    public DetentionResult? Detention { get; private set; }

    public List<string> Trace { get; private set; } = [];

    public LrFacts? Input { get; private set; }

    public bool PodAccepted { get; private set; }

    public bool BilledWithoutPod { get; private set; }

    public DateTimeOffset CalculatedAt { get; private set; }

    public Guid? CalculatedBy { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? ReviewNote { get; private set; }

    public Guid? InvoiceId { get; private set; }

    public static FreightCalculation Create(Guid tenantId, Lr lr, int version, CustomerAgreement? agreement, FreightResult? freight, DetentionResult? detention, LrFacts input, bool podAccepted,
        bool billedWithoutPod, IEnumerable<string> holds, string? failure, DateTimeOffset at, Guid? by)
    {
        var calc = new FreightCalculation
        {
            TenantId = tenantId, LrId = lr.Id, LrNumber = lr.Number, CustomerId = lr.CustomerId, CalcVersion = version, AgreementId = agreement?.Id, AgreementRevision = agreement?.Revision,
            Input = input, PodAccepted = podAccepted, BilledWithoutPod = billedWithoutPod, CalculatedAt = at, CalculatedBy = by, Detention = detention,
        };
        if (freight is not null)
        {
            calc.RateId = freight.RateId;
            calc.BillableWeightKg = freight.BillableWeightKg;
            calc.WeightBasisUsed = freight.WeightBasisUsed;
            calc.BasicFreight = freight.BasicFreight;
            calc.OtherCharges = freight.OtherCharges;
            calc.Lines = [.. freight.Lines];
            calc.Trace = [.. freight.Trace];
        }

        if (detention is { Status: DetentionStatus.Calculated })
        {
            calc.DetentionAmount = detention.Amount;
            if (detention.Amount > 0)
            {
                calc.Lines.Add(new FreightLine("DETENTION", "Detention", detention.Amount, $"{detention.Units:0.##} {detention.ChargeUnit?.ToString().ToLowerInvariant()}(s) x {detention.Rate:0.00}"));
            }

            calc.Trace.AddRange(detention.Trace);
        }
        else if (detention is not null)
        {
            calc.Trace.AddRange(detention.Trace);
        }

        calc.Holds = [.. holds];
        if (failure is not null)
        {
            calc.Holds.Add(failure);
        }

        calc.Status = calc.Holds.Count > 0 ? CalculationStatus.OnHold : CalculationStatus.Calculated;
        return calc;
    }

    /// <summary>True when recalculating would give the same agreement, rate, amounts and holds, so a reviewer's approval of this version still stands.</summary>
    public bool SameOutcomeAs(FreightCalculation other) =>
        AgreementId == other.AgreementId && AgreementRevision == other.AgreementRevision && RateId == other.RateId && BillableWeightKg == other.BillableWeightKg
        && BasicFreight == other.BasicFreight && OtherCharges == other.OtherCharges && DetentionAmount == other.DetentionAmount
        && PodAccepted == other.PodAccepted && BilledWithoutPod == other.BilledWithoutPod && Holds.SequenceEqual(other.Holds);

    public bool IsBillable => Status is CalculationStatus.Reviewed && InvoiceId is null;

    /// <summary>A reviewer confirms the figures. A calculation that is on hold cannot be reviewed until the hold is cleared.</summary>
    public Result Review(Guid? by, DateTimeOffset at, string? note)
    {
        if (Status != CalculationStatus.Calculated)
        {
            return Error.Conflict("ops.calc_state", Status == CalculationStatus.OnHold ? "This calculation is on hold: resolve the hold before reviewing it." : "Only a calculated charge can be reviewed.");
        }

        Status = CalculationStatus.Reviewed;
        ReviewedBy = by;
        ReviewedAt = at;
        ReviewNote = Customer.Clean(note);
        return Result.Success();
    }

    /// <summary>
    /// Clears a detention hold with an amount the reviewer decides (zero is allowed), and the reason. Only a hold about detention can be cleared this way; a missing rate or an
    /// unaccepted POD cannot.
    /// </summary>
    public Result ResolveDetentionHold(decimal amount, string reason, Guid? by, DateTimeOffset at)
    {
        if (Status != CalculationStatus.OnHold || Detention is not { Status: DetentionStatus.OnHold })
        {
            return Error.Conflict("ops.calc_state", "This calculation has no detention hold to resolve.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("ops.calc_reason", "A reason is required to resolve a detention hold.");
        }

        if (amount < 0)
        {
            return Error.Validation("ops.calc_amount", "Detention cannot be negative.");
        }

        DetentionOverride = Money.Round(amount);
        DetentionOverrideReason = reason.Trim();
        Holds.RemoveAll(h => h == Detention.HoldReason);
        if (Holds.Count == 0)
        {
            Status = CalculationStatus.Calculated;
        }

        return Result.Success();
    }

    public void Supersede() => Status = CalculationStatus.Superseded;

    public void MarkInvoiced(Guid invoiceId)
    {
        InvoiceId = invoiceId;
        Status = CalculationStatus.Invoiced;
    }

    public void ReleaseFromInvoice()
    {
        InvoiceId = null;
        Status = CalculationStatus.Reviewed;
    }
}

public sealed class InvoiceLine : Entity, ITenantScoped
{
    private InvoiceLine()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid LrId { get; private set; }

    public string LrNumber { get; private set; } = null!;

    public Guid FreightCalculationId { get; private set; }

    public int CalculationVersion { get; private set; }

    public Guid? AgreementId { get; private set; }

    public int? AgreementRevision { get; private set; }

    public Guid? PodId { get; private set; }

    public string Description { get; private set; } = null!;

    public decimal BasicFreight { get; private set; }

    public decimal Detention { get; private set; }

    public decimal OtherCharges { get; private set; }

    public decimal Taxable { get; private set; }

    public decimal Cgst { get; private set; }

    public decimal Sgst { get; private set; }

    public decimal Igst { get; private set; }

    public static InvoiceLine Create(Guid tenantId, Guid invoiceId, int lineNumber, FreightCalculation calc, Guid? podId, string description, TaxResult tax) => new()
    {
        TenantId = tenantId, InvoiceId = invoiceId, LineNumber = lineNumber, LrId = calc.LrId, LrNumber = calc.LrNumber, FreightCalculationId = calc.Id, CalculationVersion = calc.CalcVersion,
        AgreementId = calc.AgreementId, AgreementRevision = calc.AgreementRevision, PodId = podId, Description = description, BasicFreight = calc.BasicFreight, Detention = calc.EffectiveDetention,
        OtherCharges = calc.OtherCharges, Taxable = calc.TotalPreTax, Cgst = tax.Cgst, Sgst = tax.Sgst, Igst = tax.Igst,
    };
}

public sealed class InvoiceSubmission : Entity, ITenantScoped
{
    private InvoiceSubmission()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public int Attempt { get; private set; }

    public SubmissionStatus Status { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public Guid? SubmittedBy { get; private set; }

    public string? PortalReference { get; private set; }

    public string? DocumentReference { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public string? Notes { get; private set; }

    public static InvoiceSubmission Create(Guid tenantId, Guid invoiceId, int attempt, DateTimeOffset at, Guid? by, string? portalReference, string? documentReference, string? notes) => new()
    {
        TenantId = tenantId, InvoiceId = invoiceId, Attempt = attempt, Status = SubmissionStatus.Submitted, SubmittedAt = at, SubmittedBy = by, PortalReference = Customer.Clean(portalReference),
        DocumentReference = Customer.Clean(documentReference), Notes = Customer.Clean(notes),
    };

    public void Acknowledge(DateTimeOffset at, string? portalReference)
    {
        Status = SubmissionStatus.Acknowledged;
        DecidedAt = at;
        PortalReference = Customer.Clean(portalReference) ?? PortalReference;
    }

    public void Reject(DateTimeOffset at, string reason)
    {
        Status = SubmissionStatus.Rejected;
        DecidedAt = at;
        RejectionReason = reason.Trim();
    }
}

public sealed class Payment : Entity, ITenantScoped
{
    private Payment()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>What reached the bank, as on the bank statement. TDS is kept apart in <see cref="TdsAmount"/>.</summary>
    public decimal Amount { get; private set; }

    public decimal TdsAmount { get; private set; }

    public DateOnly ReceivedOn { get; private set; }

    public string? Mode { get; private set; }

    public string? Reference { get; private set; }

    public string? Remarks { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public Guid? RecordedBy { get; private set; }

    public static Payment Create(Guid tenantId, Guid invoiceId, Guid customerId, decimal amount, DateOnly receivedOn, string? mode, string? reference, string? remarks, DateTimeOffset at, Guid? by, decimal tds = 0) => new()
    {
        TenantId = tenantId, InvoiceId = invoiceId, CustomerId = customerId, Amount = Money.Round(amount), TdsAmount = Money.Round(tds), ReceivedOn = receivedOn, Mode = Customer.Clean(mode), Reference = Customer.Clean(reference),
        Remarks = Customer.Clean(remarks), RecordedAt = at, RecordedBy = by,
    };
}

/// <summary>The last number issued in a series for a financial year. Numbers are taken inside the invoice's own transaction, so a failed invoice leaves no gap.</summary>
[AuditIgnore]
public sealed class InvoiceSeries : Entity, ITenantScoped
{
    private InvoiceSeries()
    {
    }

    public Guid TenantId { get; private set; }

    public string Series { get; private set; } = null!;

    public string FinancialYear { get; private set; } = null!;

    public long LastNumber { get; private set; }
}

/// <summary>A customer invoice. Once issued it is not recalculated; a change is a credit note or a new invoice.</summary>
public sealed class Invoice : AggregateRoot, ITenantScoped
{
    private Invoice()
    {
    }

    public Guid TenantId { get; private set; }

    public string Number { get; private set; } = null!;

    public string Series { get; private set; } = null!;

    public string FinancialYear { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public string CustomerName { get; private set; } = null!;

    public string? CustomerGstin { get; private set; }

    public string? BillingAddress { get; private set; }

    public string? CustomerReference { get; private set; }

    public DateOnly InvoiceDate { get; private set; }

    public DateOnly DueDate { get; private set; }

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    public SubmissionStatus Submission { get; private set; } = SubmissionStatus.NotSubmitted;

    public TaxMode TaxMode { get; private set; }

    public decimal TaxRatePercent { get; private set; }

    public string? TaxNote { get; private set; }

    public string? Sac { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal Cgst { get; private set; }

    public decimal Sgst { get; private set; }

    public decimal Igst { get; private set; }

    public decimal GrandTotal { get; private set; }

    public decimal AmountPaid { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public string? CancelReason { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    public Guid? IssuedBy { get; private set; }

    public string? Notes { get; private set; }

    public List<InvoiceLine> Lines { get; private set; } = [];

    public List<InvoiceSubmission> Submissions { get; private set; } = [];

    /// <summary>Of <see cref="AmountPaid"/> (everything settled, cash plus TDS), the part the customer deducted as tax at source and never sent to the bank.</summary>
    public decimal TdsDeducted { get; private set; }

    public decimal Outstanding => GrandTotal - AmountPaid;

    public PaymentState PaymentState => AmountPaid <= 0 ? PaymentState.Unpaid : AmountPaid >= GrandTotal ? PaymentState.Paid : PaymentState.PartiallyPaid;

    public static Invoice Create(Guid tenantId, string number, string series, string financialYear, Customer customer, TaxSettings tax, DateOnly invoiceDate, int dueDays, string? customerReference, string? notes) => new()
    {
        TenantId = tenantId, Number = number, Series = series, FinancialYear = financialYear, CustomerId = customer.Id, CustomerName = customer.Name, CustomerGstin = customer.Gstin,
        BillingAddress = string.Join(", ", new[] { customer.BillingLine1, customer.BillingCity, customer.BillingState, customer.BillingPincode }.Where(x => !string.IsNullOrWhiteSpace(x))),
        CustomerReference = Customer.Clean(customerReference), InvoiceDate = invoiceDate, DueDate = invoiceDate.AddDays(dueDays), TaxMode = tax.Mode, TaxRatePercent = tax.RatePercent, Sac = tax.Sac,
        TaxNote = tax.Note, Notes = Customer.Clean(notes),
    };

    public void AddLine(InvoiceLine line)
    {
        Lines.Add(line);
        Subtotal += line.Taxable;
        Cgst += line.Cgst;
        Sgst += line.Sgst;
        Igst += line.Igst;
        GrandTotal = Subtotal + Cgst + Sgst + Igst;
    }

    /// <summary>The invoice is waiting for the approval request to be decided.</summary>
    public void AwaitApproval(Guid requestId)
    {
        ApprovalRequestId = requestId;
        Status = InvoiceStatus.PendingApproval;
    }

    /// <summary>Applies the outcome of an approval request. Ignores outcomes for other requests and for invoices that are no longer waiting, so a repeated notice changes nothing.</summary>
    public bool ApplyApprovalOutcome(Guid requestId, bool approved, DateTimeOffset at)
    {
        if (Status != InvoiceStatus.PendingApproval || ApprovalRequestId != requestId)
        {
            return false;
        }

        if (approved)
        {
            Issue(null, at);
        }
        else
        {
            Status = InvoiceStatus.Cancelled;
            CancelReason = "Not approved.";
        }

        return true;
    }

    public void Issue(Guid? by, DateTimeOffset at)
    {
        Status = InvoiceStatus.Issued;
        IssuedAt = at;
        IssuedBy = by;
        Submission = SubmissionStatus.NotSubmitted;
    }

    public Result Cancel(string reason)
    {
        if (Status == InvoiceStatus.Cancelled)
        {
            return Error.Conflict("ops.invoice_state", "The invoice is already cancelled.");
        }

        if (AmountPaid > 0)
        {
            return Error.Conflict("ops.invoice_paid", "An invoice with payments cannot be cancelled.");
        }

        if (Submission is SubmissionStatus.Submitted or SubmissionStatus.Acknowledged)
        {
            return Error.Conflict("ops.invoice_submitted", "An invoice that has been sent to the customer cannot be cancelled here.");
        }

        Status = InvoiceStatus.Cancelled;
        CancelReason = reason.Trim();
        return Result.Success();
    }

    public Result Submit(Guid? by, DateTimeOffset at, string? portalReference, string? documentReference, string? notes)
    {
        if (Status != InvoiceStatus.Issued)
        {
            return Error.Conflict("ops.invoice_state", "Only an issued invoice can be submitted.");
        }

        if (Submission is SubmissionStatus.Submitted or SubmissionStatus.Acknowledged)
        {
            return Error.Conflict("ops.invoice_submitted", "This invoice has already been submitted.");
        }

        Submissions.Add(InvoiceSubmission.Create(TenantId, Id, Submissions.Count + 1, at, by, portalReference, documentReference, notes));
        Submission = SubmissionStatus.Submitted;
        return Result.Success();
    }

    public Result Acknowledge(DateTimeOffset at, string? portalReference)
    {
        var current = Submissions.OrderByDescending(s => s.Attempt).FirstOrDefault();
        if (current is null || Submission != SubmissionStatus.Submitted)
        {
            return Error.Conflict("ops.submission_state", "There is no submission waiting for the customer's decision.");
        }

        current.Acknowledge(at, portalReference);
        Submission = SubmissionStatus.Acknowledged;
        return Result.Success();
    }

    public Result RejectSubmission(DateTimeOffset at, string reason)
    {
        var current = Submissions.OrderByDescending(s => s.Attempt).FirstOrDefault();
        if (current is null || Submission != SubmissionStatus.Submitted)
        {
            return Error.Conflict("ops.submission_state", "There is no submission waiting for the customer's decision.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("ops.submission_reason", "The customer's rejection reason is required.");
        }

        current.Reject(at, reason);
        Submission = SubmissionStatus.ResubmissionRequired;
        return Result.Success();
    }

    /// <summary><paramref name="amount"/> is what reached the bank; <paramref name="tds"/> is tax the customer deducted at source. Both settle the invoice.</summary>
    public Result ApplyPayment(decimal amount, decimal tds = 0)
    {
        if (Status != InvoiceStatus.Issued)
        {
            return Error.Conflict("ops.invoice_state", "Payments can only be recorded against an issued invoice.");
        }

        if (amount <= 0)
        {
            return Error.Validation("ops.payment_amount", "The payment must be more than zero.");
        }

        if (tds < 0)
        {
            return Error.Validation("ops.payment_tds", "The TDS cannot be negative.");
        }

        var settled = Money.Round(amount) + Money.Round(tds);
        if (settled > Outstanding)
        {
            return Error.Conflict("ops.payment_mismatch", $"The payment plus TDS ({settled:0.00}) is more than the outstanding balance ({Outstanding:0.00}). Check the amounts or the invoice number.");
        }

        AmountPaid += settled;
        TdsDeducted += Money.Round(tds);
        return Result.Success();
    }
}

/// <summary>A per-tenant setting stored as JSON, read with built-in defaults (never hard-coded rules).</summary>
[AuditIgnore]
public sealed class OpsSetting : Entity, ITenantScoped
{
    private OpsSetting()
    {
    }

    public Guid TenantId { get; private set; }

    public string Key { get; private set; } = null!;

    public string ValueJson { get; private set; } = null!;

    public static OpsSetting Create(Guid tenantId, string key, string json) => new() { TenantId = tenantId, Key = key, ValueJson = json };

    public void Set(string json) => ValueJson = json;
}

public static class OpsSettingKeys
{
    public const string Arrival = "arrival";
    public const string Numbering = "numbering";
    public const string Pod = "pod";
    public const string Invoice = "invoice";
    public const string Fleet = "fleet";
}

/// <summary>How the arrival check and the QR code behave. Radius is per location; these are the limits that apply to every location.</summary>
public sealed record ArrivalSetting(int MaxAccuracyM = 100, int TokenValidityDays = 14);

public sealed record NumberingSetting(string RequestPrefix = "REQ", string LrPrefix = "LR", string TripPrefix = "TRP", int Digits = 6);

public sealed record PodSetting(int MaxFiles = PodSetting.Ceiling, long MaxBytes = 10 * 1024 * 1024)
{
    /// <summary>No POD submission, from the driver or the office, carries more than this many photos or files, whatever an older setting says.</summary>
    public const int Ceiling = 10;

    public int EffectiveMaxFiles => Math.Clamp(MaxFiles, 1, Ceiling);
}

/// <summary>The transporter whose fleet the vehicle pick-list shows (the vehicle master belongs to a transporter record).</summary>
public sealed record FleetSetting(Guid? OperatingTransporterId = null);

/// <param name="SupplierName">The name printed on the invoice ("For : ...").</param>
/// <param name="SupplierState">The state's name as printed under the address.</param>
/// <param name="LogoKey">Stored file keys of the logo and signature images. Set only through the branding upload, never through the settings form.</param>
/// <param name="Declaration">The declaration printed on the invoice; empty means the standard wording.</param>
public sealed record InvoiceSetting(
    string SeriesPrefix = "INV", string? SupplierGstin = null, string? SupplierStateCode = null, bool RequireApproval = false,
    string? SupplierName = null, string? SupplierAddress = null, string? SupplierState = null, string? LogoKey = null, string? SignatureKey = null, string? Declaration = null)
{
    public const string StandardDeclaration = "We declare that this invoice shows the actual price of the goods described and that all particulars are true and correct.";
}
