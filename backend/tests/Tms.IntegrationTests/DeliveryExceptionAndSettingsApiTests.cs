using System.Net;
using System.Text.Json;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Application.Settings;
using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.DeliveryScenario;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DeliveryExceptionAndSettingsApiTests(TmsApiFactory factory)
{
    private static async Task<ExceptionDto> RaiseAsync(DeliveryScenario s, Guid deliveryId, ExceptionType type = ExceptionType.AddressIssue)
    {
        var response = await s.Admin.PostJsonAsync("/api/v1/delivery-exceptions", new RaiseExceptionRequest(deliveryId, type, "The customer rang to say the gate is locked after 6pm", null));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ExceptionDto>();
    }

    [Fact]
    public async Task An_exception_is_assigned_investigated_escalated_resolved_with_a_finding_and_closed()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync();
        var exception = await RaiseAsync(s, delivery.Summary.Id);
        exception.Summary.Status.ShouldBe(ExceptionStatus.Open);
        exception.Summary.Severity.ShouldBe(ExceptionSeverity.Medium); // from the tenant's rules
        exception.ResponsibleParty.ShouldBe(ResponsibleParty.Unknown);
        var id = exception.Summary.Id;

        (await s.Admin.PostJsonAsync("/api/v1/delivery-exceptions", new RaiseExceptionRequest(delivery.Summary.Id, ExceptionType.AddressIssue, "again", null))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // one open per type

        (await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{id}/assign", new AssignExceptionRequest(null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var owner = Guid.NewGuid();
        var assigned = await (await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{id}/assign", new AssignExceptionRequest(owner, "Customer service", DateTimeOffset.UtcNow.AddDays(2), ExceptionSeverity.High))).ReadAsync<ExceptionDto>();
        (assigned.Summary.OwnerUserId, assigned.Summary.Department, assigned.Summary.Severity, assigned.Summary.Status).ShouldBe((owner, "Customer service", ExceptionSeverity.High, ExceptionStatus.Acknowledged));

        var investigating = await (await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{id}/investigate", new InvestigateExceptionRequest("Gate closes at 6pm", ResponsibleParty.Customer))).ReadAsync<ExceptionDto>();
        investigating.Summary.Status.ShouldBe(ExceptionStatus.UnderInvestigation);

        (await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{id}/escalate", new ReasonRequest(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var escalated = await (await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{id}/escalate", new ReasonRequest("Customer is a key account"))).ReadAsync<ExceptionDto>();
        (escalated.Summary.Status, escalated.Summary.Severity).ShouldBe((ExceptionStatus.Escalated, ExceptionSeverity.Critical));

        (await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{id}/notes", new NoteRequest("Called the customer"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Admin.PostAsync($"/api/v1/delivery-exceptions/{id}/close", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // resolve first
        (await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{id}/resolve", new ResolveExceptionRequest("", null, ResponsibleParty.Customer, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var resolved = await (await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{id}/resolve", new ResolveExceptionRequest(
            "Re-delivered before 6pm", "The gate is locked after 6pm", ResponsibleParty.Customer, "Rescheduled", 0m, null))).ReadAsync<ExceptionDto>();
        (resolved.Summary.Status, resolved.ResponsibleParty, resolved.ResolvedAt is not null).ShouldBe((ExceptionStatus.Resolved, ResponsibleParty.Customer, true));
        (await (await s.Admin.PostAsync($"/api/v1/delivery-exceptions/{id}/close", null)).ReadAsync<ExceptionDto>()).Summary.Status.ShouldBe(ExceptionStatus.Closed);
        resolved.Notes.Select(n => n.Text).ShouldContain("Called the customer");

        var open = await (await s.Admin.GetAsync($"/api/v1/delivery-exceptions?deliveryId={delivery.Summary.Id}&openOnly=true")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        open.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_vendor_sees_its_own_exceptions_but_cannot_manage_them_and_never_sees_anothers()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var exception = await RaiseAsync(s, (await s.DeliveryAsync()).Summary.Id, ExceptionType.LateDelivery);

        var mine = await (await s.Vendor.GetAsync($"/api/v1/delivery-exceptions?pageSize=100")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        mine.Items.ShouldContain(e => e.Id == exception.Summary.Id);
        (await s.Rival.GetAsync($"/api/v1/delivery-exceptions/{exception.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var theirs = await (await s.Rival.GetAsync("/api/v1/delivery-exceptions?pageSize=100")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        theirs.Items.ShouldNotContain(e => e.Id == exception.Summary.Id);

        (await s.Vendor.PostAsync($"/api/v1/delivery-exceptions/{exception.Summary.Id}/acknowledge", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Vendor.PostJsonAsync("/api/v1/delivery-exceptions", new RaiseExceptionRequest(exception.Summary.DeliveryId, ExceptionType.Damage, "x", null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Settings_show_defaults_change_per_tenant_and_reject_nonsense()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var all = await (await s.Admin.GetAsync("/api/v1/delivery-settings")).ReadAsync<List<SettingDto>>();
        all.Select(x => x.Key).ShouldContain(DeliverySettingKeys.PodRules);
        all.Select(x => x.Key).ShouldContain(DeliverySettingKeys.Ocr);
        all.Single(x => x.Key == DeliverySettingKeys.Sla).Value.GetProperty("podSubmissionHours").GetInt32().ShouldBe(24);

        var original = DeliverySettingDefaults.For(DeliverySettingKeys.Sla)!;
        try
        {
            var changed = await s.Admin.PutJsonAsync($"/api/v1/delivery-settings/{DeliverySettingKeys.Sla}", new SlaSetting(12, 2, 6));
            changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
            (await changed.ReadAsync<SettingDto>()).IsCustomised.ShouldBeTrue();
            var again = await (await s.Admin.GetAsync("/api/v1/delivery-settings")).ReadAsync<List<SettingDto>>();
            again.Single(x => x.Key == DeliverySettingKeys.Sla).Value.GetProperty("podReviewHours").GetInt32().ShouldBe(2);

            (await s.Admin.PutJsonAsync($"/api/v1/delivery-settings/{DeliverySettingKeys.Sla}", new SlaSetting(0, 2, 6))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await s.Admin.PutJsonAsync($"/api/v1/delivery-settings/{DeliverySettingKeys.Ocr}", new OcrSetting(true, 1.5m, 0.8m, 0.7m, 0.6m, [], []))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await s.Admin.PutJsonAsync($"/api/v1/delivery-settings/{DeliverySettingKeys.DamageTypes}", Array.Empty<ReasonSetting>())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await s.Admin.PutJsonAsync("/api/v1/delivery-settings/nope", new { a = 1 })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await s.Vendor.PutJsonAsync($"/api/v1/delivery-settings/{DeliverySettingKeys.Sla}", new SlaSetting(1, 1, 1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await s.SetSettingAsync(DeliverySettingKeys.Sla, original);
        }
    }

    [Fact]
    public async Task Turning_auto_acceptance_off_sends_every_proof_to_a_reviewer()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var original = DeliverySettingDefaults.For(DeliverySettingKeys.AutoAccept)!;
        await s.SetSettingAsync(DeliverySettingKeys.AutoAccept, new AutoAcceptSetting(false, false, false));
        try
        {
            var done = await s.PhotoAndSubmitAsync(await s.CompletedAsync(await s.ArrivedAsync()));
            done.Summary.Status.ShouldBe(PodStatus.UnderReview);
        }
        finally
        {
            await s.SetSettingAsync(DeliverySettingKeys.AutoAccept, original);
        }
    }

    [Fact]
    public async Task A_tenant_can_add_its_own_reasons_and_the_server_then_accepts_them()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var original = DeliverySettingDefaults.For(DeliverySettingKeys.AttemptReasons)!;
        var reasons = ((List<ReasonSetting>)original).Append(new ReasonSetting("FESTIVAL", "Local festival")).ToList();
        await s.SetSettingAsync(DeliverySettingKeys.AttemptReasons, reasons);
        try
        {
            var arrived = await s.ArrivedAsync();
            var attempt = await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{arrived.Summary.Id}/attempt", new AttemptRequest("FESTIVAL", null, null, null, Here()));
            attempt.StatusCode.ShouldBe(HttpStatusCode.OK, await attempt.Content.ReadAsStringAsync());
        }
        finally
        {
            await s.SetSettingAsync(DeliverySettingKeys.AttemptReasons, original);
        }
    }

    [Fact]
    public async Task Staff_without_the_deliveries_permission_see_nothing()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        using var outsider = await factory.UserWithPermissionsAsync(s.Admin, "users.read");
        (await outsider.GetAsync("/api/v1/deliveries")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetAsync("/api/v1/pods")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetAsync("/api/v1/delivery-exceptions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var reader = await factory.UserWithPermissionsAsync(s.Admin, DeliveryPermissions.Read);
        (await reader.GetAsync("/api/v1/deliveries")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostJsonAsync("/api/v1/deliveries", s.Request())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsync("/api/v1/pods/review-queue", null)).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await reader.GetAsync("/api/v1/pods/review-queue")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
