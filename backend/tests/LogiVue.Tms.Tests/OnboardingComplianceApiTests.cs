using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Alerts;
using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Application.Documents;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Milestone 3: onboarding workflow, document verification, expiry, compliance blocks and alerts.</summary>
public class OnboardingComplianceApiTests : IClassFixture<TestHost>
{
    private const string Transporters = "/api/v1/transporters";
    private const string AllRoles = "Transport Admin,Transport Manager,Transport Executive,Compliance User,Finance User,Operations User";
    private const string ValidGstin = "27AAPFU0939F1ZV";
    private const string ValidPan = "AAPFU0939F";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly TestHost _host;
    private readonly HttpClient _admin;
    private static Dictionary<string, long>? _documentTypeIds;

    public OnboardingComplianceApiTests(TestHost host)
    {
        _host = host;
        _host.EnsureTransporterSchemaAsync().GetAwaiter().GetResult();
        _admin = Client("admin", AllRoles);
    }

    // ---------- helpers ----------

    private HttpClient Client(string user, string roles)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, user);
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, roles);
        return client;
    }

    private async Task<long> DocTypeAsync(string code)
    {
        _documentTypeIds ??= (await _admin.GetFromJsonAsync<List<LookupDto>>(
                "/api/v1/transporter-management/lookups/document-types", TestHost.Json))!
            .ToDictionary(l => l.Code, l => l.Id);
        return _documentTypeIds[code];
    }

    private static string UniqueCode() => $"O{Guid.NewGuid():N}"[..10].ToUpperInvariant();

    /// <summary>A well-formed GSTIN that is unique per call, since GSTINs must not repeat across transporters.</summary>
    private static string UniqueGstin()
    {
        var random = Random.Shared;
        var letters = new string(Enumerable.Range(0, 5).Select(_ => (char)random.Next('A', 'Z' + 1)).ToArray());
        var digits = random.Next(1000, 10000);
        var entity = (char)random.Next('A', 'Z' + 1);
        return $"27{letters}{digits:0000}{entity}1Z{(char)random.Next('A', 'Z' + 1)}";
    }

    private async Task<TransporterDetail> CreateTransporterAsync(bool withGstin = true)
    {
        var request = new CreateTransporterRequest(UniqueCode(), "Onboard Logistics Pvt Ltd", null, 1, "Private Limited",
            ValidPan, withGstin ? UniqueGstin() : null, null, "Plot 1", "Pune", "Maharashtra", "India",
            "Asha Rao", "ops@onboard.example", "+91 98200 44444", null);

        var response = await _admin.PostAsJsonAsync(Transporters, request, TestHost.Json);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<TransporterDetail>(TestHost.Json))!;

        // Submission requires at least one active contact, so every fixture starts with one.
        var contact = await _admin.PostAsJsonAsync($"{Transporters}/{created.Id}/contacts",
            new SaveContactRequest("Asha Rao", "Operations Head", "ops@onboard.example", "+91 98200 44444", "Operations", IsPrimary: true), TestHost.Json);
        contact.StatusCode.Should().Be(HttpStatusCode.Created);

        return created;
    }

    private async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, long transporterId, long typeId,
        DateOnly? expiry = null, long? vehicleId = null, string fileName = "doc.pdf", string contentType = "application/pdf",
        byte[]? bytes = null, string? number = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(typeId.ToString()), "documentTypeId");
        if (number is not null) form.Add(new StringContent(number), "documentNumber");
        if (expiry is { } e) form.Add(new StringContent(e.ToString("yyyy-MM-dd")), "expiryDate");
        if (vehicleId is { } v) form.Add(new StringContent(v.ToString()), "vehicleId");

        var file = new ByteArrayContent(bytes ?? Encoding.UTF8.GetBytes("%PDF-1.4 test document"));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        return await client.PostAsync($"{Transporters}/{transporterId}/documents", form);
    }

    private async Task<DocumentDto> UploadVerifiedAsync(long transporterId, string typeCode, DateOnly? expiry = null, long? vehicleId = null)
    {
        var response = await UploadAsync(_admin, transporterId, await DocTypeAsync(typeCode), expiry, vehicleId);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = (await response.Content.ReadFromJsonAsync<DocumentDto>(TestHost.Json))!;

        var verify = await Client("compliance", "Compliance User")
            .PostAsJsonAsync($"{Transporters}/{transporterId}/documents/{doc.Id}/verify", new VerifyDocumentRequest(null), TestHost.Json);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await verify.Content.ReadFromJsonAsync<DocumentDto>(TestHost.Json))!;
    }

    private async Task UploadAllMandatoryTransporterDocsAsync(long transporterId)
    {
        await UploadVerifiedAsync(transporterId, "GST_CERT");
        await UploadVerifiedAsync(transporterId, "PAN");
        await UploadVerifiedAsync(transporterId, "COMPANY_REG");
    }

    /// <summary>Drives a transporter from Draft to Active through every configured stage.</summary>
    private async Task<long> DriveToActiveAsync()
    {
        var transporter = await CreateTransporterAsync();
        await UploadAllMandatoryTransporterDocsAsync(transporter.Id);

        var path = $"{Transporters}/{transporter.Id}";
        await Client("exec", "Transport Executive").PostAsJsonAsync($"{path}/submit", new CommentsRequest("Complete pack"), TestHost.Json);
        await Client("mgr", "Transport Manager").PostAsJsonAsync($"{path}/approve", new CommentsRequest(null), TestHost.Json);
        await Client("comp", "Compliance User").PostAsJsonAsync($"{path}/approve", new CommentsRequest(null), TestHost.Json);
        await Client("ops", "Operations User").PostAsJsonAsync($"{path}/approve", new CommentsRequest(null), TestHost.Json);
        await Client("mgr", "Transport Manager").PostAsJsonAsync($"{path}/approve", new CommentsRequest(null), TestHost.Json);
        await Client("fin", "Finance User").PostAsJsonAsync($"{path}/approve", new CommentsRequest(null), TestHost.Json);
        var activated = await Client("mgr", "Transport Manager").PostAsJsonAsync($"{path}/activate", new CommentsRequest(null), TestHost.Json);
        activated.StatusCode.Should().Be(HttpStatusCode.OK);
        return transporter.Id;
    }

    private async Task<ApiError> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiError>(TestHost.Json))!;

    private async Task<PagedResult<AlertDto>> AlertsFor(long transporterId, string? alertType = null)
    {
        var url = $"/api/v1/transporter-management/alerts?transporterId={transporterId}" + (alertType is null ? string.Empty : $"&alertType={alertType}");
        return (await _admin.GetFromJsonAsync<PagedResult<AlertDto>>(url, TestHost.Json))!;
    }

    // ---------- submission ----------

    [Fact]
    public async Task Submit_rejects_an_incomplete_transporter_with_field_details()
    {
        var transporter = await CreateTransporterAsync(withGstin: false);

        var response = await Client("exec", "Transport Executive").PostAsJsonAsync($"{Transporters}/{transporter.Id}/submit", new CommentsRequest(null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await ErrorOf(response);
        error.Code.Should().Be("TRANSPORTER_INCOMPLETE");
        error.Details.Should().Contain(d => d.Field == nameof(Transporter.Gstin));
    }

    [Fact]
    public async Task Submit_requires_a_transport_executive_role()
    {
        var transporter = await CreateTransporterAsync();

        var response = await Client("ops", "Operations User").PostAsJsonAsync($"{Transporters}/{transporter.Id}/submit", new CommentsRequest(null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorOf(response)).Code.Should().Be("WORKFLOW_ROLE_REQUIRED");
    }

    // ---------- approval workflow ----------

    [Fact]
    public async Task Full_onboarding_runs_every_configured_stage_to_active_and_records_each_action()
    {
        var transporterId = await DriveToActiveAsync();

        var history = await _admin.GetFromJsonAsync<List<ApprovalActionDto>>($"{Transporters}/{transporterId}/approval-history", TestHost.Json);
        history!.Select(h => h.Action).Should().Equal(
            ApprovalActionType.Submit, ApprovalActionType.Advance, ApprovalActionType.Advance, ApprovalActionType.Advance,
            ApprovalActionType.Advance, ApprovalActionType.Advance, ApprovalActionType.Activate);
        history.Should().OnlyContain(h => !string.IsNullOrEmpty(h.ActorUserId));

        var detail = await _admin.GetFromJsonAsync<TransporterDetail>($"{Transporters}/{transporterId}", TestHost.Json);
        detail!.Status.Should().Be(TransporterStatus.Active);
    }

    [Fact]
    public async Task Approval_is_blocked_while_mandatory_documents_are_missing()
    {
        var transporter = await CreateTransporterAsync();
        await Client("exec", "Transport Executive").PostAsJsonAsync($"{Transporters}/{transporter.Id}/submit", new CommentsRequest(null), TestHost.Json);
        await Client("mgr", "Transport Manager").PostAsJsonAsync($"{Transporters}/{transporter.Id}/approve", new CommentsRequest(null), TestHost.Json);

        var response = await Client("comp", "Compliance User").PostAsJsonAsync($"{Transporters}/{transporter.Id}/approve", new CommentsRequest(null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await ErrorOf(response);
        error.Code.Should().Be("APPROVAL_BLOCKED_BY_COMPLIANCE");
        error.Details.Select(d => d.Field).Should().Contain(new[] { "GST_CERT", "PAN", "COMPANY_REG" });
    }

    [Fact]
    public async Task A_stage_can_only_be_approved_by_its_configured_role()
    {
        var transporter = await CreateTransporterAsync();
        var path = $"{Transporters}/{transporter.Id}";
        await Client("exec", "Transport Executive").PostAsJsonAsync($"{path}/submit", new CommentsRequest(null), TestHost.Json);
        await Client("mgr", "Transport Manager").PostAsJsonAsync($"{path}/approve", new CommentsRequest(null), TestHost.Json);

        // The current stage is Document Verification, which requires the Compliance User role.
        var response = await Client("fin", "Finance User").PostAsJsonAsync($"{path}/approve", new CommentsRequest(null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rejected_transporter_can_be_resubmitted()
    {
        var transporter = await CreateTransporterAsync();
        var path = $"{Transporters}/{transporter.Id}";
        await Client("exec", "Transport Executive").PostAsJsonAsync($"{path}/submit", new CommentsRequest(null), TestHost.Json);

        var rejected = await Client("mgr", "Transport Manager").PostAsJsonAsync($"{path}/reject", new ReasonRequest("Incomplete KYC"), TestHost.Json);
        var resubmitted = await Client("exec", "Transport Executive").PostAsJsonAsync($"{path}/submit", new CommentsRequest("Fixed"), TestHost.Json);

        rejected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rejected.Content.ReadFromJsonAsync<TransporterDetail>(TestHost.Json))!.Status.Should().Be(TransporterStatus.Rejected);
        resubmitted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resubmitted.Content.ReadFromJsonAsync<TransporterDetail>(TestHost.Json))!.Status.Should().Be(TransporterStatus.Submitted);
    }

    [Fact]
    public async Task Reject_requires_a_reason()
    {
        var transporter = await CreateTransporterAsync();
        await Client("exec", "Transport Executive").PostAsJsonAsync($"{Transporters}/{transporter.Id}/submit", new CommentsRequest(null), TestHost.Json);

        var response = await Client("mgr", "Transport Manager").PostAsJsonAsync($"{Transporters}/{transporter.Id}/reject", new ReasonRequest(" "), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorOf(response)).Details.Should().Contain(d => d.Field == nameof(ReasonRequest.Reason));
    }

    [Fact]
    public async Task Suspend_then_reactivate_then_blacklist_locks_the_transporter()
    {
        var transporterId = await DriveToActiveAsync();
        var path = $"{Transporters}/{transporterId}";
        var manager = Client("mgr", "Transport Manager");

        var suspended = await manager.PostAsJsonAsync($"{path}/suspend", new ReasonRequest("Repeated delays"), TestHost.Json);
        var reactivated = await manager.PostAsJsonAsync($"{path}/activate", new CommentsRequest("Cleared"), TestHost.Json);
        var blacklisted = await manager.PostAsJsonAsync($"{path}/blacklist", new ReasonRequest("Fraud"), TestHost.Json);
        var afterBlacklist = await manager.PostAsJsonAsync($"{path}/activate", new CommentsRequest(null), TestHost.Json);

        suspended.StatusCode.Should().Be(HttpStatusCode.OK);
        reactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        blacklisted.StatusCode.Should().Be(HttpStatusCode.OK);
        afterBlacklist.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(afterBlacklist)).Code.Should().Be("TRANSPORTER_LOCKED");
    }

    [Fact]
    public async Task Activation_is_an_illegal_transition_from_draft()
    {
        var transporter = await CreateTransporterAsync();

        var response = await Client("mgr", "Transport Manager").PostAsJsonAsync($"{Transporters}/{transporter.Id}/activate", new CommentsRequest(null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(response)).Code.Should().Be("ILLEGAL_TRANSITION");
    }

    [Fact]
    public async Task Stage_configuration_is_served_in_order()
    {
        var steps = await _admin.GetFromJsonAsync<List<OnboardingStepDto>>("/api/v1/transporter-management/lookups/onboarding-steps", TestHost.Json);

        steps!.Select(s => s.Name).Should().Equal("Document Verification", "Operations Review", "Commercial Review", "Finance Review");
    }

    // ---------- documents ----------

    [Fact]
    public async Task Upload_stores_metadata_and_returns_the_file_for_download()
    {
        var transporter = await CreateTransporterAsync();
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 GST certificate contents");

        var response = await UploadAsync(_admin, transporter.Id, await DocTypeAsync("GST_CERT"), bytes: bytes, number: "GST-2026-001");
        var doc = (await response.Content.ReadFromJsonAsync<DocumentDto>(TestHost.Json))!;
        var download = await _admin.GetAsync($"{Transporters}/{transporter.Id}/documents/{doc.Id}/file");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        doc.VerificationStatus.Should().Be(DocumentVerificationStatus.Pending);
        doc.OriginalFileName.Should().Be("doc.pdf");
        doc.FileSizeBytes.Should().Be(bytes.Length);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    [Fact]
    public async Task Upload_rejects_disallowed_file_types()
    {
        var transporter = await CreateTransporterAsync();

        var response = await UploadAsync(_admin, transporter.Id, await DocTypeAsync("GST_CERT"),
            fileName: "payload.exe", contentType: "application/octet-stream");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorOf(response)).Details.Should().Contain(d => d.Field == "OriginalFileName");
    }

    [Fact]
    public async Task Expiry_is_required_for_types_that_need_it()
    {
        var transporter = await CreateTransporterAsync();

        var response = await UploadAsync(_admin, transporter.Id, await DocTypeAsync("INSURANCE"), vehicleId: null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_identity_fields_after_verification_sends_the_document_back_for_verification()
    {
        var transporter = await CreateTransporterAsync();
        var verified = await UploadVerifiedAsync(transporter.Id, "PAN");

        var updated = await _admin.PutAsJsonAsync($"{Transporters}/{transporter.Id}/documents/{verified.Id}",
            new SaveDocumentRequest(verified.DocumentTypeId, "AAPFU0939X", null, null, null, "Corrected number"), TestHost.Json);

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = (await updated.Content.ReadFromJsonAsync<DocumentDto>(TestHost.Json))!;
        doc.VerificationStatus.Should().Be(DocumentVerificationStatus.Pending);
        doc.VerifiedBy.Should().BeNull();
    }

    [Fact]
    public async Task Reject_document_requires_reason_and_marks_it_rejected()
    {
        var transporter = await CreateTransporterAsync();
        var uploaded = await UploadAsync(_admin, transporter.Id, await DocTypeAsync("PAN"));
        var doc = (await uploaded.Content.ReadFromJsonAsync<DocumentDto>(TestHost.Json))!;
        var compliance = Client("comp", "Compliance User");
        var path = $"{Transporters}/{transporter.Id}/documents/{doc.Id}";

        var empty = await compliance.PostAsJsonAsync($"{path}/reject", new ReasonRequest(""), TestHost.Json);
        var rejected = await compliance.PostAsJsonAsync($"{path}/reject", new ReasonRequest("Name does not match PAN card"), TestHost.Json);

        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        rejected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rejected.Content.ReadFromJsonAsync<DocumentDto>(TestHost.Json))!.VerificationStatus.Should().Be(DocumentVerificationStatus.Rejected);
    }

    [Fact]
    public async Task Verification_requires_a_compliance_role()
    {
        var transporter = await CreateTransporterAsync();
        var uploaded = await UploadAsync(_admin, transporter.Id, await DocTypeAsync("PAN"));
        var doc = (await uploaded.Content.ReadFromJsonAsync<DocumentDto>(TestHost.Json))!;

        var response = await Client("ops", "Operations User")
            .PostAsJsonAsync($"{Transporters}/{transporter.Id}/documents/{doc.Id}/verify", new VerifyDocumentRequest(null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Expired_mandatory_document_blocks_approval()
    {
        var transporter = await CreateTransporterAsync();
        await UploadVerifiedAsync(transporter.Id, "PAN");
        await UploadVerifiedAsync(transporter.Id, "COMPANY_REG");
        await UploadVerifiedAsync(transporter.Id, "GST_CERT", expiry: Today.AddDays(-3));
        var path = $"{Transporters}/{transporter.Id}";
        await Client("exec", "Transport Executive").PostAsJsonAsync($"{path}/submit", new CommentsRequest(null), TestHost.Json);
        await Client("mgr", "Transport Manager").PostAsJsonAsync($"{path}/approve", new CommentsRequest(null), TestHost.Json);

        var response = await Client("comp", "Compliance User").PostAsJsonAsync($"{path}/approve", new CommentsRequest(null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(response)).Details.Should().Contain(d => d.Field == "GST_CERT" && d.Reason.Contains("expired"));
    }

    // ---------- compliance & alerts ----------

    [Fact]
    public async Task Compliance_report_flags_expired_vehicle_insurance_as_blocking_that_vehicle()
    {
        var transporter = await CreateTransporterAsync();
        var vehicle = await _admin.PostAsJsonAsync($"{Transporters}/{transporter.Id}/vehicles",
            new SaveVehicleRequest("MH14 QZ0001", 1, 9000, null, null, null, null, VehicleOwnershipType.Owned, VehicleAvailabilityStatus.Available, null), TestHost.Json);
        var vehicleId = (await vehicle.Content.ReadFromJsonAsync<VehicleDto>(TestHost.Json))!.Id;
        await UploadVerifiedAsync(transporter.Id, "INSURANCE", expiry: Today.AddDays(-2), vehicleId: vehicleId);

        var report = await _admin.GetFromJsonAsync<ComplianceReportDto>($"{Transporters}/{transporter.Id}/compliance", TestHost.Json);

        report!.BlockedVehicleIds.Should().Contain(vehicleId);
        report.Items.Should().Contain(i => i.DocumentTypeCode == "INSURANCE" && i.State == ComplianceItemState.Expired);
    }

    [Fact]
    public async Task Evaluation_raises_expiry_alerts_and_resolves_them_when_renewed()
    {
        var transporter = await CreateTransporterAsync();
        var vehicle = await _admin.PostAsJsonAsync($"{Transporters}/{transporter.Id}/vehicles",
            new SaveVehicleRequest("KA05 MN7777", 1, 9000, null, null, null, null, VehicleOwnershipType.Owned, VehicleAvailabilityStatus.Available, null), TestHost.Json);
        var vehicleId = (await vehicle.Content.ReadFromJsonAsync<VehicleDto>(TestHost.Json))!.Id;
        var insurance = await UploadVerifiedAsync(transporter.Id, "INSURANCE", expiry: Today.AddDays(10), vehicleId: vehicleId);

        var evaluate = await _admin.PostAsync($"/api/v1/transporter-management/compliance/evaluate?transporterId={transporter.Id}", null);
        var raised = await AlertsFor(transporter.Id, "COMPLIANCE_EXPIRING");

        evaluate.StatusCode.Should().Be(HttpStatusCode.OK);
        raised.Items.Should().ContainSingle(a => a.EntityId == insurance.Id.ToString() && a.Status == AlertStatus.Open);

        // Renew the policy: the expiry changes, so verification restarts and the old expiry alert no longer applies.
        await _admin.PutAsJsonAsync($"{Transporters}/{transporter.Id}/documents/{insurance.Id}",
            new SaveDocumentRequest(insurance.DocumentTypeId, "POL-NEW", Today, Today.AddYears(1), vehicleId, null), TestHost.Json);
        await _admin.PostAsync($"/api/v1/transporter-management/compliance/evaluate?transporterId={transporter.Id}", null);

        var after = await AlertsFor(transporter.Id, "COMPLIANCE_EXPIRING");
        after.Items.Should().Contain(a => a.EntityId == insurance.Id.ToString() && a.Status == AlertStatus.Resolved);
    }

    [Fact]
    public async Task Expired_vehicle_insurance_raises_a_critical_expired_alert()
    {
        var transporter = await CreateTransporterAsync();
        var vehicle = await _admin.PostAsJsonAsync($"{Transporters}/{transporter.Id}/vehicles",
            new SaveVehicleRequest("TN09 AB1212", 1, 9000, null, null, null, null, VehicleOwnershipType.Owned, VehicleAvailabilityStatus.Available, null), TestHost.Json);
        var vehicleId = (await vehicle.Content.ReadFromJsonAsync<VehicleDto>(TestHost.Json))!.Id;
        var insurance = await UploadVerifiedAsync(transporter.Id, "INSURANCE", expiry: Today.AddDays(-1), vehicleId: vehicleId);

        await _admin.PostAsync($"/api/v1/transporter-management/compliance/evaluate?transporterId={transporter.Id}", null);
        var alerts = await AlertsFor(transporter.Id, "COMPLIANCE_EXPIRED");

        alerts.Items.Should().ContainSingle(a => a.EntityId == insurance.Id.ToString())
            .Which.Severity.Should().Be(Severity.Critical);
    }

    [Fact]
    public async Task Alert_moves_from_open_to_acknowledged_to_resolved_and_cannot_be_resolved_twice()
    {
        var transporter = await CreateTransporterAsync();
        var vehicle = await _admin.PostAsJsonAsync($"{Transporters}/{transporter.Id}/vehicles",
            new SaveVehicleRequest("RJ14 PQ5555", 1, 9000, null, null, null, null, VehicleOwnershipType.Owned, VehicleAvailabilityStatus.Available, null), TestHost.Json);
        var vehicleId = (await vehicle.Content.ReadFromJsonAsync<VehicleDto>(TestHost.Json))!.Id;
        await UploadVerifiedAsync(transporter.Id, "INSURANCE", expiry: Today.AddDays(5), vehicleId: vehicleId);
        await _admin.PostAsync($"/api/v1/transporter-management/compliance/evaluate?transporterId={transporter.Id}", null);
        var alert = (await AlertsFor(transporter.Id, "COMPLIANCE_EXPIRING")).Items.Single();

        var acknowledged = await _admin.PostAsync($"/api/v1/transporter-management/alerts/{alert.Id}/acknowledge", null);
        var resolved = await _admin.PostAsJsonAsync($"/api/v1/transporter-management/alerts/{alert.Id}/resolve", new CommentsRequest("Renewal in progress"), TestHost.Json);
        var again = await _admin.PostAsJsonAsync($"/api/v1/transporter-management/alerts/{alert.Id}/resolve", new CommentsRequest(null), TestHost.Json);

        (await acknowledged.Content.ReadFromJsonAsync<AlertDto>(TestHost.Json))!.Status.Should().Be(AlertStatus.Acknowledged);
        (await resolved.Content.ReadFromJsonAsync<AlertDto>(TestHost.Json))!.Status.Should().Be(AlertStatus.Resolved);
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
