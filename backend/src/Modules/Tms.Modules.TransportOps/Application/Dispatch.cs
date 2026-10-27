using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.India;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.TransportOps.Application;

public sealed record EventDto(
    Guid Id, EventType Type, DateTimeOffset ActualAt, DateTimeOffset RecordedAt, EventSource Source, Guid? UserId, string? Remarks, string? Verification, double? DistanceM, double? AccuracyM, int? RadiusM,
    Guid? CorrectsEventId, Guid? TripId);

public sealed record LrSummaryDto(
    Guid Id, string Number, Guid CustomerId, string CustomerName, string? CustomerReference, string CfaName, string DestinationName, string DestinationCity, Guid? TripId, string? TripNumber,
    string? VehicleRegistration, DeliveryStatus Status, PodStatus PodStatus, DateOnly? DeliveryDate, decimal TotalWeightKg, int TotalPackages, DateTimeOffset? CfaReachedAt, DateTimeOffset? LoadingStartedAt,
    DateTimeOffset? LoadingCompletedAt, DateTimeOffset? DepartedAt, DateTimeOffset? ArrivedAt, DateTimeOffset? UnloadedAt, bool QrActive, DateTimeOffset CreatedAt, MovementType Movement = MovementType.Supply);

public sealed record LrDetailDto(
    LrSummaryDto Lr, string? Instructions, DateTimeOffset? QrExpiresAt, int PrintCount, IReadOnlyList<MaterialLineDto> Lines, IReadOnlyList<EventDto> Events, Guid? RequestId, long Version,
    string? IndentNo = null, IReadOnlyList<FileDto>? Files = null, CrossBillingDto? CrossBilling = null);

/// <summary>The price agreed for a cross-billing LR. Shown only to people who may see prices or create such LRs.</summary>
public sealed record CrossBillingDto(CrossBillingMode Mode, decimal? Amount, decimal? Kg, decimal? RatePerKg, decimal? Freight);

public sealed record CrossBillingRequest(CrossBillingMode Mode, decimal? Amount, decimal? Kg, decimal? RatePerKg);

/// <summary><see cref="Packages"/> is the number of packages; the material is optional on an LR. Volume is given in cubic feet.</summary>
public sealed record LrLineRequest(
    string? MaterialCode, string? MaterialName, decimal Quantity, string Unit, decimal WeightKg, decimal? VolumeCbm, string? Dimensions, int Packages, decimal? VolumeCuFt = null, decimal? NetWeightKg = null, string? InvoiceNo = null);

/// <summary>Create an LR from a ready delivery request (give <see cref="RequestId"/>), or directly (customer, CFA, destination and lines).</summary>
public sealed record CreateLrRequest(
    Guid? RequestId, Guid? CustomerId, string? CustomerReference, Guid? CfaLocationId, Guid? DestinationLocationId, DateOnly? DeliveryDate, string? Instructions, IReadOnlyList<LrLineRequest>? Lines, string? IndentNo = null, MovementType? Movement = null, CrossBillingRequest? CrossBilling = null);

public sealed record CreatedLrDto(LrDetailDto Lr, string QrPath);

public sealed record LrPrintDto(
    Guid Id, string Number, string CustomerName, string? CustomerReference, string CfaName, string CfaAddress, string DestinationName, string DestinationAddress, string? VehicleRegistration, string? DriverName,
    string? DriverPhone, DateOnly? DeliveryDate, string? Instructions, IReadOnlyList<MaterialLineDto> Lines, decimal TotalWeightKg, int TotalPackages, string QrPath, int PrintCount, string? TripNumber,
    string? IndentNo = null, DateTimeOffset? CreatedAt = null);

internal sealed class CreateLrRequestValidator : AbstractValidator<CreateLrRequest>
{
    public CreateLrRequestValidator()
    {
        RuleFor(x => x).Must(x => x.RequestId is not null || (x.CustomerId is not null && x.CfaLocationId is not null && x.DestinationLocationId is not null && x.Lines is { Count: > 0 }))
            .WithMessage("Give a delivery request, or the customer, CFA, destination and at least one material line.").WithName("LR");
        RuleFor(x => x.CrossBilling).NotNull().When(x => x.Movement == MovementType.CrossBilling).WithMessage("Enter how the cross-billing freight is priced.");
        When(x => x.Movement == MovementType.CrossBilling && x.CrossBilling is not null, () =>
        {
            RuleFor(x => x.CrossBilling!.Amount).NotNull().GreaterThan(0).When(x => x.CrossBilling!.Mode == CrossBillingMode.Lumpsum).WithName("Lumpsum amount");
            RuleFor(x => x.CrossBilling!.Kg).NotNull().GreaterThan(0).When(x => x.CrossBilling!.Mode == CrossBillingMode.PerKg).WithName("Material (kg)");
            RuleFor(x => x.CrossBilling!.RatePerKg).NotNull().GreaterThan(0).When(x => x.CrossBilling!.Mode == CrossBillingMode.PerKg).WithName("Rate per kg");
        });
        RuleFor(x => x.CustomerReference).MaximumLength(100);
        RuleFor(x => x.Instructions).MaximumLength(1000);
        RuleFor(x => x.IndentNo).MaximumLength(60);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(m => m.MaterialName).MaximumLength(200);
            l.RuleFor(m => m.Unit).NotEmpty().MaximumLength(12);
            l.RuleFor(m => m.Quantity).GreaterThan(0);
            l.RuleFor(m => m.WeightKg).GreaterThan(0);
            l.RuleFor(m => m.InvoiceNo).MaximumLength(60);
            l.RuleFor(m => m.VolumeCuFt).GreaterThanOrEqualTo(0).When(m => m.VolumeCuFt is not null);
            l.RuleFor(m => m.NetWeightKg).GreaterThanOrEqualTo(0).When(m => m.NetWeightKg is not null);
            l.RuleFor(m => m).Must(m => m.NetWeightKg is null || m.NetWeightKg <= m.WeightKg).WithMessage("Net weight cannot be more than gross weight.").WithName("Net weight");
        }).When(x => x.Lines is not null);
    }
}

