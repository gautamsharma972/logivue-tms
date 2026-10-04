using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Application.Tendering;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TenderApiTests(TmsApiFactory factory)
{
    private sealed record Carrier(TransporterDto Transporter, ContractDto Contract, VehicleDto Vehicle, DriverDto Driver, HttpClient Vendor);

    /// <summary>A second transporter priced on the scenario's own lane, with its own vendor login and fleet.</summary>
    private async Task<Carrier> SecondCarrierAsync(ShipmentScenario s, decimal rate)
    {
        var transporter = await ContractApiData.ActiveTransporterAsync(s.Admin);
        var contract = await ContractApiData.ActiveContractAsync(
            s.Admin, transporter.Id, ContractType.Ftl, null,
            ContractApiData.Flat(ContractApiData.State(s.OriginState), ContractApiData.State(s.DropState), rate, s.VehicleTypeId));
        var (vehicle, driver) = await ShipmentScenario.RegisterFleetAsync(s.Admin, transporter.Id, s.VehicleTypeId);
        var role = await s.Admin.CreateExternalRoleAsync(ShipmentPermissions.Respond, Tms.Modules.Transporters.Domain.TransporterPermissions.PerformanceSelf);
        var email = ApiExtensions.UniqueEmail("vendor2");
        (await s.Admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(email, "Second Dispatcher", ApiExtensions.StrongPassword, UserType.Transporter, [role.Id], transporter.Id)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        return new Carrier(transporter, contract, vehicle, driver, await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword));
    }

    private static async Task<TenderDto> StartAsync(ShipmentScenario s, Guid shipmentId, TenderMode mode, params Guid[] contractIds)
    {
        var response = await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipmentId}/tenders", new StartTenderRequest(mode, contractIds, null, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<TenderDto>();
    }

    private static async Task<TenderDto> CurrentAsync(HttpClient client, Guid shipmentId) =>
        (await (await client.GetAsync($"/api/v1/shipments/{shipmentId}/tenders")).ReadAsync<List<TenderDto>>()).First();

    private static async Task<ShipmentDto> ShipmentOfAsync(HttpClient client, Guid id) =>
        await (await client.GetAsync($"/api/v1/shipments/{id}")).ReadAsync<ShipmentDto>();

    private static async Task ExpireAsync(Guid tenderId)
    {
        await using var connection = new MySqlConnection(TmsApiFactory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand("UPDATE shipments_tender_invitees SET deadline = @past WHERE tender_id = @id AND status = 'Sent'", connection);
        command.Parameters.AddWithValue("@past", DateTime.UtcNow.AddMinutes(-1));
        command.Parameters.AddWithValue("@id", tenderId);
        (await command.ExecuteNonQueryAsync()).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_sequential_tender_offers_the_load_to_one_transporter_and_passes_it_on_when_they_decline()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, flatRate: 30_000m);
        var second = await SecondCarrierAsync(s, 35_000m);
        var shipment = await s.ShipmentAsync(await s.OrderAsync());

        var tender = await StartAsync(s, shipment.Summary.Id, TenderMode.Sequential, s.Contract.Summary.Id, second.Contract.Summary.Id);
        tender.Status.ShouldBe(TenderStatus.Open);
        tender.Invitees.Select(i => i.Status).ShouldBe([InviteeStatus.Sent, InviteeStatus.Waiting]);
        (await ShipmentOfAsync(s.Admin, shipment.Summary.Id)).Summary.TransporterId.ShouldBe(s.Transporter.Id);

        // The second carrier cannot see the load yet.
        (await second.Vendor.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var declined = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/reject", new ReasonRequest("No vehicle free"));
        declined.StatusCode.ShouldBe(HttpStatusCode.OK, await declined.Content.ReadAsStringAsync());
        (await declined.ReadAsync<ShipmentDto>()).Summary.TransporterId.ShouldBe(second.Transporter.Id);

        var moved = await CurrentAsync(s.Admin, shipment.Summary.Id);
        moved.Invitees.Select(i => i.Status).ShouldBe([InviteeStatus.Rejected, InviteeStatus.Sent]);

        var accepted = await second.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/accept", new AcceptRequest(second.Vehicle.Id, second.Driver.Id));
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var done = await CurrentAsync(s.Admin, shipment.Summary.Id);
        done.Status.ShouldBe(TenderStatus.Awarded);
        done.AwardedTransporterId.ShouldBe(second.Transporter.Id);
    }

    [Fact]
    public async Task When_the_last_transporter_declines_the_tender_is_exhausted_and_the_shipment_is_a_draft_again()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, flatRate: 30_000m);
        var second = await SecondCarrierAsync(s, 35_000m);
        var shipment = await s.ShipmentAsync(await s.OrderAsync());
        await StartAsync(s, shipment.Summary.Id, TenderMode.Sequential, s.Contract.Summary.Id, second.Contract.Summary.Id);

        (await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/decline", new DeclineTenderRequest("Busy"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/decline", new DeclineTenderRequest("Busy too"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await CurrentAsync(s.Admin, shipment.Summary.Id)).Status.ShouldBe(TenderStatus.Exhausted);
        var after = await ShipmentOfAsync(s.Admin, shipment.Summary.Id);
        after.Summary.Status.ShouldBe(ShipmentStatus.Draft);
        after.Summary.TransporterId.ShouldBeNull();
    }

    [Fact]
    public async Task A_missed_deadline_expires_the_invitation_and_moves_a_sequential_tender_on()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, flatRate: 30_000m);
        var second = await SecondCarrierAsync(s, 35_000m);
        var shipment = await s.ShipmentAsync(await s.OrderAsync());
        var tender = await StartAsync(s, shipment.Summary.Id, TenderMode.Sequential, s.Contract.Summary.Id, second.Contract.Summary.Id);

        await ExpireAsync(tender.Id);

        // Reading anything settles the deadline: the first carrier is expired and the load is with the second.
        var after = await ShipmentOfAsync(s.Admin, shipment.Summary.Id);
        after.Summary.TransporterId.ShouldBe(second.Transporter.Id);
        var current = await CurrentAsync(s.Admin, shipment.Summary.Id);
        current.Invitees.Select(i => i.Status).ShouldBe([InviteeStatus.Expired, InviteeStatus.Sent]);

        // The first carrier can no longer answer.
        var late = await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/accept", new AcceptRequest(s.Vehicle.Id, s.Driver.Id));
        late.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_broadcast_tender_is_seen_by_every_invitee_and_a_planner_awards_one_bid()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, flatRate: 30_000m);
        var second = await SecondCarrierAsync(s, 35_000m);
        var outsider = await SecondCarrierAsync(s, 36_000m);
        var shipment = await s.ShipmentAsync(await s.OrderAsync());

        var tender = await StartAsync(s, shipment.Summary.Id, TenderMode.Broadcast, s.Contract.Summary.Id, second.Contract.Summary.Id);
        (await ShipmentOfAsync(s.Admin, shipment.Summary.Id)).Summary.Status.ShouldBe(ShipmentStatus.Bidding);
        tender.Invitees.ShouldAllBe(i => i.Status == InviteeStatus.Sent);

        (await s.Vendor.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Vendor.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await outsider.Vendor.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var listed = await (await second.Vendor.GetAsync("/api/v1/shipments?pageSize=100")).ReadAsync<PagedResult<ShipmentSummaryDto>>();
        listed.Items.ShouldContain(x => x.Id == shipment.Summary.Id);

        // A vendor sees only its own invitation, and never the contract price or who else was invited.
        var theirView = await CurrentAsync(second.Vendor, shipment.Summary.Id);
        theirView.Invitees.Count.ShouldBe(1);
        theirView.Invitees[0].QuotedTotal.ShouldBeNull();
        theirView.Invitees[0].ContractReference.ShouldBeNull();

        // A bid needs the bidder's own, usable vehicle and driver.
        (await second.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/bid", new BidRequest(s.Vehicle.Id, s.Driver.Id, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await second.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/bid", new BidRequest(second.Vehicle.Id, second.Driver.Id, null, "Can load tonight"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/bid", new BidRequest(s.Vehicle.Id, s.Driver.Id, null, null))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A vendor cannot award; a planner picks the dearer bid and the other offer is closed.
        var staffView = await CurrentAsync(s.Admin, shipment.Summary.Id);
        var winner = staffView.Invitees.Single(i => i.TransporterId == second.Transporter.Id);
        (await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/award", new AwardTenderRequest(winner.Id))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var awarded = await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/award", new AwardTenderRequest(winner.Id));
        awarded.StatusCode.ShouldBe(HttpStatusCode.OK, await awarded.Content.ReadAsStringAsync());

        var result = await CurrentAsync(s.Admin, shipment.Summary.Id);
        result.Status.ShouldBe(TenderStatus.Awarded);
        result.Invitees.Single(i => i.TransporterId == s.Transporter.Id).Status.ShouldBe(InviteeStatus.Superseded);
        var after = await ShipmentOfAsync(s.Admin, shipment.Summary.Id);
        after.Summary.Status.ShouldBe(ShipmentStatus.Accepted);
        after.Summary.TransporterId.ShouldBe(second.Transporter.Id);
        after.Summary.VehicleRegistration.ShouldBe(second.Vehicle.RegistrationNumber);
        after.Summary.FreightEstimate.ShouldBe(35_000m); // priced from the winner's own contract
        (await s.Vendor.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_counter_offer_the_planner_agrees_becomes_the_estimate_and_a_declined_one_ends_the_invitation()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, flatRate: 30_000m);
        var second = await SecondCarrierAsync(s, 35_000m);
        var shipment = await s.ShipmentAsync(await s.OrderAsync());
        await StartAsync(s, shipment.Summary.Id, TenderMode.Sequential, s.Contract.Summary.Id, second.Contract.Summary.Id);

        (await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/counter", new CounterOfferRequest(-5, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/counter", new CounterOfferRequest(33_000m, "Diesel is up"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var pending = (await CurrentAsync(s.Admin, shipment.Summary.Id)).Invitees[0];
        pending.CounterStatus.ShouldBe(CounterStatus.Pending);

        (await s.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/counter-decision", new CounterDecisionRequest(pending.Id, true, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/counter-decision", new CounterDecisionRequest(pending.Id, true, "OK for this once"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ShipmentOfAsync(s.Admin, shipment.Summary.Id)).Summary.FreightEstimate.ShouldBe(33_000m);

        // The same again with the second carrier being declined.
        var other = await s.ShipmentAsync(await s.OrderAsync());
        await StartAsync(s, other.Summary.Id, TenderMode.Sequential, s.Contract.Summary.Id, second.Contract.Summary.Id);
        await s.Vendor.PostJsonAsync($"/api/v1/shipments/{other.Summary.Id}/tenders/counter", new CounterOfferRequest(39_000m, null));
        var asked = (await CurrentAsync(s.Admin, other.Summary.Id)).Invitees[0];
        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{other.Summary.Id}/tenders/counter-decision", new CounterDecisionRequest(asked.Id, false, "Too high"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var moved = await CurrentAsync(s.Admin, other.Summary.Id);
        moved.Invitees.Select(i => i.Status).ShouldBe([InviteeStatus.Rejected, InviteeStatus.Sent]);
        moved.Invitees[0].CounterStatus.ShouldBe(CounterStatus.Declined);
    }

    [Fact]
    public async Task Cancelling_a_tender_closes_every_offer_and_frees_the_shipment()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, flatRate: 30_000m);
        var second = await SecondCarrierAsync(s, 35_000m);
        var shipment = await s.ShipmentAsync(await s.OrderAsync());
        await StartAsync(s, shipment.Summary.Id, TenderMode.Broadcast, s.Contract.Summary.Id, second.Contract.Summary.Id);

        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/cancel", new ReasonRequest(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/cancel", new ReasonRequest("Customer changed the date"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var tender = await CurrentAsync(s.Admin, shipment.Summary.Id);
        tender.Status.ShouldBe(TenderStatus.Cancelled);
        tender.Invitees.ShouldAllBe(i => i.Status == InviteeStatus.Cancelled);
        (await ShipmentOfAsync(s.Admin, shipment.Summary.Id)).Summary.Status.ShouldBe(ShipmentStatus.Draft);
        (await second.Vendor.GetAsync($"/api/v1/shipments/{shipment.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_tender_needs_two_different_priced_transporters_and_refuses_a_shipment_already_out()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, flatRate: 30_000m);
        var shipment = await s.ShipmentAsync(await s.OrderAsync());

        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders", new StartTenderRequest(TenderMode.Broadcast, [s.Contract.Summary.Id], null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders", new StartTenderRequest(TenderMode.Broadcast, [s.Contract.Summary.Id, Guid.NewGuid()], null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tender", new TenderRequest(s.Contract.Summary.Id, null));
        var second = await SecondCarrierAsync(s, 35_000m);
        var again = await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders", new StartTenderRequest(TenderMode.Sequential, [s.Contract.Summary.Id, second.Contract.Summary.Id], null, null));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Offering_a_load_emails_the_transporter_and_a_superseded_bidder_is_told_it_is_closed()
    {
        using var s = await ShipmentScenario.CreateAsync(factory, flatRate: 30_000m);
        var second = await SecondCarrierAsync(s, 35_000m);
        var shipment = await s.ShipmentAsync(await s.OrderAsync());
        await StartAsync(s, shipment.Summary.Id, TenderMode.Broadcast, s.Contract.Summary.Id, second.Contract.Summary.Id);

        var mail = factory.Services.GetRequiredService<CapturingEmailSender>();
        var number = shipment.Summary.Number;
        await Eventually(() => mail.Sent.Count(m => m.Subject == $"Load {number} offered to you") >= 2);

        await second.Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/bid", new BidRequest(second.Vehicle.Id, second.Driver.Id, null, null));
        var winner = (await CurrentAsync(s.Admin, shipment.Summary.Id)).Invitees.Single(i => i.TransporterId == second.Transporter.Id);
        (await s.Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tenders/award", new AwardTenderRequest(winner.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await Eventually(() => mail.Sent.Any(m => m.Subject == $"Offer {number} closed"));
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100);
        }

        condition().ShouldBeTrue();
    }
}
