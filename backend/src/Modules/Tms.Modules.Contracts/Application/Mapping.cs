using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Contracts.Application;

internal static class Mapping
{
    public static ContractSummaryDto ToSummary(this Contract c, string transporterName, DateOnly today) =>
        new(c.Id, c.Number, c.Revision, c.Reference, c.TransporterId, transporterName, c.Type, c.Title, c.EffectiveStatus(today),
            c.EffectiveFrom, c.EffectiveTo, c.DaysUntilExpiry(today), c.RateCount, c.EstimatedAnnualSpend);

    public static ContractDto ToDto(this Contract c, string transporterName, string? ownerName, DateOnly today) =>
        new(c.ToSummary(transporterName, today), c.PaymentTermsDays, c.Terms, c.Fuel, c.OwnerUserId, ownerName, c.ApprovalRequestId,
            c.RevisionOfId, c.TerminationReason, c.RatesRevision, c.ActivatedAt, c.CreatedAt, c.Version, c.MissingForSubmission(today));

    public static RateCardDto ToDto(this RateCard r, IReadOnlyDictionary<Guid, VehicleTypeInfo> vehicleTypes) =>
        new(r.Id, r.Origin.ToDto(), r.Destination.ToDto(), $"{r.Origin} → {r.Destination}", r.BothWays, r.VehicleTypeId,
            r.VehicleTypeId is { } v && vehicleTypes.TryGetValue(v, out var type) ? type.Name : null,
            r.MinDistanceKm, r.MaxDistanceKm, r.Pricing);

    public static ZoneDto ToDto(this Zone z) => new(z.Id, z.Code, z.Name, z.Members, z.Version);

    public static DieselPriceDto ToDto(this DieselPrice d) => new(d.Id, d.Region, d.EffectiveFrom, d.PricePerLitre);

    public static ContractDocumentDto ToDto(this ContractDocument d) =>
        new(d.Id, d.ContractId, d.Kind, d.Title, d.FileName, d.ContentType, d.SizeBytes, d.CreatedAt);
}
