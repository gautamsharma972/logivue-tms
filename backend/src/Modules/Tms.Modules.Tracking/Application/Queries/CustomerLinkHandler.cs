using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Application.Queries;

/// <summary>
/// Secure links for customers. The link is a long random secret that is shown once and stored only as a hash; it opens one shipment, read-only, until it expires or is revoked. The
/// public view says where the goods are and when they should arrive and nothing about cost, carriers' internals, exceptions or notes.
/// </summary>
internal sealed class CustomerLinkHandler(TrackingDbContext db, TrackingAccess access, ITrackingSettings settings, IAmbientUserContext ambient, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<CreatedLinkDto>> CreateAsync(CreateLinkRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageLinks)
        {
            return TrackingAccess.Forbidden;
        }

        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == request.ShipmentId || s.ShipmentId == request.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return TrackingAccess.ShipmentNotFound;
        }

        var rules = await settings.GetAsync<LinkSetting>(TrackingSettingKeys.Links, cancellationToken);
        var days = request.ValidDays ?? rules.DefaultValidityDays;
        if (days < 1 || days > rules.MaxValidityDays)
        {
            return Error.Validation("tracking.link_validity", $"A link can be valid for 1 to {rules.MaxValidityDays} days.");
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow();
        var link = CustomerTrackingLink.Create(shipment, Clean(request.CustomerReference), Clean(request.CustomerName) ?? shipment.CustomerName, Hash(token), now.AddDays(days));
        db.Links.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedLinkDto(ToDto(link, now), token, $"/track/{token}");
    }

    public async Task<Result<IReadOnlyList<CustomerLinkDto>>> ListAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        if (!access.CanManageLinks && !access.CanRead)
        {
            return TrackingAccess.Forbidden;
        }

        var now = clock.GetUtcNow();
        var rows = await db.Links.AsNoTracking().Where(l => l.TrackedShipmentId == shipmentId || db.Shipments.Any(s => s.ShipmentId == shipmentId && s.Id == l.TrackedShipmentId)).OrderByDescending(l => l.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(l => ToDto(l, now)).ToList();
    }

    public async Task<Result<CustomerLinkDto>> RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanManageLinks)
        {
            return TrackingAccess.Forbidden;
        }

        var link = await db.Links.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (link is null)
        {
            return TrackingAccess.LinkNotFound;
        }

        link.Revoke(user.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(link, clock.GetUtcNow());
    }

    /// <summary>What a customer sees when they open the link. Anyone holding the secret may; there is no sign-in, so every failure looks the same.</summary>
    public async Task<Result<CustomerTrackingDto>> ViewAsync(string token, CancellationToken cancellationToken)
    {
        var notValid = Error.NotFound("tracking.link_invalid", "This tracking link is not valid.");
        if (token.Length is < 32 or > 64 || token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            return notValid;
        }

        var hash = Hash(token);
        // The one place a tenant's data is read before anyone has signed in: found by the hash of the secret alone, then everything else is read as that tenant.
        var link = await db.Links.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.TokenHash == hash, cancellationToken);
        var now = clock.GetUtcNow();
        if (link is null || link.StatusAt(now) != LinkStatus.Active)
        {
            return notValid;
        }

        ambient.RunAs(link.TenantId, null, "customer-link");
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.Stops).FirstOrDefaultAsync(s => s.Id == link.TrackedShipmentId, cancellationToken);
        if (shipment is null)
        {
            return notValid;
        }

        var route = await db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.TrackedShipmentId == shipment.Id, cancellationToken);
        var tracked = await db.Links.FirstAsync(l => l.Id == link.Id, cancellationToken);
        tracked.Viewed(now);
        await db.SaveChangesAsync(cancellationToken);
        return View(shipment, route, link, now);
    }

    private static CustomerTrackingDto View(TrackedShipment s, RoutePlan? route, CustomerTrackingLink link, DateTimeOffset now)
    {
        var stops = s.Stops.OrderBy(x => x.Sequence).ToList();
        var drops = stops.Where(x => x.Kind == StopKind.Drop).ToList();
        var mine = link.CustomerReference is { } reference ? drops.Where(x => string.Equals(x.CustomerReference, reference, StringComparison.OrdinalIgnoreCase)).ToList() : drops;
        var target = mine.Count > 0 ? mine.Last() : drops.LastOrDefault() ?? stops.LastOrDefault();
        var firstPickup = stops.FirstOrDefault(x => x.Kind == StopKind.Pickup) ?? stops.FirstOrDefault();
        var delivered = s.Delivery == TrackedDeliveryStatus.Delivered || s.Execution is ExecutionStatus.Delivered or ExecutionStatus.Completed;
        var departed = firstPickup?.Status is StopStatus.Departed || s.Execution is ExecutionStatus.Departed or ExecutionStatus.InTransit or ExecutionStatus.ApproachingDestination or ExecutionStatus.ArrivedDestination or ExecutionStatus.Delivered or ExecutionStatus.Completed;
        var arrived = target?.Status is StopStatus.Arrived or StopStatus.Departed || s.Execution == ExecutionStatus.ArrivedDestination || delivered;
        var inTransit = departed && !arrived;
        var live = s.LastCapturedAt is { } at && (now - at).TotalHours <= 6 && !delivered && s.Tracking != TrackingHealth.NotStarted;

        var steps = new List<CustomerStepDto>
        {
            new("Dispatched", "done", s.StartedAt ?? s.CreatedAt),
            new(firstPickup is null ? "Departed" : $"Departed {firstPickup.City ?? firstPickup.Name}", departed ? "done" : "pending", firstPickup?.DepartedAt),
            new("In transit", inTransit ? "current" : departed ? "done" : "pending", null),
        };
        if (inTransit && live)
        {
            steps.Add(new CustomerStepDto(Label(s, stops, target), "current", s.LastCapturedAt));
        }

        steps.Add(new CustomerStepDto($"Arrived {target?.City ?? target?.Name ?? "at destination"}", arrived ? "done" : "pending", target?.ArrivedAt));
        steps.Add(new CustomerStepDto("Delivered", delivered ? "done" : "pending", delivered ? s.CompletedAt ?? target?.DepartedAt : null));

        var etaAt = delivered ? null : target?.EtaAt is { } stopEta && s.EtaOverrideAt is null ? stopEta : s.CurrentEtaAt;
        var risk = delivered ? "Delivered" : s.Risk switch { RiskStatus.OnTime => "On time", RiskStatus.AtRisk => "May be delayed", RiskStatus.Delayed or RiskStatus.SeverelyDelayed => "Delayed", _ => "Not yet known" };
        var status = delivered ? "Delivered" : arrived ? "Arrived" : inTransit ? "In transit" : departed ? "Departed" : "Dispatched";
        var points = route?.Points ?? [];
        var stride = Math.Max(1, points.Count / 300);
        return new CustomerTrackingDto(
            s.ShipmentReference, firstPickup?.City ?? s.OriginName, target?.City ?? s.DestinationName, status, steps, live ? Math.Round(s.LastLatitude ?? 0, 4) : null, live ? Math.Round(s.LastLongitude ?? 0, 4) : null,
            live ? s.LastCapturedAt : null, live ? Label(s, stops, target) : null, etaAt, target?.WindowStart, target?.WindowEnd ?? target?.PlannedArrival, risk, delivered, delivered ? s.CompletedAt : null,
            points.Where((_, i) => i % stride == 0 || i == points.Count - 1).Select(p => new[] { p.Latitude, p.Longitude }).ToList(), now);
    }

    /// <summary>"Near Lonavala": a place name the customer knows, from the stops, never raw coordinates and never a claim the system cannot back.</summary>
    private static string Label(TrackedShipment s, List<ShipmentStop> stops, ShipmentStop? target)
    {
        if (s.LastLatitude is not { } lat || s.LastLongitude is not { } lon)
        {
            return "Location not available";
        }

        var here = new GeoPoint(lat, lon);
        var near = stops.Where(x => x.Point is not null).Select(x => (Stop: x, Km: Geo.DistanceKm(here, x.Point!.Value))).OrderBy(x => x.Km).FirstOrDefault();
        if (near.Stop is not null && near.Km <= 8)
        {
            return $"Near {near.Stop.City ?? near.Stop.Name}";
        }

        var left = target?.Point is { } t ? Geo.DistanceKm(here, t) : (double?)null;
        return left is { } km && target is not null ? $"About {Math.Round(km / 5) * 5:0} km from {target.City ?? target.Name}" : "On the way";
    }

    private static CustomerLinkDto ToDto(CustomerTrackingLink l, DateTimeOffset now) =>
        new(l.Id, l.TrackedShipmentId, l.ShipmentReference, l.CustomerReference, l.CustomerName, l.CreatedAt, l.ExpiresAt, l.StatusAt(now), l.RevokedAt, l.ViewCount, l.LastViewedAt);

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
