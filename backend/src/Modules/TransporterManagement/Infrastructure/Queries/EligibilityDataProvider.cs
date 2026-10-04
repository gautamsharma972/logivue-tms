using LogiVue.Tms.TransporterManagement.Application.Eligibility;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Queries;

/// <summary>
/// Bulk loads for eligibility. Lanes and rates are filtered to the requested lane in the database. Fleet, documents,
/// holdings and KPIs are loaded only for the transporters that serve the lane.
/// </summary>
internal sealed class EligibilityDataProvider(TransporterDbContext db) : IEligibilityDataProvider
{
    public async Task<EligibilityData> LoadAsync(TransporterSelectionRequest request, int performanceWindowDays, CancellationToken cancellationToken = default)
    {
        var service = request.ServiceType.Trim().ToUpperInvariant();
        var origin = request.OriginLocationId;
        var destination = request.DestinationLocationId;
        var requestDate = request.RequiredDate;
        var windowStart = requestDate.AddDays(-performanceWindowDays);

        // Every transporter is read, so one that does not serve the lane still gets a clear "lane not configured" reason.
        // The master list is small; the heavier fleet, document and KPI tables are narrowed to the candidates below.
        var transporters = await db.Transporters.AsNoTracking().ToListAsync(cancellationToken);

        var lanes = await db.TransporterLanes.AsNoTracking()
            .Where(l => l.OriginLocationReference == origin && l.DestinationLocationReference == destination && l.ServiceType == service)
            .ToListAsync(cancellationToken);

        var rates = await db.TransporterRates.AsNoTracking()
            .Where(r => r.OriginLocationReference == origin && r.DestinationLocationReference == destination && r.ServiceType == service)
            .ToListAsync(cancellationToken);

        // Fleet, documents, holdings and KPIs are needed only for transporters that serve the lane. Narrowing them here
        // keeps the cost proportional to the candidates rather than to the whole transporter base.
        var candidates = lanes.Select(l => l.TransporterId).Distinct().ToList();

        var vehicles = candidates.Count == 0 ? new List<TransporterVehicle>() : await db.TransporterVehicles.AsNoTracking()
            .Where(v => candidates.Contains(v.TransporterId) && v.Status == RecordStatus.Active
                && (request.VehicleTypeId == null || v.VehicleTypeReference == request.VehicleTypeId))
            .ToListAsync(cancellationToken);

        var capabilities = candidates.Count == 0 ? new List<CapabilityHolding>() : await (
                from c in db.TransporterCapabilities.AsNoTracking()
                join type in db.CapabilityTypes.AsNoTracking() on c.CapabilityTypeId equals type.Id
                where candidates.Contains(c.TransporterId)
                select new CapabilityHolding(c.TransporterId, type.Code, c.EffectiveFrom, c.EffectiveTo, c.Status))
            .ToListAsync(cancellationToken);

        var documents = candidates.Count == 0 ? new List<TransporterDocument>() : await db.TransporterDocuments.AsNoTracking()
            .Where(d => candidates.Contains(d.TransporterId)).ToListAsync(cancellationToken);
        var documentTypes = await db.DocumentTypes.AsNoTracking().ToListAsync(cancellationToken);
        var rules = await db.PlanningRules.AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);

        var kpis = candidates.Count == 0 ? new List<PerformanceKpi>() : await db.PerformanceKpis.AsNoTracking()
            .Where(k => candidates.Contains(k.TransporterId) && k.PeriodEnd >= windowStart && k.PeriodEnd <= requestDate)
            .ToListAsync(cancellationToken);

        return new EligibilityData(transporters, lanes, rates, vehicles, capabilities, documents, documentTypes, rules, kpis);
    }
}
