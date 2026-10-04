using System.Text.RegularExpressions;
using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Fleet;

/// <summary>Transporter-owned vehicles. Placement-driven states are set by the placement workflow, not manually.</summary>
public interface IFleetService
{
    Task<VehicleDto> AddVehicleAsync(long transporterId, SaveVehicleRequest request, CancellationToken cancellationToken = default);

    Task<VehicleDto> UpdateVehicleAsync(long transporterId, long vehicleId, SaveVehicleRequest request, CancellationToken cancellationToken = default);
}

public sealed class FleetService(
    IRepository<Transporter> transporters,
    IRepository<TransporterVehicle> vehicles,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    IValidator<SaveVehicleRequest> validator,
    ILogger<FleetService> logger) : IFleetService
{
    /// <summary>States that only the placement workflow may set.</summary>
    private static readonly VehicleAvailabilityStatus[] WorkflowOnlyStatuses =
        [VehicleAvailabilityStatus.Assigned, VehicleAvailabilityStatus.InTransit];

    public async Task<VehicleDto> AddVehicleAsync(long transporterId, SaveVehicleRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        EnsureManualStatusAllowed(request.AvailabilityStatus, current: null);

        var registration = NormaliseRegistration(request.RegistrationNumber);
        if (await vehicles.AnyAsync(v => v.TransporterId == transporterId && v.RegistrationNumber == registration, cancellationToken))
        {
            throw new ConflictException($"Vehicle {registration} is already registered to this transporter.", "VEHICLE_REGISTRATION_EXISTS");
        }

        var entity = new TransporterVehicle
        {
            TransporterId = transporterId,
            RegistrationNumber = registration,
            VehicleTypeReference = request.VehicleTypeReference,
            PayloadCapacityKg = request.PayloadCapacityKg,
            UsableVolumeM3 = request.UsableVolumeM3,
            LengthM = request.LengthM,
            WidthM = request.WidthM,
            HeightM = request.HeightM,
            OwnershipType = request.OwnershipType,
            AvailabilityStatus = request.AvailabilityStatus,
            CurrentLocationReference = request.CurrentLocationReference,
            Status = request.Status
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        vehicles.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterVehicle", entity.Id.ToString(), "VehicleAdded",
            NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Vehicle {RegistrationNumber} added to transporter {TransporterId}", registration, transporterId);

        return entity.ToDto();
    }

    public async Task<VehicleDto> UpdateVehicleAsync(long transporterId, long vehicleId, SaveVehicleRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var entity = await vehicles.FindAsync(vehicleId, cancellationToken);
        if (entity is null || entity.TransporterId != transporterId)
        {
            throw new NotFoundException($"Vehicle {vehicleId} was not found for transporter {transporterId}.", "VEHICLE_NOT_FOUND");
        }

        EnsureManualStatusAllowed(request.AvailabilityStatus, current: entity.AvailabilityStatus);

        var registration = NormaliseRegistration(request.RegistrationNumber);
        if (registration != entity.RegistrationNumber
            && await vehicles.AnyAsync(v => v.TransporterId == transporterId && v.RegistrationNumber == registration, cancellationToken))
        {
            throw new ConflictException($"Vehicle {registration} is already registered to this transporter.", "VEHICLE_REGISTRATION_EXISTS");
        }

        var before = AuditJson.Serialize(entity);

        entity.RegistrationNumber = registration;
        entity.VehicleTypeReference = request.VehicleTypeReference;
        entity.PayloadCapacityKg = request.PayloadCapacityKg;
        entity.UsableVolumeM3 = request.UsableVolumeM3;
        entity.LengthM = request.LengthM;
        entity.WidthM = request.WidthM;
        entity.HeightM = request.HeightM;
        entity.OwnershipType = request.OwnershipType;
        entity.AvailabilityStatus = request.AvailabilityStatus;
        entity.CurrentLocationReference = request.CurrentLocationReference;
        entity.Status = request.Status;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterVehicle", vehicleId.ToString(), "VehicleUpdated",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.ToDto();
    }

    /// <summary>Assigned and in-transit are set when a vehicle is placed on a load. Manual edits may not introduce them.</summary>
    private static void EnsureManualStatusAllowed(VehicleAvailabilityStatus requested, VehicleAvailabilityStatus? current)
    {
        if (WorkflowOnlyStatuses.Contains(requested) && requested != current)
        {
            throw new BusinessRuleException(
                $"A vehicle cannot be set to {requested} manually. Placement on a load sets this status.",
                "VEHICLE_STATUS_NOT_MANUAL");
        }
    }

    /// <summary>Registrations are compared without spaces, hyphens or case, so "mh12 ab-1234" equals "MH12AB1234".</summary>
    private static string NormaliseRegistration(string value) =>
        Regex.Replace(value, "[\\s-]+", string.Empty).ToUpperInvariant();
}
