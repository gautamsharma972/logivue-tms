using FluentValidation;
using Microsoft.AspNetCore.Http;
using Tms.Modules.Shipments.Domain;

namespace Tms.Modules.Shipments.Application.Delivery;

public sealed record RecordDeliveryRequest(DateTimeOffset? DeliveredAt, string ReceiverName, int? DeliveredPackages, int? DamagedPackages, string? Remarks);

/// <summary>Multipart form of a proof-of-delivery upload.</summary>
public sealed class UploadPodForm
{
    public IFormFile? File { get; init; }
}

public sealed record PodDocumentDto(Guid Id, Guid ShipmentId, Guid OrderId, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);

public enum PodStage
{
    /// <summary>On the road: not yet delivered.</summary>
    DeliveryPending = 1,
    AwaitingProof = 2,
    ProofUploaded = 3,
    ProofRejected = 4,
    ProofVerified = 5,
}

public sealed record ListPodQuery(PodStage? Stage = null, string? Search = null, bool? OverdueOnly = null, Guid? TransporterId = null, int Page = 1, int PageSize = 25);

public sealed record PodLineDto(
    Guid ShipmentId,
    string ShipmentNumber,
    Guid OrderId,
    string OrderNumber,
    string? LrNumber,
    string Consignee,
    string ConsigneeCity,
    Guid? TransporterId,
    string? TransporterName,
    PodStage Stage,
    DateTimeOffset? DeliveredAt,
    string? ReceiverName,
    int? PackagesShipped,
    int? DeliveredPackages,
    int? DamagedPackages,
    int? ShortagePackages,
    bool HasException,
    int? AgeDays,
    bool Overdue,
    string? RejectionReason,
    int Documents);

public sealed record AgeingQuery(int OverdueDays = 7);

public sealed record AgeingBucketDto(string Label, int Count);

public sealed record TransporterAgeingDto(Guid? TransporterId, string Name, int Outstanding, int Overdue, int OldestDays);

public sealed record AgeingDto(int OverdueDays, int Outstanding, int Overdue, int WithExceptions, IReadOnlyList<AgeingBucketDto> Buckets, IReadOnlyList<TransporterAgeingDto> Transporters);

internal sealed class RecordDeliveryRequestValidator : AbstractValidator<RecordDeliveryRequest>
{
    public RecordDeliveryRequestValidator() => RuleFor(x => x.ReceiverName).NotNull(); // the rules (length, quantities, remarks) live in the domain
}
