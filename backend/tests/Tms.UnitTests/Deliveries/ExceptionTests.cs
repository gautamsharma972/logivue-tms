using Tms.Modules.Deliveries.Domain;
using static Tms.UnitTests.Deliveries.DeliveryTestData;

namespace Tms.UnitTests.Deliveries;

public class ExceptionTests
{
    private static DeliveryException Raise(ExceptionType type = ExceptionType.Shortage) =>
        DeliveryException.Raise(Tenant, "EXC-00001", Arrived(), null, type, ExceptionSeverity.Medium, "Short by 3", Now.AddHours(24), Now);

    [Fact]
    public void A_raised_exception_is_open_with_a_due_date_and_announces_itself()
    {
        var e = Raise();

        e.Status.ShouldBe(ExceptionStatus.Open);
        e.IsOpen.ShouldBeTrue();
        e.DueAt.ShouldBe(Now.AddHours(24));
        e.ResponsibleParty.ShouldBe(ResponsibleParty.Unknown); // never assumed
        e.DomainEvents.Count.ShouldBe(1);
    }

    [Fact]
    public void An_exception_needs_an_owner_to_be_assigned_and_becomes_acknowledged()
    {
        var e = Raise();
        e.Assign(null, " ", null, null, null, Now).Error.Code.ShouldBe("exceptions.owner_required");

        var owner = Guid.NewGuid();
        e.Assign(owner, "Claims", Now.AddDays(2), ExceptionSeverity.High, owner, Now).IsSuccess.ShouldBeTrue();

        (e.OwnerUserId, e.Department, e.DueAt, e.Severity, e.Status).ShouldBe((owner, "Claims", Now.AddDays(2), ExceptionSeverity.High, ExceptionStatus.Acknowledged));
    }

    [Fact]
    public void Escalation_needs_a_reason_and_raises_the_severity_one_level()
    {
        var e = Raise();
        e.Escalate(" ", null, Now).Error.Code.ShouldBe("exceptions.reason_required");
        e.Escalate("Customer threatening to cancel", null, Now).IsSuccess.ShouldBeTrue();

        e.Status.ShouldBe(ExceptionStatus.Escalated);
        e.Severity.ShouldBe(ExceptionSeverity.High);
        e.EscalatedAt.ShouldBe(Now);
        e.Escalate("again", null, Now).IsFailure.ShouldBeTrue(); // already escalated
    }

    [Fact]
    public void Resolving_records_the_finding_and_closing_needs_it_resolved_first()
    {
        var e = Raise();
        e.Close(null, Now).Error.Code.ShouldBe("exceptions.not_resolved");
        e.Resolve(" ", null, ResponsibleParty.Unknown, null, null, null, null, Now).Error.Code.ShouldBe("exceptions.resolution_required");

        e.Resolve("Credit note issued", "Short loaded at the warehouse", ResponsibleParty.Warehouse, "Raised with the warehouse", 1_500m, "CLM-7", null, Now.AddDays(1)).IsSuccess.ShouldBeTrue();

        (e.Status, e.ResponsibleParty, e.FinancialImpact, e.ClaimReference, e.ResolvedAt).ShouldBe((ExceptionStatus.Resolved, ResponsibleParty.Warehouse, 1_500m, "CLM-7", (DateTimeOffset?)Now.AddDays(1)));
        e.IsOpen.ShouldBeFalse();
        e.Close(null, Now.AddDays(2)).IsSuccess.ShouldBeTrue();
        e.Status.ShouldBe(ExceptionStatus.Closed);
        e.Assign(Guid.NewGuid(), null, null, null, null, Now).IsFailure.ShouldBeTrue(); // a finished exception is not reopened by assignment
    }

    [Fact]
    public void Investigation_and_notes_leave_a_trail()
    {
        var e = Raise();
        e.Investigate("Seal broken", ResponsibleParty.Transporter, null, Now).IsSuccess.ShouldBeTrue();
        e.AddNote("Called the driver", null, Now.AddMinutes(5)).IsSuccess.ShouldBeTrue();
        e.AddNote(" ", null, Now).Error.Code.ShouldBe("exceptions.note_required");

        e.Status.ShouldBe(ExceptionStatus.UnderInvestigation);
        e.Notes.Select(n => n.Text).ShouldBe(["Under investigation.", "Called the driver"]);
    }

    [Fact]
    public void A_sync_record_remembers_the_first_answer_and_counts_retries()
    {
        var sync = SyncRecord.Begin(Tenant, "client-1", "device-1", "complete", Guid.NewGuid(), Now, Now, Now);
        sync.SyncStatus.ShouldBe(SyncStatus.Pending);
        sync.Fail("timeout");
        sync.Retry(Now.AddMinutes(1));
        sync.SyncAttempt.ShouldBe(2);
        sync.Succeed(Guid.NewGuid(), "{}");
        (sync.SyncStatus, sync.LastError).ShouldBe((SyncStatus.Synced, null));
    }
}
