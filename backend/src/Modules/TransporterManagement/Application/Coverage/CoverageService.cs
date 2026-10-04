using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Coverage;

/// <summary>Where a transporter operates (lanes) and what it can do (capabilities).</summary>
public interface ICoverageService
{
    Task<LaneDto> AddLaneAsync(long transporterId, SaveLaneRequest request, CancellationToken cancellationToken = default);

    Task<LaneDto> UpdateLaneAsync(long transporterId, long laneId, SaveLaneRequest request, CancellationToken cancellationToken = default);

    Task<CapabilityDto> AddCapabilityAsync(long transporterId, AddCapabilityRequest request, CancellationToken cancellationToken = default);

    Task RemoveCapabilityAsync(long transporterId, long capabilityId, CancellationToken cancellationToken = default);
}

public sealed class CoverageService(
    IRepository<Transporter> transporters,
    IRepository<TransporterLane> lanes,
    IRepository<TransporterCapability> capabilities,
    IRepository<CapabilityType> capabilityTypes,
    IRepository<ServiceTypeDefinition> serviceTypes,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<SaveLaneRequest> laneValidator,
    IValidator<AddCapabilityRequest> capabilityValidator,
    ILogger<CoverageService> logger) : ICoverageService
{
    private static readonly DateTime OpenEnded = DateTime.MaxValue;

    public async Task<LaneDto> AddLaneAsync(long transporterId, SaveLaneRequest request, CancellationToken cancellationToken = default)
    {
        await laneValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var serviceType = request.ServiceType.Trim().ToUpperInvariant();
        await ServiceTypeGuard.EnsureActiveAsync(serviceTypes, serviceType, cancellationToken);
        await EnsureNoOverlappingLaneAsync(transporterId, 0, request, serviceType, cancellationToken);

        var entity = new TransporterLane
        {
            TransporterId = transporterId,
            OriginLocationReference = request.OriginLocationReference,
            DestinationLocationReference = request.DestinationLocationReference,
            ServiceType = serviceType,
            VehicleTypeReference = request.VehicleTypeReference,
            TransitSlaMinutes = request.TransitSlaMinutes,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Status = request.Status
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        lanes.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterLane", entity.Id.ToString(), "LaneAdded",
            NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Lane {ServiceType} {Origin}->{Destination} added to transporter {TransporterId}",
            serviceType, entity.OriginLocationReference, entity.DestinationLocationReference, transporterId);

        return entity.ToDto();
    }

    public async Task<LaneDto> UpdateLaneAsync(long transporterId, long laneId, SaveLaneRequest request, CancellationToken cancellationToken = default)
    {
        await laneValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var entity = await lanes.FindAsync(laneId, cancellationToken);
        if (entity is null || entity.TransporterId != transporterId)
        {
            throw new NotFoundException($"Lane {laneId} was not found for transporter {transporterId}.", "LANE_NOT_FOUND");
        }

        var serviceType = request.ServiceType.Trim().ToUpperInvariant();
        await ServiceTypeGuard.EnsureActiveAsync(serviceTypes, serviceType, cancellationToken);
        await EnsureNoOverlappingLaneAsync(transporterId, laneId, request, serviceType, cancellationToken);

        var before = AuditJson.Serialize(entity);

        entity.OriginLocationReference = request.OriginLocationReference;
        entity.DestinationLocationReference = request.DestinationLocationReference;
        entity.ServiceType = serviceType;
        entity.VehicleTypeReference = request.VehicleTypeReference;
        entity.TransitSlaMinutes = request.TransitSlaMinutes;
        entity.EffectiveFrom = request.EffectiveFrom;
        entity.EffectiveTo = request.EffectiveTo;
        entity.Status = request.Status;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterLane", laneId.ToString(), "LaneUpdated",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.ToDto();
    }

    public async Task<CapabilityDto> AddCapabilityAsync(long transporterId, AddCapabilityRequest request, CancellationToken cancellationToken = default)
    {
        await capabilityValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var type = await capabilityTypes.FindAsync(request.CapabilityTypeId, cancellationToken);
        if (type is null || !type.IsActive)
        {
            throw new BusinessRuleException(
                $"Capability type {request.CapabilityTypeId} is not available.", "CAPABILITY_TYPE_INVALID");
        }

        var alreadyOpen = await capabilities.AnyAsync(c =>
            c.TransporterId == transporterId
            && c.CapabilityTypeId == request.CapabilityTypeId
            && c.Status == RecordStatus.Active
            && c.EffectiveTo == null, cancellationToken);
        if (alreadyOpen)
        {
            throw new ConflictException($"Capability {type.Code} is already assigned to this transporter.", "CAPABILITY_ALREADY_ASSIGNED");
        }

        var entity = new TransporterCapability
        {
            TransporterId = transporterId,
            CapabilityTypeId = request.CapabilityTypeId,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            Status = RecordStatus.Active
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        capabilities.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterCapability", entity.Id.ToString(), "CapabilityAdded",
            NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.ToDto(type.Code, type.Name);
    }

    /// <summary>
    /// Removal is a soft end-date, not a delete, so historical eligibility and performance stay explainable.
    /// </summary>
    public async Task RemoveCapabilityAsync(long transporterId, long capabilityId, CancellationToken cancellationToken = default)
    {
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var entity = await capabilities.FindAsync(capabilityId, cancellationToken);
        if (entity is null || entity.TransporterId != transporterId)
        {
            throw new NotFoundException($"Capability {capabilityId} was not found for transporter {transporterId}.", "CAPABILITY_NOT_FOUND");
        }

        if (entity.Status == RecordStatus.Inactive)
        {
            throw new BusinessRuleException($"Capability {capabilityId} has already been removed.", "CAPABILITY_ALREADY_REMOVED");
        }

        var before = AuditJson.Serialize(entity);
        entity.Status = RecordStatus.Inactive;
        entity.EffectiveTo = clock.GetUtcNow().UtcDateTime.Date;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterCapability", capabilityId.ToString(), "CapabilityRemoved",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// A transporter may not have two active lanes for the same origin, destination, service and vehicle type
    /// whose effective periods overlap. Otherwise eligibility would be ambiguous.
    /// </summary>
    private async Task EnsureNoOverlappingLaneAsync(long transporterId, long excludeLaneId, SaveLaneRequest request, string serviceType, CancellationToken cancellationToken)
    {
        var candidates = await lanes.ListAsync(l =>
            l.TransporterId == transporterId
            && l.Id != excludeLaneId
            && l.Status == RecordStatus.Active
            && l.OriginLocationReference == request.OriginLocationReference
            && l.DestinationLocationReference == request.DestinationLocationReference
            && l.ServiceType == serviceType
            && l.VehicleTypeReference == request.VehicleTypeReference, cancellationToken);

        var newEnd = request.EffectiveTo ?? OpenEnded;
        var overlaps = candidates.Any(existing =>
            existing.EffectiveFrom <= newEnd && (existing.EffectiveTo ?? OpenEnded) >= request.EffectiveFrom);

        if (overlaps)
        {
            throw new ConflictException(
                $"An active {serviceType} lane for this origin and destination already overlaps the requested period.",
                "LANE_OVERLAP");
        }
    }
}
