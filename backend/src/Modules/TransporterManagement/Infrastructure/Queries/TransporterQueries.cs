using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Application.Alerts;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Queries;

/// <summary>EF Core projections for list and detail screens. Each list is one query, not one query per row.</summary>
internal sealed class TransporterQueries(TransporterDbContext db) : ITransporterQueries
{
    public async Task<PagedResult<TransporterListItem>> SearchTransportersAsync(TransporterSearch search, CancellationToken cancellationToken = default)
    {
        var page = Paging.NormalisePage(search.Page);
        var pageSize = Paging.NormalisePageSize(search.PageSize);

        var query = db.Transporters.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim();
            query = query.Where(t =>
                t.TransporterCode.Contains(term)
                || t.LegalName.Contains(term)
                || (t.TradeName != null && t.TradeName.Contains(term))
                || (t.Gstin != null && t.Gstin.Contains(term)));
        }

        if (search.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        if (search.TransporterTypeId is { } typeId)
        {
            query = query.Where(t => t.TransporterTypeId == typeId);
        }

        if (!string.IsNullOrWhiteSpace(search.City))
        {
            var city = search.City.Trim();
            query = query.Where(t => t.City == city);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(t => t.LegalName)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransporterListItem(
                t.Id,
                t.TransporterCode,
                t.LegalName,
                t.TradeName,
                db.TransporterTypes.Where(tt => tt.Id == t.TransporterTypeId).Select(tt => tt.Name).FirstOrDefault(),
                t.Status,
                t.City,
                t.State,
                t.PrimaryContactName,
                t.PrimaryContactPhone,
                db.TransporterVehicles.Count(v => v.TransporterId == t.Id && v.Status == RecordStatus.Active),
                db.TransporterLanes.Count(l => l.TransporterId == t.Id && l.Status == RecordStatus.Active)))
            .ToListAsync(cancellationToken);

        return new PagedResult<TransporterListItem>(items, page, pageSize, total);
    }

    public async Task<TransporterDetail?> GetTransporterAsync(long id, CancellationToken cancellationToken = default)
    {
        var transporter = await db.Transporters.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new
            {
                t.Id,
                t.TransporterCode,
                t.LegalName,
                t.TradeName,
                t.TransporterTypeId,
                TransporterType = db.TransporterTypes.Where(tt => tt.Id == t.TransporterTypeId).Select(tt => tt.Name).FirstOrDefault(),
                t.CompanyType,
                t.Pan,
                t.Gstin,
                t.RegistrationNumber,
                t.Address,
                t.City,
                t.State,
                t.Country,
                t.PrimaryContactName,
                t.PrimaryContactEmail,
                t.PrimaryContactPhone,
                t.Website,
                t.Status,
                ActiveVehicles = db.TransporterVehicles.Count(v => v.TransporterId == t.Id && v.Status == RecordStatus.Active),
                ActiveLanes = db.TransporterLanes.Count(l => l.TransporterId == t.Id && l.Status == RecordStatus.Active),
                t.CreatedAt,
                t.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (transporter is null)
        {
            return null;
        }

        var contacts = await db.TransporterContacts.AsNoTracking()
            .Where(c => c.TransporterId == id)
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.Name)
            .Select(c => new ContactDto(c.Id, c.Name, c.Designation, c.Email, c.Phone, c.ContactType, c.IsPrimary, c.IsActive))
            .ToListAsync(cancellationToken);

        var branches = await db.TransporterBranches.AsNoTracking()
            .Where(b => b.TransporterId == id)
            .OrderBy(b => b.BranchCode)
            .Select(b => new BranchDto(b.Id, b.BranchCode, b.BranchName, b.Address, b.City, b.State, b.Latitude, b.Longitude, b.ContactName, b.ContactPhone, b.IsActive))
            .ToListAsync(cancellationToken);

        var capabilities = await (
                from c in db.TransporterCapabilities.AsNoTracking()
                join type in db.CapabilityTypes.AsNoTracking() on c.CapabilityTypeId equals type.Id
                where c.TransporterId == id
                orderby type.Name
                select new CapabilityDto(c.Id, c.CapabilityTypeId, type.Code, type.Name, c.EffectiveFrom, c.EffectiveTo, c.Status))
            .ToListAsync(cancellationToken);

        return new TransporterDetail(
            transporter.Id,
            transporter.TransporterCode,
            transporter.LegalName,
            transporter.TradeName,
            transporter.TransporterTypeId,
            transporter.TransporterType,
            transporter.CompanyType,
            transporter.Pan,
            transporter.Gstin,
            transporter.RegistrationNumber,
            transporter.Address,
            transporter.City,
            transporter.State,
            transporter.Country,
            transporter.PrimaryContactName,
            transporter.PrimaryContactEmail,
            transporter.PrimaryContactPhone,
            transporter.Website,
            transporter.Status,
            transporter.ActiveVehicles,
            transporter.ActiveLanes,
            contacts,
            branches,
            capabilities,
            transporter.CreatedAt,
            transporter.UpdatedAt);
    }