/// <summary>Names for the ids that appear on LR and trip screens, loaded in one pass for a page of rows.</summary>
internal sealed class Lookups(TransportOpsDbContext db)
{
    public Dictionary<Guid, string> Customers { get; private set; } = [];

    public Dictionary<Guid, OpsLocation> Locations { get; private set; } = [];

    public Dictionary<Guid, string> Trips { get; private set; } = [];

    public Dictionary<Guid, PodStatus> Pods { get; private set; } = [];

    public async Task<Lookups> LoadAsync(IReadOnlyCollection<Lr> lrs, CancellationToken cancellationToken)
    {
        var customerIds = lrs.Select(l => l.CustomerId).Distinct().ToList();
        var locationIds = lrs.SelectMany(l => new[] { l.CfaLocationId, l.DestinationLocationId }).Distinct().ToList();
        var tripIds = lrs.Where(l => l.TripId is not null).Select(l => l.TripId!.Value).Distinct().ToList();
        var lrIds = lrs.Select(l => l.Id).ToList();
        Customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        Locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        Trips = await db.Trips.AsNoTracking().Where(t => tripIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Number, cancellationToken);
        Pods = await db.Pods.AsNoTracking().Where(p => lrIds.Contains(p.LrId)).ToDictionaryAsync(p => p.LrId, p => p.Status, cancellationToken);
        return this;
    }

    public LrSummaryDto Summary(Lr l, DateTimeOffset now) =>
        new(l.Id, l.Number, l.CustomerId, Customers.GetValueOrDefault(l.CustomerId, "?"), l.CustomerReference, Locations.TryGetValue(l.CfaLocationId, out var cfa) ? cfa.Name : "?",
            Locations.TryGetValue(l.DestinationLocationId, out var d) ? d.Name : "?", d?.City ?? "?", l.TripId, l.TripId is { } t ? Trips.GetValueOrDefault(t) : null, l.VehicleRegistration, l.Status,
            Pods.GetValueOrDefault(l.Id, PodStatus.NotYetDue), l.DeliveryDate, l.TotalWeightKg, l.TotalPackages, l.CfaReachedAt, l.LoadingStartedAt, l.LoadingCompletedAt, l.DepartedAt, l.ArrivedAt, l.UnloadedAt,
            l.TokenActiveAt(now), l.CreatedAt, l.Movement);
}

internal sealed class LrHandler(TransportOpsDbContext db, OpsAccess access, ISequenceGenerator sequences, IOpsSettings settings, IFieldEncryptor encryptor, TimeProvider clock, UomHandler uoms, FileIntake files)
{
    /// <summary>Most invoice copies that can be kept against one LR.</summary>
    public const int MaxInvoiceFiles = 10;

    public async Task<Result<PagedResult<LrSummaryDto>>> ListAsync(
        string? search, DeliveryStatus? status, PodStatus? podStatus, Guid? customerId, Guid? tripId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Lrs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(l => l.Number.Contains(s) || (l.CustomerReference != null && l.CustomerReference.Contains(s)) || (l.VehicleRegistration != null && l.VehicleRegistration.Contains(s)));
        }

        if (status is { } st)
        {
            query = query.Where(l => l.Status == st);
        }

        if (podStatus is { } ps)
        {
            query = ps == PodStatus.NotYetDue
                ? query.Where(l => !db.Pods.Any(p => p.LrId == l.Id) || db.Pods.Any(p => p.LrId == l.Id && p.Status == PodStatus.NotYetDue))
                : query.Where(l => db.Pods.Any(p => p.LrId == l.Id && p.Status == ps));
        }

        if (customerId is { } c)
        {
            query = query.Where(l => l.CustomerId == c);
        }

        if (tripId is { } t)
        {
            query = query.Where(l => l.TripId == t);
        }

        if (from is { } f)
        {
            query = query.Where(l => l.DeliveryDate >= f);
        }

        if (to is { } e)
        {
            query = query.Where(l => l.DeliveryDate <= e);
        }

