using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

public sealed record MaterialLineDto(string? MaterialCode, string MaterialName, decimal Quantity, string Unit, decimal WeightKg, decimal? VolumeCbm, string? Dimensions, int Packages, string? Remarks = null, decimal? VolumeCuFt = null, decimal? NetWeightKg = null);

public sealed record FileDto(Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);

public sealed record RequestSummaryDto(
    Guid Id, string Number, Guid CustomerId, string CustomerName, string? CustomerReference, DateTimeOffset ReceivedAt, string CfaName, string DestinationName, DateOnly? RequestedDate, string Priority,
    RequestStatus Status, decimal TotalWeightKg, Guid? LrId);

public sealed record RequestDto(
    Guid Id, string Number, Guid CustomerId, string CustomerName, string? CustomerReference, DateTimeOffset ReceivedAt, string? EmailReference, Guid? CfaLocationId, string? CfaName, Guid? DestinationLocationId,
    string? DestinationName, DateOnly? RequestedDate, string Priority, string? VehicleTypeRequested, string? Instructions, string? Remarks, RequestStatus Status, IReadOnlyList<string> Issues, Guid? LrId,
    IReadOnlyList<MaterialLineDto> Lines, IReadOnlyList<FileDto> Files, long Version);

public sealed record SaveRequestRequest(
    Guid CustomerId, string? CustomerReference, DateTimeOffset? ReceivedAt, string? EmailReference, Guid? CfaLocationId, Guid? DestinationLocationId, DateOnly? RequestedDate, string? Priority,
    string? VehicleTypeRequested, string? Instructions, string? Remarks, IReadOnlyList<MaterialLineDto> Lines, long Version = 0);

internal sealed class SaveRequestRequestValidator : AbstractValidator<SaveRequestRequest>
{
    public SaveRequestRequestValidator()
    {
        RuleFor(x => x.CustomerReference).MaximumLength(100);
        RuleFor(x => x.EmailReference).MaximumLength(300);
        RuleFor(x => x.Priority).MaximumLength(20);
        RuleFor(x => x.VehicleTypeRequested).MaximumLength(60);
        RuleFor(x => x.Instructions).MaximumLength(1000);
        RuleFor(x => x.Remarks).MaximumLength(1000);
        RuleFor(x => x.Lines).NotNull().Must(l => l.Count <= 100).WithMessage("A request can have at most 100 material lines.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(m => m.MaterialName).NotEmpty().MaximumLength(200);
            l.RuleFor(m => m.MaterialCode).MaximumLength(60);
            l.RuleFor(m => m.Unit).NotEmpty().MaximumLength(12);
            l.RuleFor(m => m.Quantity).GreaterThanOrEqualTo(0);
            l.RuleFor(m => m.WeightKg).GreaterThanOrEqualTo(0);
            l.RuleFor(m => m.VolumeCbm).GreaterThanOrEqualTo(0).When(m => m.VolumeCbm.HasValue);
            l.RuleFor(m => m.Packages).GreaterThanOrEqualTo(0);
            l.RuleFor(m => m.Dimensions).MaximumLength(100);
        });
    }
}

internal sealed class RequestHandler(TransportOpsDbContext db, OpsAccess access, ISequenceGenerator sequences, IOpsSettings settings, FileIntake files, TimeProvider clock)
{
    public async Task<Result<PagedResult<RequestSummaryDto>>> ListAsync(string? search, RequestStatus? status, Guid? customerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Requests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(r => r.Number.Contains(s) || (r.CustomerReference != null && r.CustomerReference.Contains(s)));
        }

        if (status is { } st)
        {
            query = query.Where(r => r.Status == st);
        }

        if (customerId is { } c)
        {
            query = query.Where(r => r.CustomerId == c);
        }

