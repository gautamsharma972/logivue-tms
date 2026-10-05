using Tms.Modules.Deliveries.Domain;

namespace Tms.Modules.Deliveries.Application.Dashboard;

public sealed record DashboardQuery(DateOnly? From = null, DateOnly? To = null, Guid? TransporterId = null);

public sealed record DashboardSummaryDto(
    DateOnly From, DateOnly To, int DeliveriesToday, int Delivered, int PartiallyDelivered, int Failed, int Refused, int Closed,
    int PodPending, int PodInPreparation, int PodSubmitted, int PodUnderReview, int PodRejected, int PodResubmissionRequired, int PodAccepted,
    int ShortageCases, int DamageCases, int OpenExceptions, int OverdueExceptions, int OpenClaims, int UnreadNotifications);

/// <summary>A null rate means it could not be judged (nothing to measure), which is different from zero.</summary>
public sealed record ComplianceMetricsDto(
    int Delivered, int PodSubmitted, int PodPending, int PodRejected, int PodAccepted, decimal? SubmissionCompliance, decimal? AcceptanceRate, decimal? RejectionRate,
    decimal? AverageSubmissionHours, decimal? AverageReviewHours, decimal? AverageResubmissionHours, decimal? OnTimeRate);

public sealed record ComplianceRowDto(string Key, string Name, ComplianceMetricsDto Metrics);

public sealed record ComplianceDto(DateOnly From, DateOnly To, string GroupBy, ComplianceMetricsDto Overall, IReadOnlyList<ComplianceRowDto> Rows);

public sealed record ComplianceQuery(DateOnly? From = null, DateOnly? To = null, string? GroupBy = null, Guid? TransporterId = null, string? Customer = null, string? Lane = null, string? Vehicle = null, string? ServiceType = null);

public sealed record AgeingStageDto(AgeingStage Stage, string Label, int Count, int Overdue, int TargetHours, IReadOnlyList<int> Buckets);

public sealed record OverdueCountDto(string Name, int Overdue, int Total);

public sealed record AgeingDto(IReadOnlyList<string> BucketLabels, IReadOnlyList<AgeingStageDto> Stages, IReadOnlyList<int> BucketTotals, IReadOnlyList<OverdueCountDto> TopTransporters, IReadOnlyList<OverdueCountDto> TopCustomers, IReadOnlyList<OverdueCountDto> TopLocations);

public sealed record AgeingItemDto(
    AgeingStage Stage, Guid DeliveryId, string DeliveryNumber, Guid? PodId, string CustomerName, string? TransporterReference, string? Destination, double AgeHours, string Bucket, int BucketIndex, bool Overdue, int TargetHours);

public sealed record AgeingItemsQuery(AgeingStage? Stage = null, int? Bucket = null, bool? OverdueOnly = null, Guid? TransporterId = null, int Page = 1, int PageSize = 25);

public sealed record ExceptionsSummaryDto(int Open, int Overdue, int Escalated, IReadOnlyList<KeyCountDto> ByType, IReadOnlyList<KeyCountDto> ByStatus, IReadOnlyList<KeyCountDto> BySeverity);

public sealed record KeyCountDto(string Key, int Count);

public sealed record ReportQuery(DateOnly? From = null, DateOnly? To = null, Guid? TransporterId = null, string? Customer = null, string? GroupBy = null, string? Format = null, string? Lane = null, string? Vehicle = null, string? ServiceType = null);
