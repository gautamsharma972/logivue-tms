using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Application.Deliveries;

internal static class DeliveryQueries
{
    public static IQueryable<Delivery> WithAll(this IQueryable<Delivery> q) =>
        q.Include(d => d.Items).Include(d => d.Attempts).Include(d => d.Events).Include(d => d.Discrepancies);
}

internal sealed class ListDeliveriesHandler(DeliveriesDbContext db, DeliveryAccess access, DeliveryMapper mapper)
{
    public async Task<Result<PagedResult<DeliverySummaryDto>>> HandleAsync(ListDeliveriesQuery query, CancellationToken cancellationToken)
    {
        var rows = db.Deliveries.AsNoTracking().Include(d => d.Discrepancies).AsQueryable();
        if (access.IsVendor)
        {
            if (access.VendorTransporterId is not { } mine || !access.CanSeeTransporter(mine))
            {
                return DeliveryAccess.Forbidden;
            }

            rows = rows.Where(d => d.TransporterId == mine);
        }
        else if (!access.CanRead)
        {
            return DeliveryAccess.Forbidden;
        }
        else if (query.TransporterId is { } transporterId)
        {
            rows = rows.Where(d => d.TransporterId == transporterId);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(d => d.Status == status);
        }

        if (query.PodStatus is { } pod)
        {
            rows = pod == PodStatus.Pending
                ? rows.Where(d => (d.Status == DeliveryStatus.Delivered || d.Status == DeliveryStatus.PartiallyDelivered) && !db.Pods.Any(p => p.DeliveryId == d.Id && p.IsCurrent))
                : rows.Where(d => db.Pods.Any(p => p.DeliveryId == d.Id && p.IsCurrent && p.Status == pod));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(d => d.Number.Contains(term) || (d.ShipmentReference != null && d.ShipmentReference.Contains(term)) || d.CustomerName.Contains(term)
                || (d.VehicleReference != null && d.VehicleReference.Contains(term)) || (d.LrNumber != null && d.LrNumber.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var customer = query.Customer.Trim();
            rows = rows.Where(d => d.CustomerName.Contains(customer) || (d.CustomerReference != null && d.CustomerReference.Contains(customer)));
        }

        if (query.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), Clock.India);
            rows = rows.Where(d => d.PlannedDeliveryAt >= start);
        }

        if (query.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), Clock.India);
            rows = rows.Where(d => d.PlannedDeliveryAt < end);
        }

        if (query.HasException is { } hasException)
        {
            rows = hasException
                ? rows.Where(d => db.Exceptions.Any(e => e.DeliveryId == d.Id && e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed))
                : rows.Where(d => !db.Exceptions.Any(e => e.DeliveryId == d.Id && e.Status != ExceptionStatus.Resolved && e.Status != ExceptionStatus.Closed));
        }

        if (query.HasDiscrepancy is { } hasDiscrepancy)
        {
            rows = hasDiscrepancy ? rows.Where(d => d.Discrepancies.Any()) : rows.Where(d => !d.Discrepancies.Any());
        }

        var page = await rows.OrderByDescending(d => d.PlannedDeliveryAt).ThenByDescending(d => d.Number).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<DeliverySummaryDto>(await mapper.SummariesAsync(page.Items.ToList(), cancellationToken), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetDeliveryHandler(DeliveriesDbContext db, DeliveryAccess access, DeliveryMapper mapper)
{
    public async Task<Result<DeliveryDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.AsNoTracking().WithAll().AsSplitQuery().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return delivery is null || !access.CanSee(delivery) ? DeliveryAccess.DeliveryNotFound : await mapper.ToDtoAsync(delivery, cancellationToken);
    }
}

internal sealed class SaveDeliveryHandler(DeliveriesDbContext db, DeliveryAccess access, DeliveryMapper mapper, ISequenceGenerator sequences, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<DeliveryDto>> CreateAsync(SaveDeliveryRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage || user.TenantId is not { } tenantId)
        {
            return DeliveryAccess.Forbidden;
        }

        var items = (request.Items ?? []).Select(i => new Delivery.ItemInput(i.Sku, i.Description, i.OrderedQuantity, i.DispatchedQuantity ?? i.OrderedQuantity, i.UnitOfMeasure)).ToList();
        var number = $"DLV-{await sequences.NextAsync(tenantId, "delivery", cancellationToken):D5}";
        var created = Delivery.Create(tenantId, number, Header(request), items, access.Actor(null), clock.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Deliveries.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(created.Value, cancellationToken);
    }

    public async Task<Result<DeliveryDto>> UpdateAsync(Guid id, SaveDeliveryRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return DeliveryAccess.Forbidden;
        }

        if (request.Version is null)
        {
            return Error.Validation("deliveries.version_required", "The current version of the delivery is required.");
        }

        var delivery = await db.Deliveries.WithAll().AsSplitQuery().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (delivery is null)
        {
            return DeliveryAccess.DeliveryNotFound;
        }

        db.Entry(delivery).Property(d => d.Version).OriginalValue = request.Version.Value;
        var updated = delivery.Update(Header(request), access.Actor(null), clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(delivery, cancellationToken);
    }

    internal static Delivery.Header Header(SaveDeliveryRequest r) => new(
        null, r.ShipmentReference, null, r.OrderReference, r.LoadReference, r.TripReference, r.LrNumber, r.Sequence, r.TransporterId, r.TransporterReference, r.VehicleId,
        r.VehicleReference, r.DriverName, r.CustomerReference, r.CustomerName ?? string.Empty, r.CustomerPhone, r.CustomerEmail, r.OriginReference, r.DestinationReference,
        r.DestinationAddress, r.CustomerLatitude, r.CustomerLongitude, r.GeofenceRadiusM, r.PlannedDeliveryAt, r.WindowStart, r.WindowEnd);
}

internal sealed class AssignDeliveryHandler(DeliveriesDbContext db, DeliveryAccess access, DeliveryMapper mapper, ITransporterDirectory transporters, IFleetDirectory fleet, TimeProvider clock)
{
    public async Task<Result<DeliveryDto>> HandleAsync(Guid id, AssignDeliveryRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return DeliveryAccess.Forbidden;
        }

        var delivery = await db.Deliveries.WithAll().AsSplitQuery().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (delivery is null)
        {
            return DeliveryAccess.DeliveryNotFound;
        }

        var found = await transporters.GetAsync([request.TransporterId], cancellationToken);
        if (!found.TryGetValue(request.TransporterId, out var transporter))
        {
            return Error.NotFound("deliveries.transporter_not_found", "That transporter was not found.");
        }

        var vehicle = request.VehicleId is { } vehicleId ? await fleet.GetVehicleAsync(vehicleId, cancellationToken) : null;
        if (request.VehicleId is not null && (vehicle is null || vehicle.TransporterId != request.TransporterId))
        {
            return Error.Validation("deliveries.vehicle_not_theirs", "That vehicle does not belong to the transporter.");
        }

        var assigned = delivery.Assign(request.TransporterId, transporter.LegalName, vehicle?.Id, vehicle?.RegistrationNumber ?? request.VehicleReference, request.DriverName, access.Actor(null), clock.GetUtcNow());
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(delivery, cancellationToken);
    }
}
