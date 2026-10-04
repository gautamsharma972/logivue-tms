using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application.Locations;

internal static class LocationMapping
{
    public static readonly Error NotFound = Error.NotFound("locations.not_found", "Location not found.");

    public static readonly Error CodeExists = Error.Conflict("locations.code_exists", "A location with this code already exists.");

    public static LocationDto ToDto(this Location l) =>
        new(l.Id, l.Code, l.Name, l.Type, l.Line1, l.City, l.State, l.Pincode, l.Latitude, l.Longitude, l.IsActive, l.Version);
}

internal sealed class ListLocationsHandler(ShipmentsDbContext db, ShipmentAccess access)
{
    public async Task<Result<PagedResult<LocationDto>>> HandleAsync(ListLocationsQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var locations = db.Locations.AsNoTracking().AsQueryable();
        if (query.Type is { } type)
        {
            locations = locations.Where(l => l.Type == type);
        }

        if (query.Active is { } active)
        {
            locations = locations.Where(l => l.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            locations = locations.Where(l => l.Code.Contains(term) || l.Name.Contains(term) || l.City.Contains(term));
        }

        var page = await locations.OrderBy(l => l.Name).ThenBy(l => l.Code).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<LocationDto>(page.Items.Select(l => l.ToDto()).ToList(), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetLocationHandler(ShipmentsDbContext db, ShipmentAccess access)
{
    public async Task<Result<LocationDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        return await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken) is { } l ? l.ToDto() : LocationMapping.NotFound;
    }
}

internal sealed class SaveLocationHandler(ShipmentsDbContext db, ShipmentAccess access, ICurrentUser user)
{
    public async Task<Result<LocationDto>> CreateAsync(SaveLocationRequest r, CancellationToken cancellationToken)
    {
        if (!access.CanPlan || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var location = Location.Create(tenantId, r.Code, r.Name, r.Type, r.Line1, r.City, r.State, r.Pincode, r.Latitude, r.Longitude);
        if (location.IsFailure)
        {
            return location.Error;
        }

        db.Locations.Add(location.Value);
        return await SaveAsync(location.Value, cancellationToken);
    }

    public async Task<Result<LocationDto>> UpdateAsync(Guid id, SaveLocationRequest r, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return LocationMapping.NotFound;
        }

        var updated = location.Update(r.Code, r.Name, r.Type, r.Line1, r.City, r.State, r.Pincode, r.Latitude, r.Longitude, r.IsActive);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (r.Version is { } version)
        {
            db.Entry(location).Property(l => l.Version).OriginalValue = version;
        }

        return await SaveAsync(location, cancellationToken);
    }

    private async Task<Result<LocationDto>> SaveAsync(Location location, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            return LocationMapping.CodeExists;
        }

        return location.ToDto();
    }
}

internal sealed class DistanceHandler(ShipmentsDbContext db, ShipmentAccess access, IRoutingProvider routing)
{
    /// <summary>Road distance between two master locations (or an estimate, labelled as such).</summary>
    public async Task<Result<DistanceDto>> HandleAsync(DistanceRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var ids = new[] { request.FromLocationId, request.ToLocationId };
        var found = await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        if (!found.TryGetValue(request.FromLocationId, out var from) || !found.TryGetValue(request.ToLocationId, out var to))
        {
            return LocationMapping.NotFound;
        }

        var route = await routing.GetRouteAsync([from.Point, to.Point], cancellationToken);
        return new DistanceDto(Math.Round(route.TotalKm, 1), Math.Round(route.TotalMinutes), route.Source);
    }
}

/// <summary>Turns order requests that name master locations into addresses, so an order's address always matches its location.</summary>
internal static class OrderLocations
{
    public static async Task<Result<(Party Pickup, Party Drop)>> ResolveAsync(ShipmentsDbContext db, SaveOrderRequest request, CancellationToken cancellationToken)
    {
        var ids = new[] { request.PickupLocationId, request.DropLocationId }.OfType<Guid>().Distinct().ToList();
        var found = ids.Count == 0 ? [] : await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);

        Result<Party> Resolve(PartyDto party, Guid? locationId, string field)
        {
            if (locationId is not { } id)
            {
                return party.ToParty();
            }

            if (!found.TryGetValue(id, out var l) || !l.IsActive)
            {
                return Error.Validation("orders.location_invalid", "The chosen location does not exist or is inactive.") with
                {
                    ValidationErrors = new Dictionary<string, string[]> { [$"{field}LocationId"] = ["Choose an active location."] },
                };
            }

            return new Party(l.Name, l.Line1, l.City, l.State, l.Pincode, party.ContactName, party.ContactPhone);
        }

        var pickup = Resolve(request.Pickup, request.PickupLocationId, "pickup");
        if (pickup.IsFailure)
        {
            return pickup.Error;
        }

        var drop = Resolve(request.Drop, request.DropLocationId, "drop");
        return drop.IsFailure ? drop.Error : (pickup.Value, drop.Value);
    }
}

/// <summary>Gives the optimizer each order together with the coordinates of its linked locations.</summary>
internal sealed class PlannableFactory(ShipmentsDbContext db)
{
    public async Task<List<PlannableOrder>> BuildAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken)
    {
        var ids = orders.SelectMany(o => new[] { o.PickupLocationId, o.DropLocationId }).OfType<Guid>().Distinct().ToList();
        var points = ids.Count == 0
            ? new Dictionary<Guid, GeoPoint>()
            : await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => new GeoPoint(l.Latitude, l.Longitude), cancellationToken);

        GeoPoint? At(Guid? id) => id is { } i && points.TryGetValue(i, out var p) ? p : null;
        return orders.Select(o => new PlannableOrder(
            o.Id, o.Number, o.Direction, o.PickupState, o.PickupCity, o.DropState, o.DropCity, o.WeightKg, o.VolumeCbm, o.ReadyDate, o.DeliverByDate,
            At(o.PickupLocationId), At(o.DropLocationId), o.DeliveryWindowFrom, o.DeliveryWindowTo,
            o.Priority, o.ProductCategory, o.Handling, o.IsHazardous, o.IsStackable, o.LongestItemM, o.PickupWindowFrom, o.PickupWindowTo)).ToList();
    }

    /// <summary>The tenant's pairs of product categories that must not share a vehicle.</summary>
    public async Task<List<CompatibilityPair>> IncompatiblePairsAsync(CancellationToken cancellationToken) =>
        await db.CompatibilityRules.AsNoTracking().Select(r => new CompatibilityPair(r.CategoryA, r.CategoryB, r.Reason)).ToListAsync(cancellationToken);
}