        var paged = await query.OrderByDescending(l => l.CreatedAt).ToPagedAsync(page, pageSize, cancellationToken);
        var lookups = await new Lookups(db).LoadAsync(paged.Items.ToList(), cancellationToken);
        var now = clock.GetUtcNow();
        return new PagedResult<LrSummaryDto>(paged.Items.Select(l => lookups.Summary(l, now)).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<Result<LrDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.AsNoTracking().Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return lr is null ? OpsErrors.LrNotFound : await DetailAsync(lr, cancellationToken);
    }

    public async Task<Result<CreatedLrDto>> CreateAsync(CreateLrRequest request, CancellationToken cancellationToken)
    {
        if (request.Movement == MovementType.CrossBilling && !access.Has(OpsPermissions.CrossBilling))
        {
            return Error.Forbidden("ops.crossbilling_forbidden", "You are not allowed to create cross-billing LRs.");
        }

        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        var tenant = access.TenantId!.Value;
        DeliveryRequest? source = null;
        Guid customerId, cfa, destination;
        string? reference, instructions;
        DateOnly? deliveryDate;
        List<(string? Code, string? Name, decimal Quantity, string Unit, decimal WeightKg, decimal? VolumeCbm, string? Dimensions, int Packages, decimal? VolumeCuFt, decimal? NetWeightKg, string? InvoiceNo)> lines;

        if (request.RequestId is { } rid)
        {
            source = await db.Requests.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == rid, cancellationToken);
            if (source is null)
            {
                return OpsErrors.RequestNotFound;
            }

            if (source.Status != RequestStatus.ReadyForDispatch)
            {
                return Error.Conflict("ops.request_not_ready", "Only a request that is ready for dispatch can become an LR. Validate it first.");
            }

            customerId = source.CustomerId;
            cfa = source.CfaLocationId!.Value;
            destination = source.DestinationLocationId!.Value;
            reference = source.CustomerReference;
            instructions = source.Instructions;
            deliveryDate = source.RequestedDate;
            lines = source.Lines.Select(l => (l.MaterialCode, (string?)l.MaterialName, l.Quantity, l.Unit, l.WeightKg, l.VolumeCbm, l.Dimensions, l.Packages, (decimal?)null, (decimal?)null, (string?)null)).ToList();
        }
        else
        {
            customerId = request.CustomerId!.Value;
            cfa = request.CfaLocationId!.Value;
            destination = request.DestinationLocationId!.Value;
            reference = request.CustomerReference;
            instructions = request.Instructions;
            deliveryDate = request.DeliveryDate;
            lines = request.Lines!.Select(l => (l.MaterialCode, l.MaterialName, l.Quantity, l.Unit, l.WeightKg, l.VolumeCbm, l.Dimensions, l.Packages, l.VolumeCuFt, l.NetWeightKg, l.InvoiceNo)).ToList();
        }

