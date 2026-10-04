using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Performance;

public sealed record ContactDto(Guid Id, Guid TransporterId, string Name, string? Designation, string? Email, string? Phone, string ContactType, bool IsPrimary, bool IsActive, long Version);

public sealed record SaveContactRequest(string Name, string? Designation, string? Email, string? Phone, string ContactType, bool IsPrimary, bool IsActive = true, long? Version = null);

public sealed record BranchDto(
    Guid Id, Guid TransporterId, string Code, string Name, string? Address, string? City, string? State, double? Latitude, double? Longitude, string? ContactName, string? ContactPhone,
    bool IsActive, long Version);

public sealed record SaveBranchRequest(
    string Code, string Name, string? Address, string? City, string? State, double? Latitude, double? Longitude, string? ContactName, string? ContactPhone, bool IsActive = true, long? Version = null);

/// <summary>Extra contacts and depots of a transporter. Staff with transporter-manage rights maintain them; a vendor maintains its own (the same rule as its profile and fleet).</summary>
internal sealed class ContactHandler(TransportersDbContext db, TransporterAccess access, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<ContactDto>>> ListAsync(Guid transporterId, CancellationToken cancellationToken)
    {
        if (access.Check(transporterId, AccessLevel.Read) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var items = await db.Contacts.AsNoTracking().Where(c => c.TransporterId == transporterId).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<Result<ContactDto>> AddAsync(Guid transporterId, SaveContactRequest request, CancellationToken cancellationToken)
    {
        var found = await db.FindTransporterAsync(access, transporterId, AccessLevel.Manage, cancellationToken);
        if (found.IsFailure || user.TenantId is not { } tenantId)
        {
            return found.IsFailure ? found.Error : TransporterAccess.NotFound;
        }

        var contact = TransporterContact.Create(tenantId, transporterId, request.Name, request.Designation, request.Email, request.Phone, request.ContactType, request.IsPrimary);
        if (contact.IsFailure)
        {
            return contact.Error;
        }

        if (request.IsPrimary)
        {
            await ClearPrimaryAsync(transporterId, null, cancellationToken);
        }

        db.Contacts.Add(contact.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(contact.Value);
    }

    public async Task<Result<ContactDto>> UpdateAsync(Guid contactId, SaveContactRequest request, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null || access.Check(contact.TransporterId, AccessLevel.Manage).IsFailure)
        {
            return Error.NotFound("contacts.not_found", "Contact not found.");
        }

        if (request.Version is { } version)
        {
            db.Entry(contact).Property(c => c.Version).OriginalValue = version;
        }

        var set = contact.Set(request.Name, request.Designation, request.Email, request.Phone, request.ContactType, request.IsPrimary, request.IsActive);
        if (set.IsFailure)
        {
            return set.Error;
        }

        if (request.IsPrimary)
        {
            await ClearPrimaryAsync(contact.TransporterId, contact.Id, cancellationToken);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("contacts.concurrent_update", "Someone else changed this contact. Reload and try again.");
        }

        return ToDto(contact);
    }

    /// <summary>A transporter has one primary contact.</summary>
    private async Task ClearPrimaryAsync(Guid transporterId, Guid? except, CancellationToken cancellationToken)
    {
        foreach (var other in await db.Contacts.Where(c => c.TransporterId == transporterId && c.IsPrimary && c.Id != (except ?? Guid.Empty)).ToListAsync(cancellationToken))
        {
            other.ClearPrimary();
        }
    }

    private static ContactDto ToDto(TransporterContact c) => new(c.Id, c.TransporterId, c.Name, c.Designation, c.Email, c.Phone, c.ContactType, c.IsPrimary, c.IsActive, c.Version);
}

internal sealed class BranchHandler(TransportersDbContext db, TransporterAccess access, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<BranchDto>>> ListAsync(Guid transporterId, CancellationToken cancellationToken)
    {
        if (access.Check(transporterId, AccessLevel.Read) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var items = await db.Branches.AsNoTracking().Where(b => b.TransporterId == transporterId).OrderBy(b => b.Code).ToListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<Result<BranchDto>> AddAsync(Guid transporterId, SaveBranchRequest request, CancellationToken cancellationToken)
    {
        var found = await db.FindTransporterAsync(access, transporterId, AccessLevel.Manage, cancellationToken);
        if (found.IsFailure || user.TenantId is not { } tenantId)
        {
            return found.IsFailure ? found.Error : TransporterAccess.NotFound;
        }

        var branch = TransporterBranch.Create(tenantId, transporterId, request.Code, request.Name, request.Address, request.City, request.State, request.Latitude, request.Longitude, request.ContactName, request.ContactPhone);
        if (branch.IsFailure)
        {
            return branch.Error;
        }

        if (await db.Branches.AnyAsync(b => b.TransporterId == transporterId && b.Code == branch.Value.Code, cancellationToken))
        {
            return Error.Conflict("branches.code_exists", $"Branch code {branch.Value.Code} already exists for this transporter.");
        }

        db.Branches.Add(branch.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(branch.Value);
    }

    public async Task<Result<BranchDto>> UpdateAsync(Guid branchId, SaveBranchRequest request, CancellationToken cancellationToken)
    {
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch is null || access.Check(branch.TransporterId, AccessLevel.Manage).IsFailure)
        {
            return Error.NotFound("branches.not_found", "Branch not found.");
        }

        if (request.Version is { } version)
        {
            db.Entry(branch).Property(b => b.Version).OriginalValue = version;
        }

        var set = branch.Set(request.Code, request.Name, request.Address, request.City, request.State, request.Latitude, request.Longitude, request.ContactName, request.ContactPhone, request.IsActive);
        if (set.IsFailure)
        {
            return set.Error;
        }

        if (await db.Branches.AnyAsync(b => b.TransporterId == branch.TransporterId && b.Code == branch.Code && b.Id != branch.Id, cancellationToken))
        {
            return Error.Conflict("branches.code_exists", $"Branch code {branch.Code} already exists for this transporter.");
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("branches.concurrent_update", "Someone else changed this branch. Reload and try again.");
        }

        return ToDto(branch);
    }

    private static BranchDto ToDto(TransporterBranch b) => new(b.Id, b.TransporterId, b.Code, b.Name, b.Address, b.City, b.State, b.Latitude, b.Longitude, b.ContactName, b.ContactPhone, b.IsActive, b.Version);
}
