using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Fleet;

/// <summary>Gives every tenant the standard Indian truck classes the first time vehicle types are needed.</summary>
internal sealed class VehicleTypeSeeder(TransportersDbContext db, ICurrentUser currentUser)
{
    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId || await db.VehicleTypes.AnyAsync(cancellationToken))
        {
            return;
        }

        foreach (var (code, name, payload, volume) in VehicleType.Defaults)
        {
            var dims = VehicleType.DefaultDimensions.GetValueOrDefault(code);
            db.VehicleTypes.Add(VehicleType.Create(tenantId, code, name, payload, volume, dims.L == 0 ? null : dims.L, dims.W == 0 ? null : dims.W, dims.H == 0 ? null : dims.H).Value);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            db.ChangeTracker.Clear(); // a concurrent request seeded first; use theirs
        }
    }
}

internal sealed class ListVehicleTypesHandler(TransportersDbContext db, TransporterAccess access, VehicleTypeSeeder seeder)
{
    public async Task<Result<IReadOnlyList<VehicleTypeDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!access.CanUseModule)
        {
            return TransporterAccess.Forbidden;
        }

        await seeder.EnsureDefaultsAsync(cancellationToken);
        var types = await db.VehicleTypes.AsNoTracking().OrderBy(t => t.PayloadKg).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<VehicleTypeDto>>(types.Select(t => t.ToDto()).ToList());
    }
}

internal sealed class SaveVehicleTypeHandler(TransportersDbContext db, ICurrentUser currentUser, TransporterAccess access, VehicleTypeSeeder seeder)
{
    public async Task<Result<VehicleTypeDto>> CreateAsync(SaveVehicleTypeRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageInternally)
        {
            return TransporterAccess.Forbidden;
        }

        await seeder.EnsureDefaultsAsync(cancellationToken);
        var created = VehicleType.Create(currentUser.TenantId!.Value, request.Code, request.Name, request.PayloadKg, request.VolumeCbm, request.LengthM, request.WidthM, request.HeightM, request.AllowsHazardous, request.SupportsTemperatureControl);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var taken = Error.Conflict("vehicle_types.code_exists", $"A vehicle type with code {created.Value.Code} already exists.");
        if (await db.VehicleTypes.AnyAsync(t => t.Code == created.Value.Code, cancellationToken))
        {
            return taken;
        }

        db.VehicleTypes.Add(created.Value);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return taken;
        }

        return created.Value.ToDto();
    }

    public async Task<Result<VehicleTypeDto>> UpdateAsync(Guid id, SaveVehicleTypeRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageInternally)
        {
            return TransporterAccess.Forbidden;
        }

        if (request.Version is null)
        {
            return Error.Validation("vehicle_types.version_required", "The current version is required.");
        }

        var type = await db.VehicleTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (type is null)
        {
            return Error.NotFound("vehicle_types.not_found", "Vehicle type not found.");
        }

        db.Entry(type).Property(t => t.Version).OriginalValue = request.Version.Value;
        var updated = type.Update(request.Name, request.PayloadKg, request.VolumeCbm, request.IsActive, request.LengthM, request.WidthM, request.HeightM, request.AllowsHazardous, request.SupportsTemperatureControl);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return type.ToDto();
    }
}

internal static class OwnerDocuments
{
    public static async Task<ILookup<Guid, ComplianceDocument>> LoadAsync(
        TransportersDbContext db, OwnerKind kind, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken)
    {
        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.OwnerKind == kind && ownerIds.Contains(d.OwnerId) && d.SupersededAt == null)
            .ToListAsync(cancellationToken);
        return documents.ToLookup(d => d.OwnerId);
    }
}

