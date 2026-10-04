using FluentAssertions;
using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Pure compliance rules: approval blocks, expiry states, vehicle allocation blocks.</summary>
public class ComplianceEvaluatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 4);

    private static readonly DocumentType Gst = new() { Id = 1, Code = "GST_CERT", Name = "GST Certificate", IsMandatory = true };
    private static readonly DocumentType Insurance = new()
    {
        Id = 4, Code = "INSURANCE", Name = "Insurance", IsMandatory = true,
        IsTransporterLevel = false, IsVehicleLevel = true, ExpiryRequired = true,
        RenewalReminderDays = 30, BlockAllocationWhenExpired = true
    };

    private static readonly DocumentType[] Types = [Gst, Insurance];

    private static TransporterDocument Doc(long id, DocumentType type, DateOnly? expiry, DocumentVerificationStatus status, long? vehicleId = null) => new()
    {
        Id = id, TransporterId = 1, DocumentTypeId = type.Id, VehicleId = vehicleId,
        ExpiryDate = expiry, VerificationStatus = status
    };

    [Fact]
    public void Missing_mandatory_transporter_document_blocks_approval()
    {
        var report = ComplianceEvaluator.Evaluate(1, Today, 30, Types, [], []);

        report.ApprovalBlocked.Should().BeTrue();
        report.Overall.Should().Be(ComplianceOverallStatus.NonCompliant);
        report.Items.Should().ContainSingle(i => i.DocumentTypeCode == "GST_CERT" && i.State == ComplianceItemState.Missing && i.BlocksApproval);
    }

    [Fact]
    public void Verified_valid_documents_are_compliant()
    {
        var docs = new[] { Doc(10, Gst, null, DocumentVerificationStatus.Verified) };

        var report = ComplianceEvaluator.Evaluate(1, Today, 30, [Gst], docs, []);

        report.ApprovalBlocked.Should().BeFalse();
        report.Overall.Should().Be(ComplianceOverallStatus.Compliant);
    }

    [Fact]
    public void Unverified_document_blocks_approval_until_verified()
    {
        var docs = new[] { Doc(10, Gst, null, DocumentVerificationStatus.Pending) };

        var report = ComplianceEvaluator.Evaluate(1, Today, 30, [Gst], docs, []);

        report.ApprovalBlocked.Should().BeTrue();
        report.Items.Single().State.Should().Be(ComplianceItemState.Unverified);
    }

    [Fact]
    public void Expiring_within_reminder_window_is_a_warning_not_a_block()
    {
        var docs = new[] { Doc(20, Insurance, Today.AddDays(10), DocumentVerificationStatus.Verified, vehicleId: 7) };
        var vehicles = new[] { new TransporterVehicle { Id = 7, TransporterId = 1, RegistrationNumber = "MH12AB1234", Status = RecordStatus.Active } };

        var report = ComplianceEvaluator.Evaluate(1, Today, 30, [Insurance], docs, vehicles);

        var item = report.Items.Single(i => i.DocumentTypeCode == "INSURANCE");
        item.State.Should().Be(ComplianceItemState.ExpiringSoon);
        item.DaysToExpiry.Should().Be(10);
        item.BlocksAllocation.Should().BeFalse();
        report.Overall.Should().Be(ComplianceOverallStatus.ExpiringSoon);
    }

    [Fact]
    public void Expired_vehicle_insurance_blocks_only_that_vehicle()
    {
        var docs = new[] { Doc(21, Insurance, Today.AddDays(-1), DocumentVerificationStatus.Verified, vehicleId: 7) };
        var vehicles = new[]
        {
            new TransporterVehicle { Id = 7, TransporterId = 1, RegistrationNumber = "MH12AB1234", Status = RecordStatus.Active },
            new TransporterVehicle { Id = 8, TransporterId = 1, RegistrationNumber = "MH12CD5678", Status = RecordStatus.Active }
        };

        var report = ComplianceEvaluator.Evaluate(1, Today, 30, [Insurance], docs, vehicles);

        report.ApprovalBlocked.Should().BeFalse("vehicle documents do not block transporter approval");
        // Vehicle 8 has no insurance at all, which is mandatory for vehicles, so it is blocked too.
        report.BlockedVehicleIds.Should().BeEquivalentTo(new long[] { 7, 8 });
        report.Overall.Should().Be(ComplianceOverallStatus.NonCompliant);
    }

    [Fact]
    public void Rejected_latest_document_overrides_an_older_valid_one()
    {
        var docs = new[]
        {
            Doc(10, Gst, null, DocumentVerificationStatus.Verified),
            Doc(11, Gst, null, DocumentVerificationStatus.Rejected)
        };

        var report = ComplianceEvaluator.Evaluate(1, Today, 30, [Gst], docs, []);

        report.Items.Single().State.Should().Be(ComplianceItemState.Rejected);
        report.ApprovalBlocked.Should().BeTrue();
    }
}
