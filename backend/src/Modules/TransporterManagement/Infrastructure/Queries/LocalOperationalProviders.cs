using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Queries;

/// <summary>
/// Claims summary from this module's local claim records. Replace with the Claims module's adapter when it exists;
/// the KPI pipeline depends only on <see cref="ITransporterClaimsProvider"/>.
/// </summary>
internal sealed class LocalClaimsProvider(TransporterDbContext db) : ITransporterClaimsProvider
{
    public async Task<TransporterClaimsSummaryDto> GetClaimsSummaryAsync(long transporterId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var startDate = DateOnly.FromDateTime(from);
        var endDate = DateOnly.FromDateTime(to);

        // A shipment counts once it is delivered in the period, so the claims rate uses the same delivery basis as the other KPIs.
        var shipments = await db.LoadExecutions.AsNoTracking()
            .CountAsync(e => e.TransporterId == transporterId && e.ActualDeliveryAt != null
                && e.ActualDeliveryAt >= from && e.ActualDeliveryAt < to, cancellationToken);

        var claims = await db.Claims.AsNoTracking()
            .Where(c => c.TransporterId == transporterId && c.ClaimDate >= startDate && c.ClaimDate < endDate)
            .Select(c => new { c.ClaimType, c.ClaimValue, c.Status })
            .ToListAsync(cancellationToken);

        // With no shipments the rate is left at zero here. The KPI itself is Not Measurable, from its counts.
        var rate = shipments == 0 ? 0m : Math.Round(claims.Count * 100m / shipments, 2);

        return new TransporterClaimsSummaryDto(
            TransporterId: transporterId,
            TotalShipments: shipments,
            ClaimsCount: claims.Count,
            ClaimsRatePct: rate,
            ClaimValue: claims.Sum(c => c.ClaimValue),
            DamageCount: claims.Count(c => c.ClaimType == Domain.Claims.ClaimType.Damage),
            ShortageCount: claims.Count(c => c.ClaimType == Domain.Claims.ClaimType.Shortage),
            LossTheftCount: claims.Count(c => c.ClaimType == Domain.Claims.ClaimType.LossTheft),
            OpenClaims: claims.Count(c => c.Status == Domain.Claims.ClaimStatus.Open),
            ResolvedClaims: claims.Count(c => c.Status == Domain.Claims.ClaimStatus.Resolved));
    }
}

internal sealed class LocalCostProvider(TransporterDbContext db) : ITransporterCostProvider
{
    public async Task<TransporterCostSummaryDto> GetCostSummaryAsync(long transporterId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var startDate = DateOnly.FromDateTime(from);
        var endDate = DateOnly.FromDateTime(to);
        var rows = await db.LoadCosts.AsNoTracking()
            .Where(c => c.TransporterId == transporterId && c.ServiceDate >= startDate && c.ServiceDate < endDate)
            .Select(c => new { c.AgreedAmount, c.InvoicedAmount })
            .ToListAsync(cancellationToken);

        return new TransporterCostSummaryDto(
            transporterId,
            LoadsWithCost: rows.Count,
            OnBudgetLoads: rows.Count(r => r.InvoicedAmount <= r.AgreedAmount),
            AgreedAmount: rows.Sum(r => r.AgreedAmount),
            InvoicedAmount: rows.Sum(r => r.InvoicedAmount));
    }
}

internal sealed class LocalAvailabilityProvider(TransporterDbContext db) : ITransporterAvailabilityProvider
{
    public async Task<TransporterAvailabilitySummaryDto> GetAvailabilitySummaryAsync(long transporterId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var startDate = DateOnly.FromDateTime(from);
        var endDate = DateOnly.FromDateTime(to);
        var rows = await db.CapacityDays.AsNoTracking()
            .Where(c => c.TransporterId == transporterId && c.Date >= startDate && c.Date < endDate)
            .Select(c => new { c.VehiclesCommitted, c.VehiclesAvailable })
            .ToListAsync(cancellationToken);

        return new TransporterAvailabilitySummaryDto(
            transporterId,
            VehicleDaysCommitted: rows.Sum(r => r.VehiclesCommitted),
            VehicleDaysAvailable: rows.Sum(r => r.VehiclesAvailable));
    }
}
