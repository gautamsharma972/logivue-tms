using System.Text.Json;

namespace Tms.Modules.Platform.Application.Audit;

public sealed record AuditLogDto(
    long Id,
    DateTimeOffset OccurredAt,
    Guid? UserId,
    string? UserName,
    string EntityType,
    string EntityId,
    string Action,
    JsonElement? Changes,
    string? TraceId,
    string? IpAddress);

public sealed record ListAuditLogsQuery(
    string? EntityType,
    string? EntityId,
    Guid? UserId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page = 1,
    int PageSize = 50);
