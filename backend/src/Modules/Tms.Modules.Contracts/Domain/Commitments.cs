using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

/// <summary>What the transporter has promised to keep available under this contract, and what the shipper has promised to give in return.</summary>
public sealed record CapacitySpec(
    Guid? VehicleTypeId,
    int CommittedVehicleCount,
    decimal? CommittedCapacityKg,
    int? MinimumMonthlyTrips,
    decimal? MinimumMonthlyTonnage,
    decimal? TargetBusinessSharePct,
    DateOnly? ValidFrom,
    DateOnly? ValidTo)
{
    public Result Validate()
    {
        static Result Fail(string field, string message) =>
            Error.Validation("contracts.capacity_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { [field] = [message] } };

        if (CommittedVehicleCount < 0 || CommittedCapacityKg < 0 || MinimumMonthlyTrips < 0 || MinimumMonthlyTonnage < 0)
        {
            return Fail("committedVehicleCount", "Commitments cannot be negative.");
        }

        if (TargetBusinessSharePct is < 0 or > 100)
        {
            return Fail("targetBusinessSharePct", "A business share is between 0 and 100%.");
        }

        if (ValidFrom is { } from && ValidTo is { } to && to < from)
        {
            return Fail("validTo", "The commitment must end on or after the day it starts.");
        }

        return CommittedVehicleCount == 0 && MinimumMonthlyTrips is null or 0 && MinimumMonthlyTonnage is null or 0 && CommittedCapacityKg is null or 0
            ? Fail("committedVehicleCount", "Commit to vehicles, trips, tonnage or capacity: an empty commitment says nothing.")
            : Result.Success();
    }
}

[AuditIgnore]
public sealed class ContractCapacity : Entity, ITenantScoped
{
    private ContractCapacity()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ContractId { get; private set; }

    public CapacitySpec Spec { get; private set; } = null!;

    internal static ContractCapacity Create(Guid tenantId, Guid contractId, CapacitySpec spec) => new() { TenantId = tenantId, ContractId = contractId, Spec = spec };
}

/// <summary>Service levels for a lane (or for everything the contract covers, when no lane is given).</summary>
public sealed record SlaSpec(
    ContractType Service,
    Place? Origin,
    Place? Destination,
    int? PickupSlaMinutes,
    int? TransitSlaMinutes,
    int? DeliverySlaMinutes,
    int? TenderLeadTimeMinutes,
    IReadOnlyList<DayOfWeek>? OperatingDays,
    TimeOnly? CutoffTime)
{
    public Result Validate()
    {
        static Result Fail(string field, string message) =>
            Error.Validation("contracts.sla_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { [field] = [message] } };

        if (!Enum.IsDefined(Service))
        {
            return Fail("service", "Choose the service.");
        }

        if (PickupSlaMinutes < 0 || TransitSlaMinutes < 0 || DeliverySlaMinutes < 0 || TenderLeadTimeMinutes < 0 || new[] { PickupSlaMinutes, TransitSlaMinutes, DeliverySlaMinutes, TenderLeadTimeMinutes }.Any(x => x > 60 * 24 * 60))
        {
            return Fail("transitSlaMinutes", "Times cannot be negative or longer than 60 days.");
        }

        if (PickupSlaMinutes is null && TransitSlaMinutes is null && DeliverySlaMinutes is null && TenderLeadTimeMinutes is null)
        {
            return Fail("transitSlaMinutes", "Give at least one service level.");
        }

        return OperatingDays?.Any(d => !Enum.IsDefined(d)) == true ? Fail("operatingDays", "An operating day is not valid.") : Result.Success();
    }

    public bool CoversLane(Location origin, Location destination, Func<string, Zone?> zones) =>
        (Origin is null || Origin.Matches(origin, zones)) && (Destination is null || Destination.Matches(destination, zones));
}

[AuditIgnore]
public sealed class ContractSla : Entity, ITenantScoped
{
    private ContractSla()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ContractId { get; private set; }

    public SlaSpec Spec { get; private set; } = null!;

    internal static ContractSla Create(Guid tenantId, Guid contractId, SlaSpec spec) => new() { TenantId = tenantId, ContractId = contractId, Spec = spec };
}