        if (source is null)
        {
            var allowed = await uoms.ActiveCodesAsync(cancellationToken);
            if (lines.FirstOrDefault(l => !allowed.Contains(l.Unit)) is { Unit: { } bad })
            {
                return Error.Validation("ops.uom_unknown", $"The unit {bad} is not in the unit-of-measure master. Pick one from the list.");
            }
        }

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return OpsErrors.CustomerNotFound;
        }

        if (customer.Status != ActiveStatus.Active)
        {
            return Error.Conflict("ops.customer_inactive", "This customer is not active.");
        }

        var places = await db.Locations.AsNoTracking().Where(l => l.Id == cfa || l.Id == destination).ToListAsync(cancellationToken);
        var from = places.FirstOrDefault(l => l.Id == cfa);
        var to = places.FirstOrDefault(l => l.Id == destination);
        if (from is null || to is null)
        {
            return OpsErrors.LocationNotFound;
        }

        if (to.Status != ActiveStatus.Active || from.Status != ActiveStatus.Active)
        {
            return Error.Conflict("ops.location_inactive", "The CFA or the destination is not active.");
        }

        var numbering = await settings.GetAsync(OpsSettingKeys.Numbering, new NumberingSetting(), cancellationToken);
        var arrival = await settings.GetAsync(OpsSettingKeys.Arrival, new ArrivalSetting(), cancellationToken);
        var number = Numbers.Format(numbering.LrPrefix, await sequences.NextAsync(tenant, "ops-lr", cancellationToken), numbering.Digits);
        var now = clock.GetUtcNow();
        var cross = request.Movement == MovementType.CrossBilling ? request.CrossBilling : null;
        var lr = Lr.Create(tenant, number, customerId, source?.Id, reference, cfa, destination, deliveryDate, instructions, lines, request.IndentNo, request.Movement ?? MovementType.Supply, cross?.Mode, cross?.Amount, cross?.Kg, cross?.RatePerKg);
        var token = Tokens.New();
        lr.IssueToken(Tokens.Hash(token), encryptor.Encrypt(token), now, now.AddDays(arrival.TokenValidityDays));
        db.Lrs.Add(lr);
        db.Pods.Add(Pod.Create(tenant, lr.Id));
        if (source is not null)
        {
            var converted = source.Convert(lr.Id);
            if (converted.IsFailure)
            {
                return converted.Error;
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (source is not null)
        {
            return Error.Conflict("ops.lr_duplicate", "An LR was already created for this request.");
        }

        return new CreatedLrDto(await DetailAsync(lr, cancellationToken), $"/q/{token}");
    }

    /// <summary>Keeps a copy of the invoice (a photo or PDF) against the LR. At most <see cref="MaxInvoiceFiles"/> per LR.</summary>
    public async Task<Result<FileDto>> AttachInvoiceCopyAsync(Guid id, Stream content, string? fileName, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        var existing = await db.Files.CountAsync(f => f.OwnerId == lr.Id, cancellationToken);
        if (existing >= MaxInvoiceFiles)
        {
            return Error.Validation("ops.file_limit", $"An LR can hold at most {MaxInvoiceFiles} invoice copies.");
        }

        var blob = await files.SaveEvidenceAsync(content, fileName, lr.TenantId, lr.Number, "INV", existing + 1, FileIntakeLimits.Attachment, cancellationToken);
        if (blob.IsFailure)
        {
            return blob.Error;
        }

        var file = StoredFile.Create(lr.TenantId, lr.Id, blob.Value.Key, blob.Value.SafeName, blob.Value.ContentType, blob.Value.SizeBytes, blob.Value.Sha256, clock.GetUtcNow(), access.UserId);
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

    /// <summary>The page the office prints. It carries the QR path, so only the office can produce it; each print is counted.</summary>
    public async Task<Result<LrPrintDto>> PrintAsync(Guid id, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        if (lr.TokenCipher is null || lr.TokenRevokedAt is not null || lr.Status == DeliveryStatus.Cancelled)
        {
            return Error.Conflict("ops.qr_revoked", "This LR's QR code has been revoked. Issue a new code before printing.");
        }

        var places = await db.Locations.AsNoTracking().Where(l => l.Id == lr.CfaLocationId || l.Id == lr.DestinationLocationId).ToListAsync(cancellationToken);
        var customer = await db.Customers.AsNoTracking().FirstAsync(c => c.Id == lr.CustomerId, cancellationToken);
        var trip = lr.TripId is { } t ? await db.Trips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == t, cancellationToken) : null;
        var cfa = places.First(p => p.Id == lr.CfaLocationId);
        var dest = places.First(p => p.Id == lr.DestinationLocationId);
        lr.Printed();
        await db.SaveChangesAsync(cancellationToken);
        static string Address(OpsLocation l) => $"{l.Line1}, {l.City}, {l.State}{(string.IsNullOrWhiteSpace(l.Pincode) ? string.Empty : " " + l.Pincode)}";
        return new LrPrintDto(lr.Id, lr.Number, customer.Name, lr.CustomerReference, cfa.Name, Address(cfa), dest.Name, Address(dest), lr.VehicleRegistration, trip?.DriverName, trip?.DriverPhone, lr.DeliveryDate,
            lr.Instructions, lr.Lines.Select(l => new MaterialLineDto(l.MaterialCode, l.MaterialName ?? string.Empty, l.Quantity, l.Unit, l.WeightKg, l.VolumeCbm, l.Dimensions, l.Packages, null, l.VolumeCuFt, l.NetWeightKg, l.InvoiceNo)).ToList(), lr.TotalWeightKg, lr.TotalPackages,
            $"/q/{encryptor.Decrypt(lr.TokenCipher)}", lr.PrintCount, trip?.Number, lr.IndentNo, lr.CreatedAt);
    }

    /// <summary>A new code replaces the old one at once: a lost or leaked printout stops working.</summary>
    public async Task<Result<CreatedLrDto>> ReissueQrAsync(Guid id, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        if (lr.Status == DeliveryStatus.Cancelled)
        {
            return Error.Conflict("ops.lr_state", "A cancelled LR has no QR code.");
        }

        var arrival = await settings.GetAsync(OpsSettingKeys.Arrival, new ArrivalSetting(), cancellationToken);
        var now = clock.GetUtcNow();
        var token = Tokens.New();
        lr.IssueToken(Tokens.Hash(token), encryptor.Encrypt(token), now, now.AddDays(arrival.TokenValidityDays));
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedLrDto(await DetailAsync(lr, cancellationToken), $"/q/{token}");
    }

    public async Task<Result<LrDetailDto>> RevokeQrAsync(Guid id, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        lr.RevokeToken(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(lr, cancellationToken);
    }

    public async Task<Result<LrDetailDto>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        var result = lr.Cancel();
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(lr, cancellationToken);
    }

    /// <summary>
    /// Removes an LR that was created by mistake. Only while nothing has happened to it: it has not left the CFA, and has no recorded events, POD, charges or invoice.
    /// Anything further is cancelled instead, so the history stays.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var lr = await db.Lrs.Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lr is null)
        {
            return OpsErrors.LrNotFound;
        }

        if (lr.Status is not (DeliveryStatus.Created or DeliveryStatus.Assigned or DeliveryStatus.Cancelled))
        {
            return Error.Conflict("ops.lr_delete_state", "An LR that has started loading or left the CFA cannot be deleted.");
        }

        if (await db.Events.AnyAsync(e => e.LrId == id, cancellationToken)
            || await db.Calculations.AnyAsync(c => c.LrId == id, cancellationToken)
            || await db.InvoiceLines.AnyAsync(l => l.LrId == id, cancellationToken))
        {
            return Error.Conflict("ops.lr_delete_used", "This LR already has events, charges or an invoice, so it cannot be deleted. Cancel it instead.");
        }

        var pod = await db.Pods.Include(p => p.Documents).FirstOrDefaultAsync(p => p.LrId == id, cancellationToken);
        if (pod is { Documents.Count: > 0 })
        {
            return Error.Conflict("ops.lr_delete_used", "This LR already has proof of delivery, so it cannot be deleted. Cancel it instead.");
        }

        var stored = await db.Files.Where(f => f.OwnerId == id).ToListAsync(cancellationToken);
        if (pod is not null)
        {
            db.Pods.Remove(pod);
        }

        db.Files.RemoveRange(stored);
        db.Lrs.Remove(lr);
        await db.SaveChangesAsync(cancellationToken);

        // Blobs go last: a failed delete leaves an orphan object, never a record pointing at nothing.
        foreach (var file in stored)
        {
            await files.DeleteAsync(file.FileKey, cancellationToken);
        }

        return Result.Success();
    }

    private async Task<LrDetailDto> DetailAsync(Lr lr, CancellationToken cancellationToken)
    {
        var lookups = await new Lookups(db).LoadAsync([lr], cancellationToken);
        var events = await db.Events.AsNoTracking().Where(e => e.LrId == lr.Id || (lr.TripId != null && e.TripId == lr.TripId && e.LrId == null)).OrderBy(e => e.ActualAt).ThenBy(e => e.RecordedAt).ToListAsync(cancellationToken);
        var files = (await db.Files.AsNoTracking().Where(f => f.OwnerId == lr.Id).OrderBy(f => f.UploadedAt).ToListAsync(cancellationToken))
            .Select(f => new FileDto(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.UploadedAt)).ToList();
        return new LrDetailDto(lookups.Summary(lr, clock.GetUtcNow()), lr.Instructions, lr.TokenExpiresAt, lr.PrintCount,
            lr.Lines.Select(l => new MaterialLineDto(l.MaterialCode, l.MaterialName ?? string.Empty, l.Quantity, l.Unit, l.WeightKg, l.VolumeCbm, l.Dimensions, l.Packages, null, l.VolumeCuFt, l.NetWeightKg, l.InvoiceNo)).ToList(), events.Select(ToDto).ToList(), lr.RequestId, lr.Version, lr.IndentNo, files, CrossBillingTerms(lr));
    }

    private CrossBillingDto? CrossBillingTerms(Lr lr) =>
        lr.Movement == MovementType.CrossBilling && lr.CrossMode is { } mode && (access.Has(OpsPermissions.BillingRead) || access.Has(OpsPermissions.CrossBilling))
            ? new CrossBillingDto(mode, lr.CrossAmount, lr.CrossKg, lr.CrossRatePerKg, lr.CrossFreight)
            : null;

    internal static EventDto ToDto(DeliveryEvent e) =>
        new(e.Id, e.Type, e.ActualAt, e.RecordedAt, e.Source, e.UserId, e.Remarks, e.Verification, e.DistanceM, e.AccuracyM, e.RadiusM, e.CorrectsEventId, e.TripId);
}

