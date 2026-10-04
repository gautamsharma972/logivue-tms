using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Alerts;

public sealed record AlertDto(
    long Id,
    string AlertType,
    Severity Severity,
    long TransporterId,
    string? LoadReference,
    string EntityType,
    string EntityId,
    string Message,
    DateTime CreatedAt,
    DateTime? DueAt,
    AlertStatus Status,
    DateTime? AcknowledgedAt,
    DateTime? ResolvedAt);

public sealed record AlertSearch(
    long? TransporterId = null,
    AlertStatus? Status = null,
    Severity? Severity = null,
    string? AlertType = null,
    int Page = 1,
    int PageSize = 25);
