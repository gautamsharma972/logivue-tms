using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.UnitTests.Transporters;

public class TransporterTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    internal static TransporterProfile Profile(string pan = "AAPFU0939F", string? gstin = "27AAPFU0939F1ZV", string phone = "98765 43210") =>
        new("Shree Roadlines Pvt Ltd", " Shree ", pan, gstin, "R. Patil", phone, "Ops@Shree.example", new Address("Plot 4, MIDC", null, "Pune", "Maharashtra", "411019"), ServiceModes.Ftl | ServiceModes.Ptl);

    private static Transporter NewDraft() => Transporter.Create(Tenant, "TR-00001", Profile()).Value;

    private static readonly BankAccount Bank = new("Shree Roadlines Pvt Ltd", "123456789012", "hdfc0001234", "HDFC Bank");

    private static Transporter ReadyToSubmit()
    {
        var t = NewDraft();
        t.UpdateBank(Bank).IsSuccess.ShouldBeTrue();
        return t;
    }

    private static Transporter Pending(out Guid requestId)
    {
        var t = ReadyToSubmit();
        requestId = Guid.NewGuid();
        t.MarkSubmitted(requestId, ApprovalStatus.Pending, Now).IsSuccess.ShouldBeTrue();
        return t;
    }

    [Fact]
    public void Create_NormalisesAndStartsAsDraft()
    {
        var t = NewDraft();

        t.Status.ShouldBe(TransporterStatus.Draft);
        t.Phone.ShouldBe("9876543210");
        t.Email.ShouldBe("ops@shree.example");
        t.TradeName.ShouldBe("Shree");
        t.Pan.ShouldBe("AAPFU0939F");
    }

    [Fact]
    public void Create_ReportsEveryInvalidFieldAtOnce()
    {
        var result = Transporter.Create(Tenant, "TR-1", Profile(pan: "BAD", gstin: "27AAPFU0939F1ZW", phone: "123") with { LegalName = " " });

        result.IsFailure.ShouldBeTrue();
        result.Error.ValidationErrors!.Keys.ShouldBe(["legalName", "pan", "gstin", "phone"], ignoreOrder: true);
    }

    [Fact]
    public void Gstin_MustMatchThePan()
    {
        var result = Transporter.Create(Tenant, "TR-1", Profile(pan: "ABCDE1234F"));

        result.Error.ValidationErrors!["gstin"].ShouldContain("The GSTIN does not belong to this PAN.");
    }

    [Fact]
    public void Gstin_IsOptional()
    {
        Transporter.Create(Tenant, "TR-1", Profile(gstin: null)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Submission_ListsEverythingMissing()
    {
        var t = NewDraft();

        t.MissingForSubmission([]).ShouldBe(["Bank details", "PAN card", "Cancelled cheque", "GST registration certificate"]);

        t.UpdateBank(Bank);
        t.MissingForSubmission([DocumentKind.PanCard, DocumentKind.CancelledCheque, DocumentKind.GstCertificate]).ShouldBeEmpty();
    }

    [Fact]
    public void WithoutAGstin_NoGstCertificateIsNeeded()
    {
        var t = Transporter.Create(Tenant, "TR-1", Profile(gstin: null)).Value;
        t.UpdateBank(Bank);

        t.MissingForSubmission([DocumentKind.PanCard, DocumentKind.CancelledCheque]).ShouldBeEmpty();
    }

    [Fact]
    public void BankDetails_AreValidatedAndNormalised()
    {
        var t = NewDraft();

        t.UpdateBank(Bank with { Ifsc = "BAD" }).Error.ValidationErrors!.ShouldContainKey("ifsc");
        t.UpdateBank(Bank with { AccountNumber = "12" }).Error.ValidationErrors!.ShouldContainKey("accountNumber");

        t.UpdateBank(Bank with { AccountNumber = "1234 5678 9012" }).IsSuccess.ShouldBeTrue();
        t.BankAccountNumber.ShouldBe("123456789012");
        t.BankIfsc.ShouldBe("HDFC0001234");
    }

    [Fact]
    public void Submitting_LocksTheRecordUntilTheDecision()
    {
        var t = Pending(out _);

        t.Status.ShouldBe(TransporterStatus.PendingApproval);
        t.UpdateProfile(Profile()).Error.Code.ShouldBe("transporters.locked");
        t.UpdateBank(Bank).Error.Code.ShouldBe("transporters.locked");
        t.MarkSubmitted(Guid.NewGuid(), ApprovalStatus.Pending, Now).Error.Code.ShouldBe("transporters.not_submittable");
    }

    [Fact]
    public void AnInstantlyApprovedSubmission_ActivatesImmediately()
    {
        var t = ReadyToSubmit();

        t.MarkSubmitted(Guid.NewGuid(), ApprovalStatus.Approved, Now).IsSuccess.ShouldBeTrue();

        t.Status.ShouldBe(TransporterStatus.Active);
        t.ActivatedAt.ShouldBe(Now);
    }

    [Fact]
    public void ApprovalOutcome_DrivesTheStatus()
    {
        var approved = Pending(out var a);
        approved.ApplyApprovalOutcome(a, ApprovalStatus.Approved, Now).ShouldBeTrue();
        approved.Status.ShouldBe(TransporterStatus.Active);

        var rejected = Pending(out var r);
        rejected.ApplyApprovalOutcome(r, ApprovalStatus.Rejected, Now).ShouldBeTrue();
        rejected.Status.ShouldBe(TransporterStatus.Rejected);

        var cancelled = Pending(out var c);
        cancelled.ApplyApprovalOutcome(c, ApprovalStatus.Cancelled, Now).ShouldBeTrue();
        cancelled.Status.ShouldBe(TransporterStatus.Draft);
    }

    [Fact]
    public void AnOutcomeForSomeOtherRequest_IsIgnored()
    {
        var t = Pending(out _);

        t.ApplyApprovalOutcome(Guid.NewGuid(), ApprovalStatus.Approved, Now).ShouldBeFalse();

        t.Status.ShouldBe(TransporterStatus.PendingApproval);
    }

    [Fact]
    public void ARejectedTransporter_CanBeCorrectedAndResubmitted()
    {
        var t = Pending(out var first);
        t.ApplyApprovalOutcome(first, ApprovalStatus.Rejected, Now);

        t.UpdateProfile(Profile() with { LegalName = "Shree Roadlines LLP" }).IsSuccess.ShouldBeTrue();
        t.MarkSubmitted(Guid.NewGuid(), ApprovalStatus.Pending, Now).IsSuccess.ShouldBeTrue();

        t.Status.ShouldBe(TransporterStatus.PendingApproval);
    }

    [Fact]
    public void AfterApproval_IdentityIsFrozenButContactDetailsCanChange()
    {
        var t = Pending(out var id);
        t.ApplyApprovalOutcome(id, ApprovalStatus.Approved, Now);

        t.UpdateProfile(Profile() with { LegalName = "Someone Else Ltd" }).Error.Code.ShouldBe("transporters.identity_frozen");
        t.UpdateProfile(Profile(pan: "ABCDE1234F", gstin: null)).Error.Code.ShouldBe("transporters.identity_frozen");

        t.UpdateProfile(Profile() with { Phone = "9123456780", ContactPerson = "S. Kulkarni" }).IsSuccess.ShouldBeTrue();
        t.Phone.ShouldBe("9123456780");
        t.LegalName.ShouldBe("Shree Roadlines Pvt Ltd");
    }

    [Fact]
    public void Suspension_NeedsAReason_AndIsReversible()
    {
        var t = Pending(out var id);
        t.Suspend("Too early").Error.Code.ShouldBe("transporters.not_active");
        t.ApplyApprovalOutcome(id, ApprovalStatus.Approved, Now);

        t.Suspend(" ").Error.Code.ShouldBe("transporters.reason_required");
        t.Suspend("Insurance lapsed on all vehicles").IsSuccess.ShouldBeTrue();
        t.Status.ShouldBe(TransporterStatus.Suspended);
        t.SuspensionReason.ShouldBe("Insurance lapsed on all vehicles");

        t.Reactivate().IsSuccess.ShouldBeTrue();
        t.Status.ShouldBe(TransporterStatus.Active);
        t.SuspensionReason.ShouldBeNull();
        t.Reactivate().Error.Type.ShouldBe(ErrorType.Conflict);
    }
}
