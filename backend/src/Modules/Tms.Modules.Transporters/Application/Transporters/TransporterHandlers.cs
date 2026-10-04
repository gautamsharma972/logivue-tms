using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Transporters;

internal sealed class ListTransportersHandler(TransportersDbContext db, TransporterAccess access)
{
    public async Task<Result<PagedResult<TransporterSummaryDto>>> HandleAsync(ListTransportersQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanUseModule)
        {
            return TransporterAccess.Forbidden;
        }

        var transporters = db.Transporters.AsNoTracking().AsQueryable();
        if (access.OwnTransporterId is { } own)
        {
            transporters = transporters.Where(t => t.Id == own);
        }

        if (query.Status is { } status)
        {
            transporters = transporters.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            transporters = transporters.Where(t =>
                t.LegalName.Contains(term) || (t.TradeName != null && t.TradeName.Contains(term)) ||
                t.Code.Contains(term) || t.Pan.Contains(term) || (t.Gstin != null && t.Gstin.Contains(term)));
        }

        var page = await transporters.OrderBy(t => t.LegalName).ThenBy(t => t.Id).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<TransporterSummaryDto>(page.Items.Select(t => t.ToSummary()).ToList(), page.Page, page.PageSize, page.TotalCount);
    }
}

/// <summary>Small picker list (id, code, name) of active transporters for forms elsewhere, e.g. linking a vendor-portal user.</summary>
internal sealed class LookupTransportersHandler(TransportersDbContext db, TransporterAccess access)
{
    public async Task<Result<IReadOnlyList<TransporterLookupDto>>> HandleAsync(string? search, CancellationToken cancellationToken)
    {
        if (access.IsVendor)
        {
            return TransporterAccess.Forbidden;
        }

        var transporters = db.Transporters.AsNoTracking().Where(t => t.Status == TransporterStatus.Active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            transporters = transporters.Where(t => t.LegalName.Contains(term) || t.Code.Contains(term));
        }

        var rows = await transporters.OrderBy(t => t.LegalName).Take(20)
            .Select(t => new TransporterLookupDto(t.Id, t.Code, t.LegalName))
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<TransporterLookupDto>>(rows);
    }
}

internal sealed class GetTransporterHandler(TransportersDbContext db, TransporterAccess access)
{
    public async Task<Result<TransporterDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await db.FindTransporterAsync(access, id, AccessLevel.Read, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var transporter = found.Value;
        var missing = transporter.Status is TransporterStatus.Draft or TransporterStatus.Rejected
            ? transporter.MissingForSubmission(await db.TransporterDocumentKindsAsync(id, cancellationToken))
            : [];
        return transporter.ToDto(missing);
    }
}

internal sealed class CreateTransporterHandler(
    TransportersDbContext db,
    ICurrentUser currentUser,
    TransporterAccess access,
    NumberSequence sequence)
{
    public async Task<Result<TransporterDto>> HandleAsync(SaveTransporterRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageInternally)
        {
            return TransporterAccess.Forbidden;
        }

        var tenantId = currentUser.TenantId!.Value;
        var number = await sequence.NextAsync(tenantId, "transporter", cancellationToken);
        var created = Transporter.Create(tenantId, $"TR-{number:D5}", request.ToProfile());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var transporter = created.Value;
        var clash = await DuplicateCheck.FindAsync(db, transporter.Pan, transporter.Gstin, transporter.Id, cancellationToken);
        if (clash is not null)
        {
            return clash;
        }

        db.Transporters.Add(transporter);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return DuplicateCheck.FromException(ex);
        }

        return transporter.ToDto(transporter.MissingForSubmission([]));
    }
}

internal static class DuplicateCheck
{
    public static async Task<Error?> FindAsync(TransportersDbContext db, string pan, string? gstin, Guid exceptId, CancellationToken cancellationToken)
    {
        if (await db.Transporters.AnyAsync(t => t.Pan == pan && t.Id != exceptId, cancellationToken))
        {
            return Error.Conflict("transporters.pan_exists", $"A transporter with PAN {pan} already exists.");
        }

        if (gstin is not null && await db.Transporters.AnyAsync(t => t.Gstin == gstin && t.Id != exceptId, cancellationToken))
        {
            return Error.Conflict("transporters.gstin_exists", $"A transporter with GSTIN {gstin} already exists.");
        }

        return null;
    }

    public static Error FromException(DbUpdateException ex) =>
        ex.ViolatedIndex().Contains("gstin", StringComparison.OrdinalIgnoreCase)
            ? Error.Conflict("transporters.gstin_exists", "A transporter with this GSTIN already exists.")
            : Error.Conflict("transporters.pan_exists", "A transporter with this PAN already exists.");
}

