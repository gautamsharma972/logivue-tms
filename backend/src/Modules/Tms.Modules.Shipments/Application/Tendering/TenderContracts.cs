using FluentValidation;
using Tms.Modules.Shipments.Domain;

namespace Tms.Modules.Shipments.Application.Tendering;

/// <param name="ContractIds">The contracts (hence transporters) to invite, in the order to try them for a sequential tender.</param>
/// <param name="ResponseMinutes">How long each transporter has to answer; defaults to four hours.</param>
public sealed record StartTenderRequest(TenderMode Mode, IReadOnlyList<Guid> ContractIds, int? ResponseMinutes, string? Notes);

/// <param name="TransporterId">Only for staff answering on a transporter's behalf; a vendor always answers for its own company.</param>
public sealed record BidRequest(Guid VehicleId, Guid DriverId, decimal? CounterRate, string? Comments, Guid? TransporterId = null);

public sealed record CounterOfferRequest(decimal Rate, string? Comments, Guid? TransporterId = null);

public sealed record DeclineTenderRequest(string Reason, Guid? TransporterId = null);

public sealed record CounterDecisionRequest(Guid InviteeId, bool Agree, string? Comments);

public sealed record AwardTenderRequest(Guid InviteeId);

/// <summary>One transporter in a tender. <see cref="QuotedTotal"/> and <see cref="ContractReference"/> are null for vendors.</summary>
public sealed record TenderInviteeDto(
    Guid Id,
    Guid TransporterId,
    string TransporterName,
    int Sequence,
    InviteeStatus Status,
    DateTimeOffset? SentAt,
    DateTimeOffset? Deadline,
    DateTimeOffset? RespondedAt,
    string? Reason,
    string? ContractReference,
    decimal? QuotedTotal,
    decimal? CounterRate,
    string? CounterComment,
    CounterStatus CounterStatus,
    decimal? AgreedRate,
    Guid? BidVehicleId,
    string? BidVehicleRegistration,
    Guid? BidDriverId,
    string? BidDriverName);

public sealed record TenderEventDto(DateTimeOffset At, string Type, Guid? InviteeId, string? TransporterName, string? Comments);

public sealed record TenderDto(
    Guid Id,
    string Number,
    Guid ShipmentId,
    string ShipmentNumber,
    TenderMode Mode,
    TenderStatus Status,
    int ResponseMinutes,
    string? Notes,
    Guid? AwardedTransporterId,
    DateTimeOffset? ClosedAt,
    string? CloseReason,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TenderInviteeDto> Invitees,
    IReadOnlyList<TenderEventDto> Events);

internal sealed class StartTenderRequestValidator : AbstractValidator<StartTenderRequest>
{
    public StartTenderRequestValidator()
    {
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.ContractIds).NotNull().Must(ids => ids is { Count: >= 2 and <= TenderRound.MaxInvitees }).WithMessage($"Choose between 2 and {TenderRound.MaxInvitees} contracts.");
        RuleFor(x => x.ResponseMinutes).InclusiveBetween(TenderRound.MinResponseMinutes, TenderRound.MaxResponseMinutes).When(x => x.ResponseMinutes.HasValue);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

internal sealed class BidRequestValidator : AbstractValidator<BidRequest>
{
    public BidRequestValidator()
    {
        RuleFor(x => x.VehicleId).NotEmpty();
        RuleFor(x => x.DriverId).NotEmpty();
        RuleFor(x => x.CounterRate).GreaterThan(0).When(x => x.CounterRate.HasValue);
        RuleFor(x => x.Comments).MaximumLength(500);
    }
}

internal sealed class CounterOfferRequestValidator : AbstractValidator<CounterOfferRequest>
{
    public CounterOfferRequestValidator()
    {
        RuleFor(x => x.Rate).GreaterThan(0).LessThan(100_000_000);
        RuleFor(x => x.Comments).MaximumLength(500);
    }
}

internal sealed class DeclineTenderRequestValidator : AbstractValidator<DeclineTenderRequest>
{
    public DeclineTenderRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class CounterDecisionRequestValidator : AbstractValidator<CounterDecisionRequest>
{
    public CounterDecisionRequestValidator()
    {
        RuleFor(x => x.InviteeId).NotEmpty();
        RuleFor(x => x.Comments).MaximumLength(500);
    }
}

internal sealed class AwardTenderRequestValidator : AbstractValidator<AwardTenderRequest>
{
    public AwardTenderRequestValidator() => RuleFor(x => x.InviteeId).NotEmpty();
}
