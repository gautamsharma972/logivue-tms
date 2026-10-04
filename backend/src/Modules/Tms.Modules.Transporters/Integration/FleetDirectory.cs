using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Transporters.Integration;

/// <summary>Lets planning see a transporter's vehicles and drivers together with whether their papers are in order.</summary>
internal sealed class FleetDirectory(TransportersDbContext db, TimeProvider clock, Application.MasterData.DocumentPolicyProvider policies) : IFleetDirectory
{
    public async Task<FleetVehicle?> GetVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);
        return vehicle is null ? null : (await ToVehiclesAsync([vehicle], cancellationToken))[0];
    }

    public async Task<FleetDriver?> GetDriverAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        var driver = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId, cancellationToken);
        return driver is null ? null : (await ToDriversAsync([driver], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<FleetVehicle>> ListVehiclesAsync(Guid transporterId, CancellationToken cancellationToken = default) =>
        await ToVehiclesAsync(await db.Vehicles.AsNoTracking().Where(v => v.TransporterId == transporterId && v.IsActive).OrderBy(v => v.RegistrationNumber).ToListAsync(cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<FleetDriver>> ListDriversAsync(Guid transporterId, CancellationToken cancellationToken = default) =>
        await ToDriversAsync(await db.Drivers.AsNoTracking().Where(d => d.TransporterId == transporterId && d.IsActive).OrderBy(d => d.FullName).ToListAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<FleetVehicle>> ToVehiclesAsync(IReadOnlyList<Vehicle> vehicles, CancellationToken cancellationToken)
    {
        var documents = await Application.Fleet.OwnerDocuments.LoadAsync(db, OwnerKind.Vehicle, vehicles.Select(v => v.Id).ToList(), cancellationToken);
        var typeIds = vehicles.Select(v => v.VehicleTypeId).Distinct().ToList();
        var types = await db.VehicleTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        var today = clock.TodayInIndia();
        var policy = await policies.GetAsync(cancellationToken);

        return vehicles.Select(v =>
        {
            var compliance = ComplianceEvaluator.ForVehicle(documents[v.Id], today, policy);
            types.TryGetValue(v.VehicleTypeId, out var type);
            return new FleetVehicle(v.Id, v.TransporterId, v.RegistrationNumber, v.VehicleTypeId, type?.Name ?? "Unknown", type?.PayloadKg ?? 0,
                v.IsActive, Map(compliance.Status), compliance.Issues, v.Availability, v.AvailableFrom, v.AvailableTo, v.AvailabilityNote);
        }).ToList();
    }

    private async Task<IReadOnlyList<FleetDriver>> ToDriversAsync(IReadOnlyList<Driver> drivers, CancellationToken cancellationToken)
    {
        var documents = await Application.Fleet.OwnerDocuments.LoadAsync(db, OwnerKind.Driver, drivers.Select(d => d.Id).ToList(), cancellationToken);
        var today = clock.TodayInIndia();
        var policy = await policies.GetAsync(cancellationToken);

        return drivers.Select(d =>
        {
            var compliance = ComplianceEvaluator.ForDriver(documents[d.Id], today, policy);
            return new FleetDriver(d.Id, d.TransporterId, d.FullName, d.Phone, d.LicenseNumber, d.IsActive, Map(compliance.Status), compliance.Issues);
        }).ToList();
    }

    private static FleetCompliance Map(ComplianceStatus status) => status switch
    {
        ComplianceStatus.Compliant => FleetCompliance.Compliant,
        ComplianceStatus.ExpiringSoon => FleetCompliance.ExpiringSoon,
        _ => FleetCompliance.NonCompliant,
    };
}