        var paged = await query.OrderByDescending(r => r.ReceivedAt).Select(r => new { r, Weight = r.Lines.Sum(l => l.WeightKg) }).ToPagedAsync(page, pageSize, cancellationToken);
        var (customers, locations) = await NamesAsync(paged.Items.Select(i => i.r), cancellationToken);
        var items = paged.Items.Select(i => new RequestSummaryDto(i.r.Id, i.r.Number, i.r.CustomerId, customers.GetValueOrDefault(i.r.CustomerId, "?"), i.r.CustomerReference, i.r.ReceivedAt,
            Name(locations, i.r.CfaLocationId), Name(locations, i.r.DestinationLocationId), i.r.RequestedDate, i.r.Priority, i.r.Status, i.Weight, i.r.LrId)).ToList();
        return new PagedResult<RequestSummaryDto>(items, paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<Result<RequestDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.Requests.AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return request is null ? OpsErrors.RequestNotFound : await ToDtoAsync(request, cancellationToken);
    }

    public async Task<Result<RequestDto>> CreateAsync(SaveRequestRequest request, CancellationToken cancellationToken)
    {
        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        var tenant = access.TenantId!.Value;
        if (await Check(request, cancellationToken) is { } error)
        {
            return error;
        }

        var numbering = await settings.GetAsync(OpsSettingKeys.Numbering, new NumberingSetting(), cancellationToken);
        var number = Numbers.Format(numbering.RequestPrefix, await sequences.NextAsync(tenant, "ops-request", cancellationToken), numbering.Digits);
        var entity = DeliveryRequest.Create(tenant, number, request.CustomerId, request.ReceivedAt ?? clock.GetUtcNow());
        Apply(entity, request);
        db.Requests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(entity, cancellationToken);
    }

    public async Task<Result<RequestDto>> UpdateAsync(Guid id, SaveRequestRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Requests.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (entity is null)
        {
            return OpsErrors.RequestNotFound;
        }

        if (await Check(request, cancellationToken) is { } error)
        {
            return error;
        }

        db.Entry(entity).Property(r => r.Version).OriginalValue = request.Version;
        foreach (var old in entity.Lines.ToList())
        {
            db.RequestLines.Remove(old);
        }

        var result = Apply(entity, request);
        if (result.IsFailure)
        {
            return result.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OpsErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(entity, cancellationToken);
    }

    public async Task<Result<RequestDto>> StartReviewAsync(Guid id, CancellationToken cancellationToken) =>
        await Transition(id, r => r.StartReview(), cancellationToken);

    /// <summary>Checks the request against the customer's required details. Complete → Validated; otherwise Incomplete, listing what is missing.</summary>
    public async Task<Result<RequestDto>> ValidateAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.Requests.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return OpsErrors.RequestNotFound;
        }

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        var issues = request.Validate(customer?.RequiredFields ?? []);
        if (issues.IsFailure)
        {
            return issues.Error;
        }

        // The destination must be usable for the arrival check later, so a missing coordinate is flagged now rather than at the gate.
        if (request.DestinationLocationId is { } dest)
        {
            var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == dest, cancellationToken);
            if (location is { HasCoordinates: false })
            {
                var list = issues.Value.ToList();
                list.Add($"{location.Name} has no map coordinates, so arrival cannot be verified at the gate. Add them in Locations.");
                request.AddIssues(list);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    public async Task<Result<RequestDto>> MarkReadyAsync(Guid id, CancellationToken cancellationToken) =>
        await Transition(id, r => r.MarkReady(), cancellationToken);

    public async Task<Result<RequestDto>> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        await Transition(id, r => r.Cancel(), cancellationToken);

    public async Task<Result<FileDto>> AttachAsync(Guid id, Stream content, string? fileName, CancellationToken cancellationToken)
    {
        var request = await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return OpsErrors.RequestNotFound;
        }

        var blob = await files.SaveAsync(content, fileName, "requests", FileIntakeLimits.Attachment, cancellationToken);
        if (blob.IsFailure)
        {
            return blob.Error;
        }

        var file = StoredFile.Create(request.TenantId, request.Id, blob.Value.Key, blob.Value.SafeName, blob.Value.ContentType, blob.Value.SizeBytes, blob.Value.Sha256, clock.GetUtcNow(), access.UserId);
        db.Files.Add(file);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(blob.Value.Key, cancellationToken);
            throw;
        }