public sealed record TripStopDto(Guid Id, int Sequence, Guid DestinationLocationId, string DestinationName, string DestinationCity, DateTimeOffset? ArrivedAt, DateTimeOffset? UnloadedAt, IReadOnlyList<LrSummaryDto> Lrs);

public sealed record TripSummaryDto(
    Guid Id, string Number, string CfaName, string VehicleRegistration, string? DriverName, string? DriverPhone, DateOnly? PlannedDate, TripStatus Status, int StopCount, int LrCount,
    DateTimeOffset? CfaReachedAt, DateTimeOffset? LoadingStartedAt, DateTimeOffset? LoadingCompletedAt, DateTimeOffset? DepartedAt, double? LoadingMinutes, double? CfaTurnaroundMinutes);

public sealed record TripDetailDto(TripSummaryDto Trip, Guid CfaLocationId, Guid? VehicleId, Guid? TransporterId, string? Remarks, IReadOnlyList<TripStopDto> Stops, IReadOnlyList<EventDto> Events, long Version);

public sealed record CreateTripRequest(
    Guid CfaLocationId, Guid? VehicleId, string? VehicleRegistration, Guid? TransporterId, string? DriverName, string? DriverPhone, DateOnly? PlannedDate, string? Remarks, IReadOnlyList<Guid> LrIds);

public sealed record UpdateTripRequest(Guid? VehicleId, string? VehicleRegistration, Guid? TransporterId, string? DriverName, string? DriverPhone, DateOnly? PlannedDate, string? Remarks, long Version);

public sealed record AddLrToTripRequest(Guid LrId);

public sealed record CfaEventRequest(EventType Type, DateTimeOffset? ActualAt, string? Remarks, string? ClientKey);

