using System.Net;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.IntegrationTests.Infrastructure.TransporterTestData;

namespace Tms.IntegrationTests.Infrastructure;

/// <summary>
/// One self-contained lane per test: made-up state names keep rates from other tests' contracts from competing for the quote,
/// since every contract in the tenant is a candidate.
/// </summary>
internal sealed class ShipmentScenario : IDisposable
{
    public required string OriginState { get; init; }

    public required string DropState { get; init; }

    public required TransporterDto Transporter { get; init; }

    public required ContractDto Contract { get; init; }

    public required VehicleDto Vehicle { get; init; }

    public required DriverDto Driver { get; init; }

    public required Guid VehicleTypeId { get; init; }

    public required HttpClient Admin { get; init; }

    public required HttpClient Vendor { get; init; }

    public decimal FlatRate { get; init; }

    public static async Task<ShipmentScenario> CreateAsync(TmsApiFactory factory, decimal flatRate = 40_000m, bool fleetPapers = true, int fleetSize = 3)
    {
        var admin = await factory.AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var originState = $"Origin{suffix}";
        var dropState = $"Drop{suffix}";

        var transporter = await ContractApiData.ActiveTransporterAsync(admin);
        var type = await ContractApiData.VehicleTypeAsync(admin);
        var contract = await ContractApiData.ActiveContractAsync(
            admin, transporter.Id, ContractType.Ftl, null,
            ContractApiData.Flat(ContractApiData.State(originState), ContractApiData.State(dropState), flatRate, type.Id));

        var (vehicle, driver) = await RegisterFleetAsync(admin, transporter.Id, type.Id, fleetPapers);
        for (var i = 1; i < fleetSize; i++)
        {
            await RegisterFleetAsync(admin, transporter.Id, type.Id, fleetPapers); // planning gives each trip its own vehicle and driver, so a plan of several trips needs several
        }

        var role = await admin.CreateExternalRoleAsync(Tms.Modules.Shipments.Domain.ShipmentPermissions.Respond);
        var email = ApiExtensions.UniqueEmail("vendor");
        var created = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            email, "Vendor Dispatcher", ApiExtensions.StrongPassword, UserType.Transporter, [role.Id], transporter.Id));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return new ShipmentScenario
        {
            OriginState = originState,
            DropState = dropState,
            Transporter = transporter,
            Contract = contract,
            Vehicle = vehicle,
            Driver = driver,
            VehicleTypeId = type.Id,
            Admin = admin,
            Vendor = await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword),
            FlatRate = flatRate,
        };
    }

    /// <summary>Registers one vehicle and one driver for a transporter, with valid papers unless asked otherwise.</summary>
    public static async Task<(VehicleDto Vehicle, DriverDto Driver)> RegisterFleetAsync(HttpClient admin, Guid transporterId, Guid vehicleTypeId, bool papers = true)
    {
        var plate = $"MH{Random.Shared.Next(10, 99)}{(char)('A' + Random.Shared.Next(26))}{(char)('A' + Random.Shared.Next(26))}{Random.Shared.Next(1000, 9999)}";
        var vehicleResponse = await admin.PostJsonAsync($"/api/v1/transporters/{transporterId}/vehicles",
            new SaveVehicleRequest(plate, vehicleTypeId, VehicleOwnership.Owned, "Tata", 2022, true, null));
        vehicleResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await vehicleResponse.Content.ReadAsStringAsync());
        var vehicle = await vehicleResponse.ReadAsync<VehicleDto>();

        var driverResponse = await admin.PostJsonAsync($"/api/v1/transporters/{transporterId}/drivers",
            new SaveDriverRequest("Ramesh Yadav", "9876543210", $"MH12{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}", true, null));
        driverResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await driverResponse.Content.ReadAsStringAsync());
        var driver = await driverResponse.ReadAsync<DriverDto>();

        if (papers)
        {
            var year = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
            await UploadOkAsync(admin, transporterId, OwnerKind.Vehicle, vehicle.Id, DocumentKind.RegistrationCertificate);
            await UploadOkAsync(admin, transporterId, OwnerKind.Vehicle, vehicle.Id, DocumentKind.Insurance, year);
            await UploadOkAsync(admin, transporterId, OwnerKind.Vehicle, vehicle.Id, DocumentKind.Fitness, year);
            await UploadOkAsync(admin, transporterId, OwnerKind.Vehicle, vehicle.Id, DocumentKind.Permit, year);
            await UploadOkAsync(admin, transporterId, OwnerKind.Driver, driver.Id, DocumentKind.DrivingLicense, year);
        }

        return (vehicle, driver);
    }

    public void Dispose()
    {
        Admin.Dispose();
        Vendor.Dispose();
    }

    public static PartyDto Party(string state, string city = "Town", string name = "Consignor") =>
        new($"{name} Pvt Ltd", "Plot 1, Industrial Area", city, state, "411019", "Contact", "9876543210");

    public SaveOrderRequest NewOrder(decimal weightKg = 5_000m, string? dropCity = null, OrderDirection direction = OrderDirection.Forward) =>
        new(direction, "PO-1", Party(OriginState, "Origin City"), Party(DropState, dropCity ?? "Drop City", "Consignee"), weightKg, 20m, 40,
            "Auto parts", ContractApiData.Today, ContractApiData.Today.AddDays(5), null,
            ReturnType: direction == OrderDirection.Reverse ? Tms.Modules.Shipments.Domain.ReturnType.CustomerReturn : null,
            ReturnReason: direction == OrderDirection.Reverse ? "Customer returned the goods" : null);

    public async Task<OrderDto> OrderAsync(decimal weightKg = 5_000m, string? dropCity = null)
    {
        var response = await Admin.PostJsonAsync("/api/v1/orders", NewOrder(weightKg, dropCity));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<OrderDto>();
    }

    public async Task<ShipmentDto> ShipmentAsync(params OrderDto[] orders)
    {
        var response = await Admin.PostJsonAsync("/api/v1/shipments",
            new CreateShipmentRequest(orders.Select(o => o.Id).ToList(), FreightMode.Ftl, VehicleTypeId, ContractApiData.Today, null));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ShipmentDto>();
    }

    public async Task<ShipmentDto> TenderedAsync(params OrderDto[] orders)
    {
        var shipment = await ShipmentAsync(orders.Length == 0 ? [await OrderAsync()] : orders);
        var response = await Admin.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/tender", new TenderRequest(Contract.Summary.Id, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ShipmentDto>();
    }

    public async Task<ShipmentDto> AcceptedAsync()
    {
        var shipment = await TenderedAsync();
        var response = await Vendor.PostJsonAsync($"/api/v1/shipments/{shipment.Summary.Id}/accept", new AcceptRequest(Vehicle.Id, Driver.Id));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ShipmentDto>();
    }
}
