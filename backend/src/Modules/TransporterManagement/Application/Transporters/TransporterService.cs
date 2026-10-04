using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Transporters;

/// <summary>Transporter master data with its contacts and branches. Writes are audited and transactional.</summary>
public interface ITransporterService
{
    Task<PagedResult<TransporterListItem>> SearchAsync(TransporterSearch search, CancellationToken cancellationToken = default);

    Task<TransporterDetail> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<TransporterDetail> CreateAsync(CreateTransporterRequest request, CancellationToken cancellationToken = default);

    Task<TransporterDetail> UpdateAsync(long id, UpdateTransporterRequest request, CancellationToken cancellationToken = default);

    Task<ContactDto> AddContactAsync(long transporterId, SaveContactRequest request, CancellationToken cancellationToken = default);

    Task<ContactDto> UpdateContactAsync(long transporterId, long contactId, SaveContactRequest request, CancellationToken cancellationToken = default);

    Task<BranchDto> AddBranchAsync(long transporterId, SaveBranchRequest request, CancellationToken cancellationToken = default);

    Task<BranchDto> UpdateBranchAsync(long transporterId, long branchId, SaveBranchRequest request, CancellationToken cancellationToken = default);
}

public sealed class TransporterService(
    IRepository<Transporter> transporters,
    IRepository<TransporterContact> contacts,
    IRepository<TransporterBranch> branches,
    IRepository<TransporterType> transporterTypes,
    ITransporterQueries queries,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<CreateTransporterRequest> createValidator,
    IValidator<UpdateTransporterRequest> updateValidator,
    IValidator<SaveContactRequest> contactValidator,
    IValidator<SaveBranchRequest> branchValidator,
    ILogger<TransporterService> logger) : ITransporterService
{
    public Task<PagedResult<TransporterListItem>> SearchAsync(TransporterSearch search, CancellationToken cancellationToken = default) =>
        queries.SearchTransportersAsync(search with
        {
            Page = Paging.NormalisePage(search.Page),
            PageSize = Paging.NormalisePageSize(search.PageSize)
        }, cancellationToken);

    public async Task<TransporterDetail> GetAsync(long id, CancellationToken cancellationToken = default) =>
        await queries.GetTransporterAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Transporter {id} was not found.", "TRANSPORTER_NOT_FOUND");

    public async Task<TransporterDetail> CreateAsync(CreateTransporterRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var code = request.TransporterCode.Trim().ToUpperInvariant();
        var gstin = Mapping.CleanUpper(request.Gstin);

        if (await transporters.AnyAsync(t => t.TransporterCode == code, cancellationToken))
        {
            throw new ConflictException($"Transporter code {code} already exists.", "TRANSPORTER_CODE_EXISTS");
        }

        if (gstin is not null && await transporters.AnyAsync(t => t.Gstin == gstin, cancellationToken))
        {
            throw new ConflictException($"GSTIN {gstin} is already registered to another transporter.", "GSTIN_EXISTS");
        }

        await TransporterGuards.EnsureActiveTransporterTypeAsync(transporterTypes, request.TransporterTypeId, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var entity = new Transporter
        {
            TransporterCode = code,
            LegalName = request.LegalName.Trim(),
            TradeName = Mapping.Clean(request.TradeName),
            TransporterTypeId = request.TransporterTypeId,
            CompanyType = Mapping.Clean(request.CompanyType),
            Pan = Mapping.CleanUpper(request.Pan),
            Gstin = gstin,
            RegistrationNumber = Mapping.Clean(request.RegistrationNumber),
            Address = Mapping.Clean(request.Address),
            City = Mapping.Clean(request.City),
            State = Mapping.Clean(request.State),
            Country = Mapping.Clean(request.Country),
            PrimaryContactName = Mapping.Clean(request.PrimaryContactName),
            PrimaryContactEmail = Mapping.Clean(request.PrimaryContactEmail),
            PrimaryContactPhone = Mapping.Clean(request.PrimaryContactPhone),
            Website = Mapping.Clean(request.Website),
            Status = TransporterStatus.Draft,
            CreatedAt = now,
            CreatedBy = currentUser.UserId,
            UpdatedAt = now,
            UpdatedBy = currentUser.UserId
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        transporters.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("Transporter", entity.Id.ToString(), "Created",
            NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Transporter {TransporterCode} created with id {TransporterId}", code, entity.Id);

        return await GetAsync(entity.Id, cancellationToken);
    }

    public async Task<TransporterDetail> UpdateAsync(long id, UpdateTransporterRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var entity = await TransporterGuards.LoadAsync(transporters, id, cancellationToken);
        TransporterGuards.EnsureEditable(entity);

        var gstin = Mapping.CleanUpper(request.Gstin);
        if (gstin is not null && gstin != entity.Gstin
            && await transporters.AnyAsync(t => t.Gstin == gstin && t.Id != id, cancellationToken))
        {
            throw new ConflictException($"GSTIN {gstin} is already registered to another transporter.", "GSTIN_EXISTS");
        }

        if (request.TransporterTypeId != entity.TransporterTypeId)
        {
            await TransporterGuards.EnsureActiveTransporterTypeAsync(transporterTypes, request.TransporterTypeId, cancellationToken);
        }

        var before = AuditJson.Serialize(entity);

        entity.LegalName = request.LegalName.Trim();
        entity.TradeName = Mapping.Clean(request.TradeName);
        entity.TransporterTypeId = request.TransporterTypeId;
        entity.CompanyType = Mapping.Clean(request.CompanyType);
        entity.Pan = Mapping.CleanUpper(request.Pan);
        entity.Gstin = gstin;
        entity.RegistrationNumber = Mapping.Clean(request.RegistrationNumber);
        entity.Address = Mapping.Clean(request.Address);
        entity.City = Mapping.Clean(request.City);
        entity.State = Mapping.Clean(request.State);
        entity.Country = Mapping.Clean(request.Country);
        entity.PrimaryContactName = Mapping.Clean(request.PrimaryContactName);
        entity.PrimaryContactEmail = Mapping.Clean(request.PrimaryContactEmail);
        entity.PrimaryContactPhone = Mapping.Clean(request.PrimaryContactPhone);
        entity.Website = Mapping.Clean(request.Website);
        entity.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        entity.UpdatedBy = currentUser.UserId;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("Transporter", id.ToString(), "Updated",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Transporter {TransporterId} master data updated", id);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<ContactDto> AddContactAsync(long transporterId, SaveContactRequest request, CancellationToken cancellationToken = default)
    {
        await contactValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        if (request.IsPrimary)
        {
            await ClearPrimaryContactAsync(transporterId, exceptId: null, cancellationToken);
        }

        var entity = new TransporterContact
        {
            TransporterId = transporterId,
            Name = request.Name.Trim(),
            Designation = Mapping.Clean(request.Designation),
            Email = Mapping.Clean(request.Email),
            Phone = Mapping.Clean(request.Phone),
            ContactType = request.ContactType.Trim(),
            IsPrimary = request.IsPrimary,
            IsActive = request.IsActive
        };

        contacts.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterContact", entity.Id.ToString(), "ContactAdded",
            NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.ToDto();
    }

    public async Task<ContactDto> UpdateContactAsync(long transporterId, long contactId, SaveContactRequest request, CancellationToken cancellationToken = default)
    {
        await contactValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var entity = await contacts.FindAsync(contactId, cancellationToken);
        if (entity is null || entity.TransporterId != transporterId)
        {
            throw new NotFoundException($"Contact {contactId} was not found for transporter {transporterId}.", "CONTACT_NOT_FOUND");
        }

        var before = AuditJson.Serialize(entity);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        if (request.IsPrimary && !entity.IsPrimary)
        {
            await ClearPrimaryContactAsync(transporterId, exceptId: contactId, cancellationToken);
        }

        entity.Name = request.Name.Trim();
        entity.Designation = Mapping.Clean(request.Designation);
        entity.Email = Mapping.Clean(request.Email);
        entity.Phone = Mapping.Clean(request.Phone);
        entity.ContactType = request.ContactType.Trim();
        entity.IsPrimary = request.IsPrimary;
        entity.IsActive = request.IsActive;

        await audit.RecordAsync(new AuditEntry("TransporterContact", contactId.ToString(), "ContactUpdated",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.ToDto();
    }

    public async Task<BranchDto> AddBranchAsync(long transporterId, SaveBranchRequest request, CancellationToken cancellationToken = default)
    {
        await branchValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var code = request.BranchCode.Trim().ToUpperInvariant();
        if (await branches.AnyAsync(b => b.TransporterId == transporterId && b.BranchCode == code, cancellationToken))
        {
            throw new ConflictException($"Branch code {code} already exists for this transporter.", "BRANCH_CODE_EXISTS");
        }

        var entity = new TransporterBranch
        {
            TransporterId = transporterId,
            BranchCode = code,
            BranchName = request.BranchName.Trim(),
            Address = Mapping.Clean(request.Address),
            City = Mapping.Clean(request.City),
            State = Mapping.Clean(request.State),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ContactName = Mapping.Clean(request.ContactName),
            ContactPhone = Mapping.Clean(request.ContactPhone),
            IsActive = request.IsActive
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        branches.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterBranch", entity.Id.ToString(), "BranchAdded",
            NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.ToDto();
    }

    public async Task<BranchDto> UpdateBranchAsync(long transporterId, long branchId, SaveBranchRequest request, CancellationToken cancellationToken = default)
    {
        await branchValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var entity = await branches.FindAsync(branchId, cancellationToken);
        if (entity is null || entity.TransporterId != transporterId)
        {
            throw new NotFoundException($"Branch {branchId} was not found for transporter {transporterId}.", "BRANCH_NOT_FOUND");
        }

        var code = request.BranchCode.Trim().ToUpperInvariant();
        if (code != entity.BranchCode
            && await branches.AnyAsync(b => b.TransporterId == transporterId && b.BranchCode == code, cancellationToken))
        {
            throw new ConflictException($"Branch code {code} already exists for this transporter.", "BRANCH_CODE_EXISTS");
        }

        var before = AuditJson.Serialize(entity);

        entity.BranchCode = code;
        entity.BranchName = request.BranchName.Trim();
        entity.Address = Mapping.Clean(request.Address);
        entity.City = Mapping.Clean(request.City);
        entity.State = Mapping.Clean(request.State);
        entity.Latitude = request.Latitude;
        entity.Longitude = request.Longitude;
        entity.ContactName = Mapping.Clean(request.ContactName);
        entity.ContactPhone = Mapping.Clean(request.ContactPhone);
        entity.IsActive = request.IsActive;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterBranch", branchId.ToString(), "BranchUpdated",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.ToDto();
    }

    /// <summary>A transporter has at most one primary contact, so promoting one demotes the others.</summary>
    private async Task ClearPrimaryContactAsync(long transporterId, long? exceptId, CancellationToken cancellationToken)
    {
        var current = await contacts.ListAsync(c => c.TransporterId == transporterId && c.IsPrimary, cancellationToken);
        foreach (var contact in current.Where(c => c.Id != exceptId))
        {
            contact.IsPrimary = false;
        }
    }
}