internal sealed class UpdateTransporterHandler(TransportersDbContext db, TransporterAccess access)
{
    public async Task<Result<TransporterDto>> HandleAsync(Guid id, SaveTransporterRequest request, CancellationToken cancellationToken)
    {
        if (request.Version is null)
        {
            return Error.Validation("transporters.version_required", "The current version of the transporter is required.");
        }

        var found = await db.FindTransporterAsync(access, id, AccessLevel.Manage, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var transporter = found.Value;
        var profile = request.ToProfile();
        if (access.IsVendor)
        {
            // Vendors maintain contact details only; who the company *is* stays under the buyer's control.
            profile = profile with { LegalName = transporter.LegalName, Pan = transporter.Pan, Gstin = transporter.Gstin };
        }

        db.Entry(transporter).Property(t => t.Version).OriginalValue = request.Version.Value;
        var updated = transporter.UpdateProfile(profile);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var clash = await DuplicateCheck.FindAsync(db, transporter.Pan, transporter.Gstin, transporter.Id, cancellationToken);
        if (clash is not null)
        {
            return clash;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return DuplicateCheck.FromException(ex);
        }

        var missing = transporter.Status is TransporterStatus.Draft or TransporterStatus.Rejected
            ? transporter.MissingForSubmission(await db.TransporterDocumentKindsAsync(id, cancellationToken))
            : [];
        return transporter.ToDto(missing);
    }
}

internal sealed class UpdateBankHandler(TransportersDbContext db, TransporterAccess access)
{
    public async Task<Result<TransporterDto>> HandleAsync(Guid id, SaveBankRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageBank)
        {
            return access.IsVendor ? TransporterAccess.NotFound : TransporterAccess.Forbidden;
        }

        var found = await db.FindTransporterAsync(access, id, AccessLevel.Manage, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var transporter = found.Value;
        db.Entry(transporter).Property(t => t.Version).OriginalValue = request.Version;
        var updated = transporter.UpdateBank(new BankAccount(request.AccountHolder, request.AccountNumber, request.Ifsc, request.BankName));
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);

        var missing = transporter.Status is TransporterStatus.Draft or TransporterStatus.Rejected
            ? transporter.MissingForSubmission(await db.TransporterDocumentKindsAsync(id, cancellationToken))
            : [];
        return transporter.ToDto(missing);
    }
}

/// <summary>Sends a completed draft for onboarding approval via the approval engine.</summary>
internal sealed class SubmitTransporterHandler(
    TransportersDbContext db,
    TransporterAccess access,
    IApprovalGateway approvals,
    TimeProvider clock)
{
    public const string DocumentType = "transporter_onboarding";

    public async Task<Result<TransporterDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanManageInternally)
        {
            return access.IsVendor ? TransporterAccess.NotFound : TransporterAccess.Forbidden;
        }

        var found = await db.FindTransporterAsync(access, id, AccessLevel.Manage, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var transporter = found.Value;
        if (transporter.Status is not (TransporterStatus.Draft or TransporterStatus.Rejected))
        {
            return Error.Conflict("transporters.not_submittable", "Only a draft or rejected transporter can be submitted for approval.");
        }

        var missing = transporter.MissingForSubmission(await db.TransporterDocumentKindsAsync(id, cancellationToken));
        if (missing.Count > 0)
        {
            return Error.Validation("transporters.onboarding_incomplete", "Complete the onboarding requirements before submitting.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["requirements"] = [.. missing] },
            };
        }

        var submitted = await approvals.SubmitAsync(
            new SubmitApproval(DocumentType, transporter.Id, $"Onboard {transporter.LegalName} ({transporter.Code})", null), cancellationToken);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var marked = transporter.MarkSubmitted(submitted.Value.RequestId, submitted.Value.Status, clock.GetUtcNow());
        if (marked.IsFailure)
        {
            return marked.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return transporter.ToDto([]);
    }
}

internal sealed class SuspendTransporterHandler(TransportersDbContext db, TransporterAccess access)
{
    public async Task<Result<TransporterDto>> HandleAsync(Guid id, SuspendRequest request, CancellationToken cancellationToken) =>
        await ChangeAsync(id, t => t.Suspend(request.Reason), cancellationToken);

    public async Task<Result<TransporterDto>> ReactivateAsync(Guid id, CancellationToken cancellationToken) =>
        await ChangeAsync(id, t => t.Reactivate(), cancellationToken);

    private async Task<Result<TransporterDto>> ChangeAsync(Guid id, Func<Transporter, Result> change, CancellationToken cancellationToken)
    {
        if (!access.CanManageInternally)
        {
            return access.IsVendor ? TransporterAccess.NotFound : TransporterAccess.Forbidden;
        }

        var found = await db.FindTransporterAsync(access, id, AccessLevel.Manage, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var outcome = change(found.Value);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return found.Value.ToDto([]);
    }
}