    public async Task<PagedResult<VehicleDto>> SearchVehiclesAsync(long transporterId, VehicleSearch search, CancellationToken cancellationToken = default)
    {
        var page = Paging.NormalisePage(search.Page);
        var pageSize = Paging.NormalisePageSize(search.PageSize);

        var query = db.TransporterVehicles.AsNoTracking().Where(v => v.TransporterId == transporterId);

        if (search.AvailabilityStatus is { } availability)
        {
            query = query.Where(v => v.AvailabilityStatus == availability);
        }

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Trim();
            query = query.Where(v => v.RegistrationNumber.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(v => v.RegistrationNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new VehicleDto(v.Id, v.TransporterId, v.RegistrationNumber, v.VehicleTypeReference, v.PayloadCapacityKg,
                v.UsableVolumeM3, v.LengthM, v.WidthM, v.HeightM, v.OwnershipType, v.AvailabilityStatus, v.CurrentLocationReference, v.Status))
            .ToListAsync(cancellationToken);

        return new PagedResult<VehicleDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<LaneDto>> SearchLanesAsync(long transporterId, LaneSearch search, CancellationToken cancellationToken = default)
    {
        var page = Paging.NormalisePage(search.Page);
        var pageSize = Paging.NormalisePageSize(search.PageSize);

        var query = db.TransporterLanes.AsNoTracking().Where(l => l.TransporterId == transporterId);

        if (search.OriginLocationReference is { } origin)
        {
            query = query.Where(l => l.OriginLocationReference == origin);
        }

        if (search.DestinationLocationReference is { } destination)
        {
            query = query.Where(l => l.DestinationLocationReference == destination);
        }

        if (!string.IsNullOrWhiteSpace(search.ServiceType))
        {
            var serviceType = search.ServiceType.Trim().ToUpperInvariant();
            query = query.Where(l => l.ServiceType == serviceType);
        }

        if (search.Status is { } status)
        {
            query = query.Where(l => l.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(l => l.ServiceType)
            .ThenBy(l => l.OriginLocationReference)
            .ThenBy(l => l.DestinationLocationReference)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LaneDto(l.Id, l.TransporterId, l.OriginLocationReference, l.DestinationLocationReference, l.ServiceType,
                l.VehicleTypeReference, l.TransitSlaMinutes, l.EffectiveFrom, l.EffectiveTo, l.Status))
            .ToListAsync(cancellationToken);

        return new PagedResult<LaneDto>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<LookupDto>> GetTransporterTypesAsync(CancellationToken cancellationToken = default) =>
        await db.TransporterTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new LookupDto(t.Id, t.Code, t.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupDto>> GetServiceTypesAsync(CancellationToken cancellationToken = default) =>
        await db.ServiceTypes.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new LookupDto(s.Id, s.Code, s.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupDto>> GetCapabilityTypesAsync(CancellationToken cancellationToken = default) =>
        await db.CapabilityTypes.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new LookupDto(c.Id, c.Code, c.Name))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<AlertDto>> SearchAlertsAsync(AlertSearch search, CancellationToken cancellationToken = default)
    {
        var page = Paging.NormalisePage(search.Page);
        var pageSize = Paging.NormalisePageSize(search.PageSize);

        var query = db.Alerts.AsNoTracking().AsQueryable();

        if (search.TransporterId is { } transporterId)
        {
            query = query.Where(a => a.TransporterId == transporterId);
        }

        if (search.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        if (search.Severity is { } severity)
        {
            query = query.Where(a => a.Severity == severity);
        }

        if (!string.IsNullOrWhiteSpace(search.AlertType))
        {
            var alertType = search.AlertType.Trim().ToUpperInvariant();
            query = query.Where(a => a.AlertType == alertType);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.DueAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AlertDto(a.Id, a.AlertType, a.Severity, a.TransporterId, a.LoadReference, a.EntityType, a.EntityId,
                a.Message, a.CreatedAt, a.DueAt, a.Status, a.AcknowledgedAt, a.ResolvedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AlertDto>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<LookupDto>> GetDocumentTypesAsync(CancellationToken cancellationToken = default) =>
        await db.DocumentTypes.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new LookupDto(d.Id, d.Code, d.Name))
            .ToListAsync(cancellationToken);
}
