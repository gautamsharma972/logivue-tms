using FluentValidation;
using Tms.Modules.Shipments.Domain;

namespace Tms.Modules.Shipments.Application.Locations;

public sealed record SaveLocationRequest(
    string Code, string Name, LocationType Type, string Line1, string City, string State, string Pincode,
    double Latitude, double Longitude, bool IsActive = true, long? Version = null);

public sealed record LocationDto(
    Guid Id, string Code, string Name, LocationType Type, string Line1, string City, string State, string Pincode,
    double Latitude, double Longitude, bool IsActive, long Version);

public sealed record ListLocationsQuery(string? Search = null, LocationType? Type = null, bool? Active = null, int Page = 1, int PageSize = 50);

public sealed record DistanceRequest(Guid FromLocationId, Guid ToLocationId);

public sealed record DistanceDto(double DistanceKm, double DurationMinutes, RouteSource Source);

internal sealed class SaveLocationRequestValidator : AbstractValidator<SaveLocationRequest>
{
    public SaveLocationRequestValidator()
    {
        // Field rules (coordinates inside India, pincode, code shape) live in the domain; this guards the request's shape.
        RuleFor(x => x.Code).NotNull();
        RuleFor(x => x.Name).NotNull();
        RuleFor(x => x.Line1).NotNull();
        RuleFor(x => x.City).NotNull();
        RuleFor(x => x.State).NotNull();
        RuleFor(x => x.Pincode).NotNull();
    }
}