public sealed record CorrectEventRequest(EventType Type, DateTimeOffset NewActualAt, string Reason);

public sealed record VehicleOptionDto(Guid Id, Guid TransporterId, string RegistrationNumber, string VehicleTypeName, int PayloadKg, bool Available, string? Note);

internal sealed class CreateTripRequestValidator : AbstractValidator<CreateTripRequest>
{
    public CreateTripRequestValidator()
    {
        RuleFor(x => x.LrIds).NotEmpty().WithMessage("Choose at least one LR for the trip.").Must(l => l.Count <= 30).WithMessage("A trip can carry at most 30 LRs.");
        RuleFor(x => x).Must(x => x.VehicleId is not null || !string.IsNullOrWhiteSpace(x.VehicleRegistration)).WithMessage("Choose a vehicle or enter its registration number.").WithName("Vehicle");
        RuleFor(x => x.VehicleRegistration).MaximumLength(20);
        RuleFor(x => x.DriverName).MaximumLength(150);
        RuleFor(x => x.DriverPhone).Must(p => IndianIdentifiers.IsValidMobile(p)).When(x => !string.IsNullOrWhiteSpace(x.DriverPhone)).WithMessage("Enter a valid mobile number.");
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}

internal sealed class CfaEventRequestValidator : AbstractValidator<CfaEventRequest>
{
    public CfaEventRequestValidator()
    {
        RuleFor(x => x.Type).Must(t => t is EventType.CfaReached or EventType.LoadingStarted or EventType.LoadingCompleted or EventType.CfaDeparted).WithMessage("This is not a CFA event.");
        RuleFor(x => x.Remarks).MaximumLength(500);
        RuleFor(x => x.ClientKey).MaximumLength(100);
    }
}

internal sealed class CorrectEventRequestValidator : AbstractValidator<CorrectEventRequest>
{
    public CorrectEventRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500).WithMessage("A reason is required to correct a time.");
    }
}

internal sealed class TripHandler(TransportOpsDbContext db, OpsAccess access, ISequenceGenerator sequences, IOpsSettings settings, IFleetDirectory fleet, TimeProvider clock)
{
    private static readonly TimeSpan FutureSlack = TimeSpan.FromMinutes(5);

    public async Task<Result<PagedResult<TripSummaryDto>>> ListAsync(string? search, TripStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Trips.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(t => t.Number.Contains(s) || t.VehicleRegistration.Contains(s) || (t.DriverName != null && t.DriverName.Contains(s)));
        }

        if (status is { } st)
        {
            query = query.Where(t => t.Status == st);
        }

