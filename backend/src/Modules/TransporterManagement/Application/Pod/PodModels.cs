using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Pod;

/// <summary>Metadata for a POD submission. The file content is passed separately as a stream.</summary>
public sealed record PodSubmissionRequest(string FileName, string ContentType, long SizeBytes, DateOnly PodDate, string ReceivedBy);

/// <param name="SubmissionSla">OnTime or Late once submitted; Pending while inside the SLA; Overdue after it.</param>
public sealed record PodDto(
    long Id,
    string LoadReference,
    long TransporterId,
    long LoadExecutionId,
    DateTime DeliveredAt,
    DateTime DueAt,
    DateTime? SubmittedAt,
    bool? SubmittedWithinSla,
    string SubmissionSla,
    DateOnly? PodDate,
    string? ReceivedBy,
    string? OriginalFileName,
    PodStatus Status,
    int RejectionCount,
    string? RejectionReason,
    string? ReviewedBy,
    DateTime? ReviewedAt);

public sealed record PodFileDto(Stream Content, string FileName, string ContentType);