        return new FileDto(file.Id, file.FileName, file.ContentType, file.SizeBytes, file.UploadedAt);
    }

    public async Task<Result<(Stream Content, string ContentType, string FileName)>> OpenFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        var stream = file is null ? null : await files.OpenAsync(file.FileKey, cancellationToken);
        return file is null || stream is null ? OpsErrors.FileNotFound : (stream, file.ContentType, file.FileName);
    }

    private async Task<Result<RequestDto>> Transition(Guid id, Func<DeliveryRequest, Result> action, CancellationToken cancellationToken)
    {
        var request = await db.Requests.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return OpsErrors.RequestNotFound;
        }

        var result = action(request);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    private async Task<Error?> Check(SaveRequestRequest r, CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == r.CustomerId, cancellationToken))
        {
            return OpsErrors.CustomerNotFound;
        }

        foreach (var id in new[] { r.CfaLocationId, r.DestinationLocationId }.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            if (!await db.Locations.AnyAsync(l => l.Id == id, cancellationToken))
            {
                return OpsErrors.LocationNotFound;
            }
        }

        return null;
    }

    private Result Apply(DeliveryRequest entity, SaveRequestRequest r)
    {
        var edited = entity.Edit(r.CustomerId, r.CustomerReference, r.ReceivedAt ?? entity.ReceivedAt, r.EmailReference, r.CfaLocationId, r.DestinationLocationId, r.RequestedDate, r.Priority ?? "Normal",
            r.VehicleTypeRequested, r.Instructions, r.Remarks);
        if (edited.IsFailure)
        {
            return edited;
        }

        entity.SetLines(r.Lines.Select(l => RequestLine.Create(entity.TenantId, entity.Id, l.MaterialCode, l.MaterialName, l.Quantity, l.Unit, l.WeightKg, l.VolumeCbm, l.Dimensions, l.Packages, l.Remarks)));
        foreach (var line in entity.Lines)
        {
            db.Entry(line).State = EntityState.Added;
        }

        return Result.Success();
    }

    private async Task<(Dictionary<Guid, string> Customers, Dictionary<Guid, string> Locations)> NamesAsync(IEnumerable<DeliveryRequest> requests, CancellationToken cancellationToken)
    {
        var list = requests.ToList();
        var customerIds = list.Select(r => r.CustomerId).Distinct().ToList();
        var locationIds = list.SelectMany(r => new[] { r.CfaLocationId, r.DestinationLocationId }).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        return (customers, locations);
    }

    private static string Name(Dictionary<Guid, string> names, Guid? id) => id is { } i && names.TryGetValue(i, out var n) ? n : "—";

    private async Task<RequestDto> ToDtoAsync(DeliveryRequest r, CancellationToken cancellationToken)
    {
        var (customers, locations) = await NamesAsync([r], cancellationToken);
        var attachments = await db.Files.AsNoTracking().Where(f => f.OwnerId == r.Id).OrderBy(f => f.UploadedAt).ToListAsync(cancellationToken);
        return new RequestDto(r.Id, r.Number, r.CustomerId, customers.GetValueOrDefault(r.CustomerId, "?"), r.CustomerReference, r.ReceivedAt, r.EmailReference, r.CfaLocationId,
            r.CfaLocationId is { } c ? locations.GetValueOrDefault(c) : null, r.DestinationLocationId, r.DestinationLocationId is { } d ? locations.GetValueOrDefault(d) : null, r.RequestedDate, r.Priority,
            r.VehicleTypeRequested, r.Instructions, r.Remarks, r.Status, r.Issues, r.LrId,
            r.Lines.Select(l => new MaterialLineDto(l.MaterialCode, l.MaterialName, l.Quantity, l.Unit, l.WeightKg, l.VolumeCbm, l.Dimensions, l.Packages, l.Remarks)).ToList(),
            attachments.Select(f => new FileDto(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.UploadedAt)).ToList(), r.Version);
    }
}

internal static class FileIntakeLimits
{
    public const long Attachment = 10 * 1024 * 1024;
}