internal sealed class VehicleHandler(
    TransportersDbContext db,
    ICurrentUser currentUser,
    TransporterAccess access,
    VehicleTypeSeeder seeder,
    TimeProvider clock,
    Application.MasterData.DocumentPolicyProvider policies)
{
    public async Task<Result<PagedResult<VehicleDto>>> ListAsync(Guid transporterId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var allowed = access.Check(transporterId, AccessLevel.Read);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        await seeder.EnsureDefaultsAsync(cancellationToken);
        var vehicles = db.Vehicles.AsNoTracking().Where(v => v.TransporterId == transporterId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = IndianIdentifiersFor(search);
            vehicles = vehicles.Where(v => v.RegistrationNumber.Contains(term));
        }

        var result = await vehicles.OrderBy(v => v.RegistrationNumber).ToPagedAsync(page, pageSize, cancellationToken);
        var dtos = await ToDtosAsync(result.Items, cancellationToken);
        return new PagedResult<VehicleDto>(dtos, result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<VehicleDto>> CreateAsync(Guid transporterId, SaveVehicleRequest request, CancellationToken cancellationToken)
    {
        var transporter = await db.FindTransporterAsync(access, transporterId, AccessLevel.Manage, cancellationToken);
        if (transporter.IsFailure)
        {
            return transporter.Error;
        }

        var typeCheck = await CheckVehicleTypeAsync(request.VehicleTypeId, cancellationToken);
        if (typeCheck is not null)
        {
            return typeCheck;
        }

        var created = Vehicle.Create(currentUser.TenantId!.Value, transporterId, request.RegistrationNumber, request.VehicleTypeId, request.Ownership, request.Make, request.YearOfManufacture);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var availability = created.Value.SetAvailability(request.Availability, request.AvailableFrom, request.AvailableTo, request.AvailabilityNote);
        if (availability.IsFailure)
        {
            return availability.Error;
        }

        var duplicate = Error.Conflict("vehicles.registration_exists", $"Vehicle {created.Value.RegistrationNumber} is already registered.");
        if (await db.Vehicles.AnyAsync(v => v.RegistrationNumber == created.Value.RegistrationNumber, cancellationToken))
        {
            return duplicate;
        }

        db.Vehicles.Add(created.Value);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return duplicate;
        }

        return (await ToDtosAsync([created.Value], cancellationToken))[0];
    }

    public async Task<Result<VehicleDto>> UpdateAsync(Guid vehicleId, SaveVehicleRequest request, CancellationToken cancellationToken)
    {
        if (request.Version is null)
        {
            return Error.Validation("vehicles.version_required", "The current version of the vehicle is required.");
        }

        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);
        if (vehicle is null || access.Check(vehicle.TransporterId, AccessLevel.Manage) is { IsFailure: true })
        {
            return Error.NotFound("vehicles.not_found", "Vehicle not found.");
        }

        var typeCheck = await CheckVehicleTypeAsync(request.VehicleTypeId, cancellationToken);
        if (typeCheck is not null)
        {
            return typeCheck;
        }

        db.Entry(vehicle).Property(v => v.Version).OriginalValue = request.Version.Value;
        var updated = vehicle.Update(request.VehicleTypeId, request.Ownership, request.Make, request.YearOfManufacture, request.IsActive);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var availability = vehicle.SetAvailability(request.Availability, request.AvailableFrom, request.AvailableTo, request.AvailabilityNote);
        if (availability.IsFailure)
        {
            return availability.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await ToDtosAsync([vehicle], cancellationToken))[0];
    }

    private async Task<Error?> CheckVehicleTypeAsync(Guid vehicleTypeId, CancellationToken cancellationToken) =>
        await db.VehicleTypes.AnyAsync(t => t.Id == vehicleTypeId && t.IsActive, cancellationToken)
            ? null
            : Error.Validation("vehicles.type_unknown", "Choose an active vehicle type.");

    private async Task<IReadOnlyList<VehicleDto>> ToDtosAsync(IReadOnlyList<Vehicle> vehicles, CancellationToken cancellationToken)
    {
        var ids = vehicles.Select(v => v.Id).ToList();
        var documents = await OwnerDocuments.LoadAsync(db, OwnerKind.Vehicle, ids, cancellationToken);
        var typeIds = vehicles.Select(v => v.VehicleTypeId).Distinct().ToList();
        var types = await db.VehicleTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        var today = clock.TodayInIndia();
        var policy = await policies.GetAsync(cancellationToken);

        return vehicles.Select(v =>
        {
            types.TryGetValue(v.VehicleTypeId, out var type);
            return new VehicleDto(v.Id, v.TransporterId, v.RegistrationNumber, v.VehicleTypeId, type?.Name ?? "Unknown", type?.PayloadKg ?? 0,
                v.Ownership, v.Make, v.YearOfManufacture, v.IsActive, ComplianceEvaluator.ForVehicle(documents[v.Id], today, policy).ToDto(), v.Version,
                v.Availability, v.AvailableFrom, v.AvailableTo, v.AvailabilityNote);
        }).ToList();
    }

    private static string IndianIdentifiersFor(string search) => Tms.SharedKernel.India.IndianIdentifiers.NormaliseVehicleRegistration(search);
}

