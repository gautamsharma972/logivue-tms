using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Application.Shipments;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Files;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application.Delivery;

internal sealed record PodFile(Stream Content, string FileName, string ContentType);

/// <summary>Recording a delivery, attaching and reviewing proof of it. Vendors act only on their own shipments; staff review.</summary>
internal sealed class DeliveryHandler(
    ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader, ICurrentUser user, IFileStore files, TimeProvider clock)
{
    private static readonly Error DocumentNotFound = Error.NotFound("pod.document_not_found", "Document not found.");

    public async Task<Result<ShipmentDto>> RecordAsync(Guid shipmentId, Guid orderId, RecordDeliveryRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanRespond)
        {
            return ShipmentAccess.Forbidden;
        }

        var found = await loader.FindAsync(shipmentId, tracked: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return ShipmentAccess.OrderNotFound;
        }

        var now = clock.GetUtcNow();
        var recorded = found.Value.RecordDelivery(order, new DeliveryDetails(request.DeliveredAt ?? now, request.ReceiverName, request.DeliveredPackages, request.DamagedPackages, request.Remarks), now);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(found.Value, cancellationToken);
    }

    public async Task<Result<PodDocumentDto>> UploadAsync(Guid shipmentId, Guid orderId, UploadPodForm form, CancellationToken cancellationToken)
    {
        if (!access.CanRespond || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var found = await loader.FindAsync(shipmentId, tracked: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var shipment = found.Value;
        if (form.File is not { Length: > 0 } file)
        {
            return Error.Validation("pod.file_required", "Attach a file.");
        }

        if (file.Length > FileSniffer.MaxBytes)
        {
            return Error.Validation("pod.file_too_large", "Files can be at most 10 MB.");
        }

        if (shipment.Orders.All(l => l.OrderId != orderId))
        {
            return Error.NotFound("shipments.order_not_found", "That order is not on this shipment.");
        }

        if (await db.PodDocuments.CountAsync(d => d.ShipmentId == shipmentId && d.OrderId == orderId, cancellationToken) >= PodDocument.MaxPerOrder)
        {
            return Error.Conflict("pod.too_many", $"At most {PodDocument.MaxPerOrder} files can be attached to one delivery.");
        }

        var head = new byte[8];
        await using var upload = file.OpenReadStream();
        var read = await upload.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        if (FileSniffer.Identify(head.AsSpan(0, read)) is not var (contentType, extension))
        {
            return Error.Validation("pod.file_type", "Upload a PDF, JPG or PNG file.");
        }

        var added = shipment.ProofAdded(orderId); // refuses before delivery and after verification
        if (added.IsFailure)
        {
            return added.Error;
        }

        upload.Position = 0;
        var key = $"{tenantId}/pod/{shipmentId}/{Guid.CreateVersion7()}{extension}";
        var fileName = Path.GetFileName(file.FileName);
        var document = PodDocument.Create(
            tenantId, shipmentId, orderId, shipment.TransporterId!.Value, key,
            string.IsNullOrWhiteSpace(fileName) ? $"pod{extension}" : fileName[..Math.Min(fileName.Length, 255)], contentType, file.Length);

        await files.SaveAsync(key, upload, cancellationToken);
        try
        {
            db.PodDocuments.Add(document);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(key, CancellationToken.None); // no orphaned blob
            throw;
        }

        return ToDto(document);
    }

    public async Task<Result<IReadOnlyList<PodDocumentDto>>> ListDocumentsAsync(Guid shipmentId, Guid orderId, CancellationToken cancellationToken)
    {
        var found = await loader.FindAsync(shipmentId, tracked: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var documents = await db.PodDocuments.AsNoTracking().Where(d => d.ShipmentId == shipmentId && d.OrderId == orderId).OrderBy(d => d.CreatedAt).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<PodDocumentDto>>(documents.Select(ToDto).ToList());
    }

    public async Task<Result<PodFile>> DownloadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.PodDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null || !await CanSeeAsync(document.ShipmentId, cancellationToken))
        {
            return DocumentNotFound;
        }

        var stream = await files.OpenReadAsync(document.FileKey, cancellationToken);
        return stream is null ? Error.NotFound("pod.file_missing", "The stored file could not be found.") : new PodFile(stream, document.FileName, document.ContentType);
    }

    public async Task<Result> DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (!access.CanRespond)
        {
            return ShipmentAccess.Forbidden;
        }

        var document = await db.PodDocuments.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        var found = document is null ? null : await loader.FindAsync(document.ShipmentId, tracked: true, cancellationToken);
        if (document is null || found is null || found.IsFailure)
        {
            return DocumentNotFound;
        }

        var remaining = await db.PodDocuments.CountAsync(d => d.ShipmentId == document.ShipmentId && d.OrderId == document.OrderId && d.Id != documentId, cancellationToken);
        var removed = found.Value.ProofRemoved(document.OrderId, remaining);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        db.PodDocuments.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        await files.DeleteAsync(document.FileKey, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ShipmentDto>> VerifyAsync(Guid shipmentId, Guid orderId, CancellationToken cancellationToken) =>
        await ReviewAsync(shipmentId, orderId, (shipment, order, now) => shipment.VerifyProof(order, user.UserId, now), cancellationToken);

    public async Task<Result<ShipmentDto>> RejectAsync(Guid shipmentId, Guid orderId, ReasonRequest request, CancellationToken cancellationToken) =>
        await ReviewAsync(shipmentId, orderId, (shipment, order, now) => shipment.RejectProof(order.Id, user.UserId, request.Reason, now), cancellationToken);

    private async Task<Result<ShipmentDto>> ReviewAsync(Guid shipmentId, Guid orderId, Func<Shipment, Order, DateTimeOffset, Result> review, CancellationToken cancellationToken)
    {
        if (!access.CanVerifyPod)
        {
            return ShipmentAccess.Forbidden;
        }

        var found = await loader.FindAsync(shipmentId, tracked: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return ShipmentAccess.OrderNotFound;
        }

        var reviewed = review(found.Value, order, clock.GetUtcNow());
        if (reviewed.IsFailure)
        {
            return reviewed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await loader.ToDtoAsync(found.Value, cancellationToken);
    }

    private async Task<bool> CanSeeAsync(Guid shipmentId, CancellationToken cancellationToken) =>
        (await loader.FindAsync(shipmentId, tracked: false, cancellationToken)).IsSuccess;

    private static PodDocumentDto ToDto(PodDocument d) => new(d.Id, d.ShipmentId, d.OrderId, d.FileName, d.ContentType, d.SizeBytes, d.CreatedAt);
}

/// <summary>The worklist of deliveries and their proof: for staff everything, for a vendor only its own shipments.</summary>
internal sealed class PodQueueHandler(ShipmentsDbContext db, ShipmentAccess access, ITransporterDirectory transporters, TimeProvider clock)
{
    private static IQueryable<PodRow> Rows(ShipmentsDbContext db) =>
        from l in db.ShipmentOrders.AsNoTracking()
        join s in db.Shipments.AsNoTracking() on l.ShipmentId equals s.Id
        join o in db.Orders.AsNoTracking() on l.OrderId equals o.Id
        where s.Status == ShipmentStatus.Dispatched || s.Status == ShipmentStatus.Delivered
        select new PodRow { Link = l, Shipment = s, Order = o };

    /// <summary>Member-initialised (not a constructor) so EF can keep filtering and paging over it in SQL.</summary>
    private sealed class PodRow
    {
        public required ShipmentOrder Link { get; init; }

        public required Shipment Shipment { get; init; }

        public required Order Order { get; init; }
    }

    public async Task<Result<PagedResult<PodLineDto>>> HandleAsync(ListPodQuery query, CancellationToken cancellationToken)
    {
        var rows = Rows(db);
        if (access.IsVendor)
        {
            if (!access.CanRespond || access.VendorTransporterId is not { } mine)
            {
                return ShipmentAccess.Forbidden;
            }

            rows = rows.Where(r => r.Shipment.TransporterId == mine);
        }
        else if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }
        else if (query.TransporterId is { } transporterId)
        {
            rows = rows.Where(r => r.Shipment.TransporterId == transporterId);
        }

        rows = query.Stage switch
        {
            PodStage.DeliveryPending => rows.Where(r => r.Link.DeliveredAt == null),
            PodStage.AwaitingProof => rows.Where(r => r.Link.DeliveredAt != null && r.Link.PodStatus == PodStatus.Awaiting),
            PodStage.ProofUploaded => rows.Where(r => r.Link.DeliveredAt != null && r.Link.PodStatus == PodStatus.Uploaded),
            PodStage.ProofRejected => rows.Where(r => r.Link.DeliveredAt != null && r.Link.PodStatus == PodStatus.Rejected),
            PodStage.ProofVerified => rows.Where(r => r.Link.DeliveredAt != null && r.Link.PodStatus == PodStatus.Verified),
            _ => rows,
        };

        var cutoff = clock.GetUtcNow().AddDays(-AgeingHandler.DefaultOverdueDays);
        if (query.OverdueOnly == true)
        {
            rows = rows.Where(r => r.Link.DeliveredAt != null && r.Link.PodStatus != PodStatus.Verified && r.Link.DeliveredAt <= cutoff);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(r => r.Shipment.Number.Contains(term) || r.Order.Number.Contains(term) || (r.Link.LrNumber != null && r.Link.LrNumber.Contains(term)) || r.Order.DropCity.Contains(term));
        }

        // Oldest outstanding first: that is the one to chase.
        var page = await rows.OrderBy(r => r.Link.DeliveredAt == null).ThenBy(r => r.Link.DeliveredAt).ThenBy(r => r.Shipment.Number)
            .ToPagedAsync(query.Page, query.PageSize, cancellationToken);

        var documentCounts = await CountsAsync(page.Items.Select(r => r.Shipment.Id).Distinct().ToList(), cancellationToken);
        var names = await transporters.GetAsync(page.Items.Where(r => r.Shipment.TransporterId != null).Select(r => r.Shipment.TransporterId!.Value), cancellationToken);
        var today = clock.TodayInIndia();

        return new PagedResult<PodLineDto>(page.Items.Select(r =>
        {
            var l = r.Link;
            int? age = l.DeliveredAt is { } at ? Math.Max(0, today.DayNumber - DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromMinutes(330)).DateTime).DayNumber) : null;
            var stage = l.DeliveredAt is null ? PodStage.DeliveryPending : l.PodStatus switch
            {
                PodStatus.Uploaded => PodStage.ProofUploaded,
                PodStatus.Verified => PodStage.ProofVerified,
                PodStatus.Rejected => PodStage.ProofRejected,
                _ => PodStage.AwaitingProof,
            };
            return new PodLineDto(
                r.Shipment.Id, r.Shipment.Number, r.Order.Id, r.Order.Number, l.LrNumber, r.Order.Drop.Name, r.Order.DropCity, r.Shipment.TransporterId,
                r.Shipment.TransporterId is { } t && names.TryGetValue(t, out var n) ? n.LegalName : null, stage, l.DeliveredAt, l.ReceiverName, l.PackagesShipped, l.DeliveredPackages,
                l.DamagedPackages, l.ShortagePackages, l.HasException, age, age is { } a && a >= AgeingHandler.DefaultOverdueDays && l.PodStatus != PodStatus.Verified,
                l.PodRejectionReason, documentCounts.GetValueOrDefault((r.Shipment.Id, r.Order.Id)));
        }).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    private async Task<Dictionary<(Guid, Guid), int>> CountsAsync(IReadOnlyList<Guid> shipmentIds, CancellationToken cancellationToken)
    {
        var rows = await db.PodDocuments.AsNoTracking().Where(d => shipmentIds.Contains(d.ShipmentId)).GroupBy(d => new { d.ShipmentId, d.OrderId })
            .Select(g => new { g.Key.ShipmentId, g.Key.OrderId, Count = g.Count() }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => (r.ShipmentId, r.OrderId), r => r.Count);
    }
}

/// <summary>How long delivered goods have waited for verified proof, in buckets and by transporter.</summary>
internal sealed class AgeingHandler(ShipmentsDbContext db, ShipmentAccess access, ITransporterDirectory transporters, TimeProvider clock)
{
    public const int DefaultOverdueDays = 7;

    private static readonly (string Label, int Min, int? Max)[] Buckets = [("0–3 days", 0, 3), ("4–7 days", 4, 7), ("8–15 days", 8, 15), ("16–30 days", 16, 30), ("Over 30 days", 31, null)];

    /// <summary>Buckets the outstanding deliveries by how long they have waited, and ranks transporters by what is overdue.</summary>
    internal static AgeingDto Summarise(IReadOnlyList<(int Age, Guid? TransporterId, bool Exception)> aged, int overdueDays, Func<Guid?, string> nameOf) => new(
        overdueDays, aged.Count, aged.Count(a => a.Age >= overdueDays), aged.Count(a => a.Exception),
        Buckets.Select(b => new AgeingBucketDto(b.Label, aged.Count(a => a.Age >= b.Min && (b.Max is null || a.Age <= b.Max)))).ToList(),
        aged.GroupBy(a => a.TransporterId).Select(g => new TransporterAgeingDto(g.Key, nameOf(g.Key), g.Count(), g.Count(a => a.Age >= overdueDays), g.Max(a => a.Age)))
            .OrderByDescending(t => t.Overdue).ThenByDescending(t => t.OldestDays).Take(10).ToList());

    public async Task<Result<AgeingDto>> HandleAsync(AgeingQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var overdueDays = Math.Clamp(query.OverdueDays, 1, 90);
        var outstanding = await (
            from l in db.ShipmentOrders.AsNoTracking()
            join s in db.Shipments.AsNoTracking() on l.ShipmentId equals s.Id
            where l.DeliveredAt != null && l.PodStatus != PodStatus.Verified
            select new { l.DeliveredAt, s.TransporterId, l.PackagesShipped, l.DeliveredPackages, l.DamagedPackages }).Take(5000).ToListAsync(cancellationToken);

        var today = clock.TodayInIndia();
        var aged = outstanding.Select(x => (Age: Math.Max(0, today.DayNumber - DateOnly.FromDateTime(x.DeliveredAt!.Value.ToOffset(TimeSpan.FromMinutes(330)).DateTime).DayNumber), x.TransporterId,
            Exception: (x.PackagesShipped - x.DeliveredPackages) > 0 || x.DamagedPackages > 0)).ToList();

        var names = await transporters.GetAsync(aged.Where(a => a.TransporterId != null).Select(a => a.TransporterId!.Value).Distinct(), cancellationToken);
        return Summarise(aged, overdueDays, id => id is { } i && names.TryGetValue(i, out var n) ? n.LegalName : "Unknown transporter");
    }
}
