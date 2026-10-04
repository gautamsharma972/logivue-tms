using FluentValidation;
using Tms.Modules.Shipments.Domain;

namespace Tms.Modules.Shipments.Application.MilkRuns;

public sealed record MilkRunStopRequest(Guid LocationId, MilkRunStopType Type, int ServiceMinutes = 30, TimeOnly? WindowFrom = null, TimeOnly? WindowTo = null);

public sealed record SaveMilkRunRequest(
    string Code,
    string Name,
    Guid DepotLocationId,
    Guid? VehicleTypeId,
    int MaxStops,
    int MaxDurationMinutes,
    TimeOnly DepartureTime,
    IReadOnlyList<DayOfWeek> Days,
    IReadOnlyList<MilkRunStopRequest> Stops,
    bool IsActive = true,
    long? Version = null);

public sealed record MilkRunStopDto(
    int Sequence, Guid LocationId, string LocationName, string City, string State, MilkRunStopType Type, int ServiceMinutes, TimeOnly? WindowFrom, TimeOnly? WindowTo);

public sealed record MilkRunDto(
    Guid Id, string Code, string Name, Guid DepotLocationId, string DepotName, Guid? VehicleTypeId, int MaxStops, int MaxDurationMinutes,
    TimeOnly DepartureTime, IReadOnlyList<DayOfWeek> Days, bool IsActive, IReadOnlyList<MilkRunStopDto> Stops, long Version);

public sealed record ListMilkRunsQuery(string? Search = null, bool? Active = null, int Page = 1, int PageSize = 50);

public sealed record PlanMilkRunRequest(Guid MilkRunId, DateOnly Date, bool KeepTemplateOrder = true);

internal sealed class SaveMilkRunRequestValidator : AbstractValidator<SaveMilkRunRequest>
{
    public SaveMilkRunRequestValidator()
    {
        // Field rules live in the domain; this guards the request's shape.
        RuleFor(x => x.Code).NotNull();
        RuleFor(x => x.Name).NotNull();
        RuleFor(x => x.Days).NotNull();
        RuleFor(x => x.Stops).NotNull();
    }
}
