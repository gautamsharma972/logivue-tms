using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Application.MasterData;
using Tms.Modules.Transporters.Application.Selection;
using Tms.Modules.Transporters.Domain;
using static Tms.IntegrationTests.Infrastructure.TransporterTestData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransporterMasterDataApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task A_transporter_type_must_come_from_the_active_list_and_a_tenant_can_add_its_own()
    {
        var admin = await factory.AdminAsync();
        var types = await (await admin.GetAsync("/api/v1/transporters/types")).ReadAsync<List<MasterEntryDto>>();
        types.ShouldContain(t => t.Code == "3PL" && t.IsBuiltIn);

        var code = $"CRANE_{Guid.NewGuid().ToString("N")[..5]}".ToUpperInvariant();
        var bad = NewRequest() with { TypeCode = code };
        var refused = await admin.PostJsonAsync("/api/v1/transporters", bad);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await admin.PutJsonAsync("/api/v1/transporters/types", new SaveMasterItemRequest(code, "Crane operator"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await admin.PostJsonAsync("/api/v1/transporters", NewRequest() with { TypeCode = code });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await created.ReadAsync<TransporterDto>()).TypeCode.ShouldBe(code);

        // Switching the type off keeps existing transporters as they are but stops new ones using it.
        await admin.PutJsonAsync("/api/v1/transporters/types", new SaveMasterItemRequest(code, "Crane operator", false));
        (await admin.PostJsonAsync("/api/v1/transporters", NewRequest() with { TypeCode = code })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutJsonAsync("/api/v1/transporters/types", new SaveMasterItemRequest("not valid!", "x"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_custom_capability_can_be_held_by_a_transporter_and_asked_for_in_selection()
    {
        var admin = await factory.AdminAsync();
        var code = $"COLD_{Guid.NewGuid().ToString("N")[..5]}".ToUpperInvariant();
        var transporter = await CreateAsync(admin);

        (await admin.PostJsonAsync($"/api/v1/transporters/{transporter.Id}/capabilities", new AddCapabilityRequest(code, DateOnly.FromDateTime(DateTime.UtcNow), null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await admin.PutJsonAsync("/api/v1/transporters/capability-types", new SaveMasterItemRequest(code, "Cold chain"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var added = await admin.PostJsonAsync($"/api/v1/transporters/{transporter.Id}/capabilities", new AddCapabilityRequest(code, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), null));
        added.StatusCode.ShouldBe(HttpStatusCode.Created, await added.Content.ReadAsStringAsync());
        (await added.ReadAsync<CapabilityDto>()).Name.ShouldBe("Cold chain");

        var catalog = await (await admin.GetAsync("/api/v1/transporters/capability-catalog")).ReadAsync<List<CapabilityTypeDto>>();
        catalog.ShouldContain(c => c.Code == code);
        catalog.ShouldContain(c => c.Code == "HAZARDOUS");
    }

    [Fact]
    public async Task Document_rules_change_what_is_required_and_when_a_paper_is_flagged()
    {
        var admin = await factory.AdminAsync();
        var rules = await (await admin.GetAsync("/api/v1/transporters/document-rules")).ReadAsync<List<DocumentRuleDto>>();
        rules.Count.ShouldBe(11);
        rules.Single(r => r.Kind == DocumentKind.Insurance).BlockWhenExpired.ShouldBeTrue();

        var transporter = await CreateAsync(admin);
        try
        {
            // The MSME certificate becomes mandatory: a transporter without one is not ready to submit.
            (await admin.PutJsonAsync("/api/v1/transporters/document-rules/MsmeCertificate", new SaveDocumentRuleRequest(true, false, 30, true, true))).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await GetAsync(admin, transporter.Id)).MissingForSubmission.ShouldContain("MSME certificate");

            // A longer reminder flags a licence expiring in 45 days.
            await UploadOkAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.TransportLicense, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45));
            async Task<ExpiryStatus> LicenceStatusAsync() =>
                (await (await admin.GetAsync($"/api/v1/transporters/{transporter.Id}/documents")).ReadAsync<List<DocumentDto>>()).Single(d => d.Kind == DocumentKind.TransportLicense).ExpiryStatus;
            (await LicenceStatusAsync()).ShouldBe(ExpiryStatus.Valid);
            await admin.PutJsonAsync("/api/v1/transporters/document-rules/TransportLicense", new SaveDocumentRuleRequest(false, false, 60, true, true));
            (await LicenceStatusAsync()).ShouldBe(ExpiryStatus.ExpiringSoon);

            (await admin.PutJsonAsync("/api/v1/transporters/document-rules/Puc", new SaveDocumentRuleRequest(true, true, 500, true, true))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await admin.PutJsonAsync("/api/v1/transporters/document-rules/Puc", new SaveDocumentRuleRequest(true, true, 30, true, false))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        finally
        {
            await admin.PutJsonAsync("/api/v1/transporters/document-rules/MsmeCertificate", new SaveDocumentRuleRequest(false, false, 30, true, true));
            await admin.PutJsonAsync("/api/v1/transporters/document-rules/TransportLicense", new SaveDocumentRuleRequest(false, false, 30, true, true));
        }
    }

    [Fact]
    public async Task Only_a_manager_can_change_master_data_but_a_vendor_cannot_even_try()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        (await s.Vendor.PutJsonAsync("/api/v1/transporters/types", new SaveMasterItemRequest("ROGUE", "Rogue"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.PutJsonAsync("/api/v1/transporters/document-rules/Insurance", new SaveDocumentRuleRequest(false, false, 0, false, false))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
