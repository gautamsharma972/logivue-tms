using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Contracts.Application;

internal static class Mapping
{
    /// <summary>Numbers shown beside a contract in a list that cost a query each, so they are worked out together for the whole page.</summary>
    public sealed record SummaryExtras(int? ActiveRates, int? ExpiringRates, int? CommittedVehicles, string? RenewalState);

    public static ContractSummaryDto ToSummary(this Contract c, string transporterName, DateOnly today, SummaryExtras? x = null) =>
        new(c.Id, c.Number, c.Revision, c.Reference, c.TransporterId, transporterName, c.Type, c.Title, c.EffectiveStatus(today),
            c.EffectiveFrom, c.EffectiveTo, c.DaysUntilExpiry(today), c.RateCount, c.EstimatedAnnualSpend, c.EffectiveServices, c.Currency, c.RevisionKind,
            x?.RenewalState, x?.ActiveRates, x?.ExpiringRates, x?.CommittedVehicles);

    public static ContractDto ToDto(this Contract c, string transporterName, string? ownerName, DateOnly today, RateValidationResult? validation = null) =>
        new(c.ToSummary(transporterName, today), c.PaymentTermsDays, c.Terms, c.Fuel, c.OwnerUserId, ownerName, c.ApprovalRequestId,
            c.RevisionOfId, c.TerminationReason, c.RatesRevision, c.ActivatedAt, c.CreatedAt, c.Version, c.MissingForSubmission(today),
            c.ToExtras(), c.Suspensions, c.CalculationVersion, c.DphRules.Count, c.Accessorials.Count, c.Capacities.Count, c.Slas.Count, validation?.Errors ?? 0, validation?.Warnings ?? 0);

    public static RateCardDto ToDto(this RateCard r, IReadOnlyDictionary<Guid, VehicleTypeInfo> vehicleTypes, Contract? contract = null, DateOnly? today = null) =>
        new(r.Id, r.Origin.ToDto(), r.Destination.ToDto(), $"{r.Origin} → {r.Destination}", r.BothWays, r.VehicleTypeId,
            r.VehicleTypeId is { } v && vehicleTypes.TryGetValue(v, out var type) ? type.Name : null,
            r.MinDistanceKm, r.MaxDistanceKm, r.Pricing, r.Extras, r.Version, contract is not null && today is { } d && contract.IsInForce(d) && (r.ValidTo is null || r.ValidTo >= d) && (r.ValidFrom is null || r.ValidFrom <= d));

    public static ZoneDto ToDto(this Zone z) => new(z.Id, z.Code, z.Name, z.Members, z.Version);

    public static DieselPriceDto ToDto(this DieselPrice d) => new(d.Id, d.Region, d.EffectiveFrom, d.PricePerLitre, d.Source);

    public static ContractDocumentDto ToDto(this ContractDocument d) =>
        new(d.Id, d.ContractId, d.Kind, d.Title, d.FileName, d.ContentType, d.SizeBytes, d.CreatedAt, d.Number, d.DocumentVersion, d.IssueDate, d.EffectiveDate, d.ExpiryDate, d.Status, d.CreatedBy, d.VerifiedBy, d.VerifiedAt);
}
