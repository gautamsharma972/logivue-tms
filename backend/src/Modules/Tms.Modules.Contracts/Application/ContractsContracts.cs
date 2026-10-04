using FluentValidation;
using Tms.Modules.Contracts.Domain;

namespace Tms.Modules.Contracts.Application;

public sealed record PlaceDto(PlaceKind Kind, string? State, string? City, string? ZoneCode);

/// <summary>One rate as entered by an operator. <see cref="Pricing"/> is polymorphic: it carries a <c>kind</c> discriminator.</summary>
public sealed record RateInputDto(
    PlaceDto Origin,
    PlaceDto Destination,
    bool BothWays,
    Guid? VehicleTypeId,
    decimal? MinDistanceKm,
    decimal? MaxDistanceKm,
    Pricing Pricing);

public sealed record RateCardDto(
    Guid Id,
    PlaceDto Origin,
    PlaceDto Destination,
    string Lane,
    bool BothWays,
    Guid? VehicleTypeId,
    string? VehicleTypeName,
    decimal? MinDistanceKm,
    decimal? MaxDistanceKm,
    Pricing Pricing);

public sealed record ContractSummaryDto(
    Guid Id,
    string Number,
    int Revision,
    string Reference,
    Guid TransporterId,
    string TransporterName,
    ContractType Type,
    string Title,
    ContractStatus Status,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    int? DaysUntilExpiry,
    int RateCount,
    decimal? EstimatedAnnualSpend);

public sealed record ContractDto(
    ContractSummaryDto Summary,
    int PaymentTermsDays,
    ContractTerms Terms,
    FuelClause? Fuel,
    Guid? OwnerUserId,
    string? OwnerName,
    Guid? ApprovalRequestId,
    Guid? RevisionOfId,
    string? TerminationReason,
    int RatesRevision,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset CreatedAt,
    long Version,
    IReadOnlyList<string> MissingForSubmission);

/// <param name="TransporterId">Fixed at creation.</param>
/// <param name="Type">Fixed at creation.</param>
/// <param name="Version">Required when updating (optimistic concurrency); ignored on create.</param>
public sealed record SaveContractRequest(
    Guid TransporterId,
    ContractType Type,
    string Title,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    int PaymentTermsDays,
    decimal? EstimatedAnnualSpend,
    Guid? OwnerUserId,
    ContractTerms Terms,
    FuelClause? Fuel,
    long? Version);

public sealed record SaveRatesRequest(IReadOnlyList<RateInputDto> Rates, long Version);

public sealed record TerminateRequest(string Reason);

public sealed record ReviseRequest(DateOnly EffectiveFrom, DateOnly EffectiveTo);

public sealed record ListContractsQuery(string? Search, ContractStatus? Status, ContractType? Type, Guid? TransporterId, int Page = 1, int PageSize = 25);

public sealed record ExpiringQuery(int WithinDays = 60, int Page = 1, int PageSize = 50);

public sealed record ZoneDto(Guid Id, string Code, string Name, IReadOnlyList<ZoneMember> Members, long Version);

public sealed record SaveZoneRequest(string Code, string Name, IReadOnlyList<ZoneMember> Members, long? Version);

public sealed record DieselPriceDto(Guid Id, string Region, DateOnly EffectiveFrom, decimal PricePerLitre);

public sealed record AddDieselPriceRequest(string Region, DateOnly EffectiveFrom, decimal PricePerLitre);

public sealed record ListDieselPricesQuery(string? Region);

/// <param name="ContractId">Price against one contract only. Combined with <see cref="Preview"/> it works on drafts too.</param>
/// <param name="Preview">Ignore approval status and dates, to test a rate card before it is submitted.</param>
public sealed record QuoteRequest(
    DateOnly? Date,
    Location Origin,
    Location Destination,
    Guid? VehicleTypeId,
    ContractType? Type,
    decimal? WeightKg,
    decimal? VolumeCbm,
    decimal? DistanceKm,
    int Drops = 1,
    Guid? ContractId = null,
    bool Preview = false);

public sealed record QuoteDto(
    Guid ContractId,
    string ContractReference,
    Guid TransporterId,
    string TransporterName,
    ContractType Type,
    string Lane,
    decimal? ChargeableWeightKg,
    IReadOnlyList<QuoteLine> Lines,
    IReadOnlyList<string> Notes,
    decimal Total);

public sealed record QuoteResultDto(DateOnly Date, IReadOnlyList<QuoteDto> Quotes, string? Message);

public sealed record ContractDocumentDto(Guid Id, Guid ContractId, ContractDocumentKind Kind, string Title, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);

internal sealed class SaveContractRequestValidator : AbstractValidator<SaveContractRequest>
{
    public SaveContractRequestValidator()
    {
        // Business rules (dates, terms, fuel clause) live in the domain; this only guards the request's shape.
        RuleFor(x => x.Title).NotNull();
        RuleFor(x => x.Terms).NotNull();
        RuleFor(x => x.TransporterId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
    }
}

internal sealed class SaveRatesRequestValidator : AbstractValidator<SaveRatesRequest>
{
    public SaveRatesRequestValidator()
    {
        RuleFor(x => x.Rates).NotNull();
        RuleForEach(x => x.Rates).ChildRules(rate =>
        {
            rate.RuleFor(r => r.Origin).NotNull();
            rate.RuleFor(r => r.Destination).NotNull();
            rate.RuleFor(r => r.Pricing).NotNull();
        });
    }
}

internal sealed class TerminateRequestValidator : AbstractValidator<TerminateRequest>
{
    public TerminateRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class SaveZoneRequestValidator : AbstractValidator<SaveZoneRequest>
{
    public SaveZoneRequestValidator()
    {
        RuleFor(x => x.Code).NotNull();
        RuleFor(x => x.Name).NotNull();
        RuleFor(x => x.Members).NotNull();
    }
}

internal sealed class AddDieselPriceRequestValidator : AbstractValidator<AddDieselPriceRequest>
{
    public AddDieselPriceRequestValidator() => RuleFor(x => x.Region).NotNull();
}

internal sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator()
    {
        RuleFor(x => x.Origin).NotNull();
        RuleFor(x => x.Destination).NotNull();
        RuleFor(x => x.Drops).InclusiveBetween(1, 50);
        RuleFor(x => x.WeightKg).GreaterThan(0).When(x => x.WeightKg is not null);
        RuleFor(x => x.VolumeCbm).GreaterThan(0).When(x => x.VolumeCbm is not null);
        RuleFor(x => x.DistanceKm).GreaterThan(0).When(x => x.DistanceKm is not null);
    }
}