internal sealed class DriverHandler(TransportersDbContext db, ICurrentUser currentUser, TransporterAccess access, TimeProvider clock, Application.MasterData.DocumentPolicyProvider policies)
{
    public async Task<Result<PagedResult<DriverDto>>> ListAsync(Guid transporterId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var allowed = access.Check(transporterId, AccessLevel.Read);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var drivers = db.Drivers.AsNoTracking().Where(d => d.TransporterId == transporterId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            drivers = drivers.Where(d => d.FullName.Contains(term) || d.Phone.Contains(term));
        }

        var result = await drivers.OrderBy(d => d.FullName).ToPagedAsync(page, pageSize, cancellationToken);
        return new PagedResult<DriverDto>(await ToDtosAsync(result.Items, cancellationToken), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<DriverDto>> CreateAsync(Guid transporterId, SaveDriverRequest request, CancellationToken cancellationToken)
    {
        var transporter = await db.FindTransporterAsync(access, transporterId, AccessLevel.Manage, cancellationToken);
        if (transporter.IsFailure)
        {
            return transporter.Error;
        }

        var created = Driver.Create(currentUser.TenantId!.Value, transporterId, request.FullName, request.Phone, request.LicenseNumber);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var duplicate = Error.Conflict("drivers.license_exists", "A driver with this licence number is already registered.");
        if (created.Value.LicenseNumber is { } license && await db.Drivers.AnyAsync(d => d.LicenseNumber == license, cancellationToken))
        {
            return duplicate;
        }

        db.Drivers.Add(created.Value);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return duplicate;
        }

        return (await ToDtosAsync([created.Value], cancellationToken))[0];
    }

    public async Task<Result<DriverDto>> UpdateAsync(Guid driverId, SaveDriverRequest request, CancellationToken cancellationToken)
    {
        if (request.Version is null)
        {
            return Error.Validation("drivers.version_required", "The current version of the driver is required.");
        }

        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Id == driverId, cancellationToken);
        if (driver is null || access.Check(driver.TransporterId, AccessLevel.Manage) is { IsFailure: true })
        {
            return Error.NotFound("drivers.not_found", "Driver not found.");
        }

        db.Entry(driver).Property(d => d.Version).OriginalValue = request.Version.Value;
        var updated = driver.Update(request.FullName, request.Phone, request.LicenseNumber, request.IsActive);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("drivers.license_exists", "A driver with this licence number is already registered.");
        }

        return (await ToDtosAsync([driver], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<DriverDto>> ToDtosAsync(IReadOnlyList<Driver> drivers, CancellationToken cancellationToken)
    {
        var documents = await OwnerDocuments.LoadAsync(db, OwnerKind.Driver, drivers.Select(d => d.Id).ToList(), cancellationToken);
        var today = clock.TodayInIndia();
        var policy = await policies.GetAsync(cancellationToken);
        return drivers.Select(d => new DriverDto(d.Id, d.TransporterId, d.FullName, d.Phone, d.LicenseNumber, d.IsActive,
            ComplianceEvaluator.ForDriver(documents[d.Id], today, policy).ToDto(), d.Version)).ToList();
    }
}