        var paged = await query.OrderByDescending(t => t.CreatedAt).ToPagedAsync(page, pageSize, cancellationToken);
        var ids = paged.Items.Select(t => t.Id).ToList();
        var stops = await db.TripStops.AsNoTracking().Where(s => ids.Contains(s.TripId)).GroupBy(s => s.TripId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var lrs = await db.Lrs.AsNoTracking().Where(l => l.TripId != null && ids.Contains(l.TripId.Value)).GroupBy(l => l.TripId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var cfas = await CfaNamesAsync(paged.Items.Select(t => t.CfaLocationId), cancellationToken);
        return new PagedResult<TripSummaryDto>(paged.Items.Select(t => Summary(t, cfas, stops.GetValueOrDefault(t.Id), lrs.GetValueOrDefault(t.Id))).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<Result<TripDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var trip = await db.Trips.AsNoTracking().Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return trip is null ? OpsErrors.TripNotFound : await DetailAsync(trip, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<VehicleOptionDto>>> VehiclesAsync(Guid? transporterId, CancellationToken cancellationToken)
    {
        var owner = transporterId ?? (await settings.GetAsync(OpsSettingKeys.Fleet, new FleetSetting(), cancellationToken)).OperatingTransporterId;
        if (owner is null)
        {
            return Error.Validation("ops.no_fleet", "Choose the transporter whose vehicles are used (Settings), or enter the registration number by hand.");
        }

        var today = clock.TodayInIndia();
        var vehicles = await fleet.ListVehiclesAsync(owner.Value, cancellationToken);
        return vehicles.Where(v => v.IsActive).OrderBy(v => v.RegistrationNumber)
            .Select(v => new VehicleOptionDto(v.Id, v.TransporterId, v.RegistrationNumber, v.VehicleTypeName, v.PayloadKg, v.IsAvailableOn(today), v.IsAvailableOn(today) ? null : v.WhyUnavailableOn(today))).ToList();
    }

    public async Task<Result<TripDetailDto>> CreateAsync(CreateTripRequest request, CancellationToken cancellationToken)
    {
        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        var tenant = access.TenantId!.Value;
        var vehicle = await ResolveVehicleAsync(request.VehicleId, request.VehicleRegistration, cancellationToken);
        if (vehicle.IsFailure)
        {
            return vehicle.Error;
        }

        var cfa = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.CfaLocationId, cancellationToken);
        if (cfa is null)
        {
            return OpsErrors.LocationNotFound;
        }

        var ids = request.LrIds.Distinct().ToList();
        var lrs = await db.Lrs.Where(l => ids.Contains(l.Id)).ToListAsync(cancellationToken);
        if (lrs.Count != ids.Count)
        {
            return OpsErrors.LrNotFound;
        }

        if (lrs.Any(l => l.Status != DeliveryStatus.Created))
        {
            return Error.Conflict("ops.lr_not_free", "One or more LRs are already on a trip or have been cancelled.");
        }

        if (lrs.Any(l => l.CfaLocationId != cfa.Id))
        {
            return Error.Validation("ops.lr_cfa_mismatch", "Every LR on a trip must load at the trip's CFA.");
        }

        var numbering = await settings.GetAsync(OpsSettingKeys.Numbering, new NumberingSetting(), cancellationToken);
        var number = Numbers.Format(numbering.TripPrefix, await sequences.NextAsync(tenant, "ops-trip", cancellationToken), numbering.Digits);
        var trip = Trip.Create(tenant, number, cfa.Id, vehicle.Value.Id, vehicle.Value.Registration, vehicle.Value.TransporterId, request.DriverName, request.DriverPhone, request.PlannedDate, request.Remarks);
        // One stop per distinct destination, in the order the LRs were chosen.
        var order = ids.Select(i => lrs.First(l => l.Id == i)).ToList();
        foreach (var destination in order.Select(l => l.DestinationLocationId).Distinct())
        {
            var stop = trip.AddStop(destination);
            foreach (var lr in order.Where(l => l.DestinationLocationId == destination))
            {
                var assigned = lr.AssignTrip(trip.Id, stop.Id, trip.VehicleRegistration);
                if (assigned.IsFailure)
                {
                    return assigned.Error;
                }
            }
        }

        db.Trips.Add(trip);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(trip, cancellationToken);
    }

    public async Task<Result<TripDetailDto>> UpdateAsync(Guid id, UpdateTripRequest request, CancellationToken cancellationToken)
    {
        var trip = await db.Trips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return OpsErrors.TripNotFound;
        }

        var vehicle = await ResolveVehicleAsync(request.VehicleId, request.VehicleRegistration, cancellationToken);
        if (vehicle.IsFailure)
        {
            return vehicle.Error;
        }

        db.Entry(trip).Property(t => t.Version).OriginalValue = request.Version;
        var updated = trip.Update(vehicle.Value.Registration, vehicle.Value.Id, vehicle.Value.TransporterId, request.DriverName, request.DriverPhone, request.PlannedDate, request.Remarks);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        foreach (var lr in await db.Lrs.Where(l => l.TripId == trip.Id).ToListAsync(cancellationToken))
        {
            lr.AssignTrip(trip.Id, lr.TripStopId!.Value, trip.VehicleRegistration);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OpsErrors.ConcurrencyConflict;
        }

        return await DetailAsync(trip, cancellationToken);
    }

    /// <summary>Records one of the four CFA events by office staff. No driver location is involved.</summary>
    public async Task<Result<TripDetailDto>> RecordCfaEventAsync(Guid id, CfaEventRequest request, CancellationToken cancellationToken)
    {
        var trip = await db.Trips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return OpsErrors.TripNotFound;
        }

        if (!string.IsNullOrWhiteSpace(request.ClientKey) && await db.Events.AnyAsync(e => e.ClientKey == request.ClientKey, cancellationToken))
        {
            return await DetailAsync(trip, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var at = request.ActualAt ?? now;
        if (at > now + FutureSlack)
        {
            return Error.Validation("ops.event_future", "An event cannot be in the future.");
        }

        if (!await db.Lrs.AnyAsync(l => l.TripId == trip.Id && l.Status != DeliveryStatus.Cancelled, cancellationToken))
        {
            return Error.Conflict("ops.trip_empty", "The trip has no LRs.");
        }

        var recorded = trip.RecordCfaEvent(request.Type, at);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        foreach (var lr in await db.Lrs.Where(l => l.TripId == trip.Id && l.Status != DeliveryStatus.Cancelled).ToListAsync(cancellationToken))
        {
            lr.ApplyCfaEvent(request.Type, at);
        }

        db.Events.Add(DeliveryEvent.Create(trip.TenantId, trip.Id, null, request.Type, at, now, EventSource.Office, access.UserId, request.Remarks, Clean(request.ClientKey)));
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(trip, cancellationToken);
    }

    /// <summary>Changes a CFA time already recorded. The correction is its own event (with the reason), so the history shows what was recorded first and what it was changed to.</summary>
    public async Task<Result<TripDetailDto>> CorrectCfaEventAsync(Guid id, CorrectEventRequest request, CancellationToken cancellationToken)
    {
        var trip = await db.Trips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return OpsErrors.TripNotFound;
        }

        var now = clock.GetUtcNow();
        if (request.NewActualAt > now + FutureSlack)
        {
            return Error.Validation("ops.event_future", "An event cannot be in the future.");
        }

        var corrected = trip.CorrectCfaTime(request.Type, request.NewActualAt);
        if (corrected.IsFailure)
        {
            return corrected.Error;
        }

        var original = await db.Events.Where(e => e.TripId == trip.Id && e.Type == request.Type && e.CorrectsEventId == null).OrderByDescending(e => e.RecordedAt).FirstOrDefaultAsync(cancellationToken);
        foreach (var lr in await db.Lrs.Where(l => l.TripId == trip.Id).ToListAsync(cancellationToken))
        {
            lr.SetTime(request.Type, request.NewActualAt);
        }

        db.Events.Add(DeliveryEvent.Create(trip.TenantId, trip.Id, null, EventType.Correction, request.NewActualAt, now, EventSource.Office, access.UserId, $"{request.Type}: {request.Reason}", null, null, original?.Id));
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(trip, cancellationToken);
    }

    public async Task<Result<TripDetailDto>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var trip = await db.Trips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (trip is null)
        {
            return OpsErrors.TripNotFound;
        }

        var cancelled = trip.Cancel();
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        foreach (var lr in await db.Lrs.Where(l => l.TripId == trip.Id).ToListAsync(cancellationToken))
        {
            lr.Unassign();
        }

        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(trip, cancellationToken);
    }

    private async Task<Result<(Guid? Id, string Registration, Guid? TransporterId)>> ResolveVehicleAsync(Guid? vehicleId, string? registration, CancellationToken cancellationToken)
    {
        if (vehicleId is { } id)
        {
            var vehicle = await fleet.GetVehicleAsync(id, cancellationToken);
            if (vehicle is null || !vehicle.IsActive)
            {
                return Error.NotFound("ops.vehicle_not_found", "Vehicle not found or not active.");
            }

            var why = vehicle.WhyUnavailableOn(clock.TodayInIndia());
            if (!vehicle.IsAvailableOn(clock.TodayInIndia()))
            {
                return Error.Conflict("ops.vehicle_unavailable", $"{vehicle.RegistrationNumber} is {why}.");
            }

            return (vehicle.Id, vehicle.RegistrationNumber, vehicle.TransporterId);
        }

        var normalised = IndianIdentifiers.NormaliseVehicleRegistration(registration);
        if (!IndianIdentifiers.IsValidVehicleRegistration(normalised))
        {
            return Error.Validation("ops.vehicle_registration", "Enter a valid vehicle registration number, for example MH12AB1234.");
        }

        return ((Guid?)null, normalised, (Guid?)null);
    }

    private async Task<Dictionary<Guid, string>> CfaNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        return await db.Locations.AsNoTracking().Where(l => wanted.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
    }

    private static TripSummaryDto Summary(Trip t, IReadOnlyDictionary<Guid, string> cfas, int stops, int lrs) =>
        new(t.Id, t.Number, cfas.GetValueOrDefault(t.CfaLocationId, "?"), t.VehicleRegistration, t.DriverName, t.DriverPhone, t.PlannedDate, t.Status, stops, lrs, t.CfaReachedAt, t.LoadingStartedAt,
            t.LoadingCompletedAt, t.DepartedAt, Minutes(t.LoadingStartedAt, t.LoadingCompletedAt), Minutes(t.CfaReachedAt, t.DepartedAt));

    internal static double? Minutes(DateTimeOffset? from, DateTimeOffset? to) => from is { } f && to is { } e && e >= f ? Math.Round((e - f).TotalMinutes, 1) : null;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<TripDetailDto> DetailAsync(Trip trip, CancellationToken cancellationToken)
    {
        var lrs = await db.Lrs.AsNoTracking().Where(l => l.TripId == trip.Id).OrderBy(l => l.Number).ToListAsync(cancellationToken);
        var lookups = await new Lookups(db).LoadAsync(lrs, cancellationToken);
        var destinationIds = trip.Stops.Select(s => s.DestinationLocationId).ToList();
        var destinations = await db.Locations.AsNoTracking().Where(l => destinationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
        var now = clock.GetUtcNow();
        var stops = trip.Stops.OrderBy(s => s.Sequence).Select(s => new TripStopDto(s.Id, s.Sequence, s.DestinationLocationId, destinations.TryGetValue(s.DestinationLocationId, out var d) ? d.Name : "?", d?.City ?? "?",
            s.ArrivedAt, s.UnloadedAt, lrs.Where(l => l.TripStopId == s.Id).Select(l => lookups.Summary(l, now)).ToList())).ToList();
        var lrIds = lrs.Select(l => l.Id).ToList();
        var events = await db.Events.AsNoTracking().Where(e => e.TripId == trip.Id || (e.LrId != null && lrIds.Contains(e.LrId.Value))).OrderBy(e => e.ActualAt).ThenBy(e => e.RecordedAt).ToListAsync(cancellationToken);
        return new TripDetailDto(Summary(trip, lookups.Locations.ToDictionary(x => x.Key, x => x.Value.Name).Concat(await CfaNamesAsync([trip.CfaLocationId], cancellationToken)).DistinctBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value),
            trip.Stops.Count, lrs.Count), trip.CfaLocationId, trip.VehicleId, trip.TransporterId, trip.Remarks, stops, events.Select(LrHandler.ToDto).ToList(), trip.Version);
    }
}
