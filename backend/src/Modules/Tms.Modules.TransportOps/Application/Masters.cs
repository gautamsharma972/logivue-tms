using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.India;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

public sealed record CustomerDto(
    Guid Id, string Code, string Name, string? Gstin, string? BillingLine1, string? BillingCity, string? BillingState, string? BillingPincode, string? BillingContactName, string? BillingContactEmail,
    string? BillingContactPhone, string? DeliveryContactName, string? DeliveryContactPhone, string? CommercialContactName, string? CommercialContactEmail, int DueDays, string? BillingRequirements,
    string? PortalMethod, string? PortalReference, string? RequiredRequestFields, ActiveStatus Status, long Version);

public sealed record SaveCustomerRequest(
    string Code, string Name, string? Gstin, string? BillingLine1, string? BillingCity, string? BillingState, string? BillingPincode, string? BillingContactName, string? BillingContactEmail,
    string? BillingContactPhone, string? DeliveryContactName, string? DeliveryContactPhone, string? CommercialContactName, string? CommercialContactEmail, int DueDays, string? BillingRequirements,
    string? PortalMethod, string? PortalReference, string? RequiredRequestFields, ActiveStatus Status, long Version = 0);

internal sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9_-]+$").WithMessage("Use letters, digits, - and _ only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Gstin).Must(g => IndianIdentifiers.IsValidGstin(g)).When(x => !string.IsNullOrWhiteSpace(x.Gstin)).WithMessage("Enter a valid 15-character GSTIN.");
        RuleFor(x => x.BillingContactEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.BillingContactEmail));
        RuleFor(x => x.CommercialContactEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.CommercialContactEmail));
        RuleFor(x => x.BillingPincode).Must(p => IndianIdentifiers.IsValidPincode(p)).When(x => !string.IsNullOrWhiteSpace(x.BillingPincode)).WithMessage("Enter a 6-digit pincode.");
        RuleFor(x => x.DueDays).InclusiveBetween(0, 365);
        RuleFor(x => x.BillingRequirements).MaximumLength(1000);
    }
}

internal sealed class CustomerHandler(TransportOpsDbContext db, OpsAccess access)
{
    public async Task<Result<PagedResult<CustomerDto>>> ListAsync(string? search, ActiveStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c => c.Code.Contains(s) || c.Name.Contains(s) || (c.Gstin != null && c.Gstin.Contains(s)));
        }

        if (status is { } st)
        {
            query = query.Where(c => c.Status == st);
        }

        var paged = await query.OrderBy(c => c.Name).ToPagedAsync(page, pageSize, cancellationToken);
        return new PagedResult<CustomerDto>(paged.Items.Select(ToDto).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<Result<CustomerDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return customer is null ? OpsErrors.CustomerNotFound : ToDto(customer);
    }

    public async Task<Result<CustomerDto>> CreateAsync(SaveCustomerRequest request, CancellationToken cancellationToken)
    {
        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Customers.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return Error.Conflict("ops.customer_code_taken", "A customer with this code already exists.");
        }

        var customer = Customer.Create(access.TenantId!.Value, code, request.Name);
        Apply(customer, request);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(customer);
    }

    public async Task<Result<CustomerDto>> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (customer is null)
        {
            return OpsErrors.CustomerNotFound;
        }

        db.Entry(customer).Property(c => c.Version).OriginalValue = request.Version;
        Apply(customer, request);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OpsErrors.ConcurrencyConflict;
        }

        return ToDto(customer);
    }

    private static void Apply(Customer c, SaveCustomerRequest r) =>
        c.Update(r.Name, r.Gstin, r.BillingLine1, r.BillingCity, r.BillingState, r.BillingPincode, r.BillingContactName, r.BillingContactEmail, r.BillingContactPhone, r.DeliveryContactName,
            r.DeliveryContactPhone, r.CommercialContactName, r.CommercialContactEmail, r.DueDays, r.BillingRequirements, r.PortalMethod, r.PortalReference, r.RequiredRequestFields, r.Status);

    internal static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.Code, c.Name, c.Gstin, c.BillingLine1, c.BillingCity, c.BillingState, c.BillingPincode, c.BillingContactName, c.BillingContactEmail, c.BillingContactPhone, c.DeliveryContactName,
            c.DeliveryContactPhone, c.CommercialContactName, c.CommercialContactEmail, c.DueDays, c.BillingRequirements, c.PortalMethod, c.PortalReference, c.RequiredRequestFields, c.Status, c.Version);
}

public sealed record LocationDto(
    Guid Id, string Code, string Name, LocationKind Kind, Guid? CustomerId, string? CustomerName, string Line1, string City, string State, string? Pincode, double? Latitude, double? Longitude,
    int GeofenceRadiusM, string? AccessNotes, ActiveStatus Status, long Version);

public sealed record SaveLocationRequest(
    string Code, string Name, LocationKind Kind, Guid? CustomerId, string Line1, string City, string State, string? Pincode, double? Latitude, double? Longitude, int GeofenceRadiusM,
    string? AccessNotes, ActiveStatus Status, long Version = 0);

internal sealed class SaveLocationRequestValidator : AbstractValidator<SaveLocationRequest>
{
    public SaveLocationRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9_-]+$").WithMessage("Use letters, digits, - and _ only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Pincode).Must(p => IndianIdentifiers.IsValidPincode(p)).When(x => !string.IsNullOrWhiteSpace(x.Pincode)).WithMessage("Enter a 6-digit pincode.");
        RuleFor(x => x.GeofenceRadiusM).InclusiveBetween(20, 5000).WithMessage("The geofence radius must be between 20 and 5000 metres.");
        RuleFor(x => x).Must(x => (x.Latitude is null) == (x.Longitude is null)).WithMessage("Enter both latitude and longitude, or neither.").WithName("Coordinates");
        RuleFor(x => x).Must(x => x.Latitude is null || x.Longitude is null || OpsLocation.InIndia(x.Latitude.Value, x.Longitude.Value))
            .WithMessage("The coordinates are outside India. Check that latitude and longitude are not swapped.").WithName("Coordinates");
    }
}

internal sealed class LocationHandler(TransportOpsDbContext db, OpsAccess access)
{
    public async Task<Result<PagedResult<LocationDto>>> ListAsync(string? search, LocationKind? kind, Guid? customerId, ActiveStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Locations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(l => l.Code.Contains(s) || l.Name.Contains(s) || l.City.Contains(s));
        }

        if (kind is { } k)
        {
            query = query.Where(l => l.Kind == k);
        }

        if (customerId is { } c)
        {
            query = query.Where(l => l.CustomerId == c);
        }

        if (status is { } st)
        {
            query = query.Where(l => l.Status == st);
        }

        var paged = await query.OrderBy(l => l.Name).ToPagedAsync(page, pageSize, cancellationToken);
        var names = await CustomerNamesAsync(paged.Items.Select(l => l.CustomerId), cancellationToken);
        return new PagedResult<LocationDto>(paged.Items.Select(l => ToDto(l, names)).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<Result<LocationDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return location is null ? OpsErrors.LocationNotFound : ToDto(location, await CustomerNamesAsync([location.CustomerId], cancellationToken));
    }

    public async Task<Result<LocationDto>> CreateAsync(SaveLocationRequest request, CancellationToken cancellationToken)
    {
        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Locations.AnyAsync(l => l.Code == code, cancellationToken))
        {
            return Error.Conflict("ops.location_code_taken", "A location with this code already exists.");
        }

        if (request.CustomerId is { } cid && !await db.Customers.AnyAsync(c => c.Id == cid, cancellationToken))
        {
            return OpsErrors.CustomerNotFound;
        }

        var location = OpsLocation.Create(access.TenantId!.Value, code, request.Name, request.Kind);
        Apply(location, request);
        db.Locations.Add(location);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(location, await CustomerNamesAsync([location.CustomerId], cancellationToken));
    }

    public async Task<Result<LocationDto>> UpdateAsync(Guid id, SaveLocationRequest request, CancellationToken cancellationToken)
    {
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return OpsErrors.LocationNotFound;
        }

        if (request.CustomerId is { } cid && !await db.Customers.AnyAsync(c => c.Id == cid, cancellationToken))
        {
            return OpsErrors.CustomerNotFound;
        }

        db.Entry(location).Property(l => l.Version).OriginalValue = request.Version;
        Apply(location, request);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OpsErrors.ConcurrencyConflict;
        }

        return ToDto(location, await CustomerNamesAsync([location.CustomerId], cancellationToken));
    }

    private static void Apply(OpsLocation l, SaveLocationRequest r) =>
        l.Update(r.Name, r.Kind, r.CustomerId, r.Line1, r.City, r.State, r.Pincode, r.Latitude, r.Longitude, r.GeofenceRadiusM, r.AccessNotes, r.Status);

    private async Task<Dictionary<Guid, string>> CustomerNamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return wanted.Count == 0 ? [] : await db.Customers.AsNoTracking().Where(c => wanted.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
    }

    internal static LocationDto ToDto(OpsLocation l, IReadOnlyDictionary<Guid, string> customers) =>
        new(l.Id, l.Code, l.Name, l.Kind, l.CustomerId, l.CustomerId is { } c && customers.TryGetValue(c, out var n) ? n : null, l.Line1, l.City, l.State, l.Pincode, l.Latitude, l.Longitude,
            l.GeofenceRadiusM, l.AccessNotes, l.Status, l.Version);
}
