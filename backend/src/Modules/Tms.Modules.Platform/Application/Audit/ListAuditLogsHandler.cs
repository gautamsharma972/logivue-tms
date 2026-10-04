using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application.Audit;

internal sealed class ListAuditLogsHandler(PlatformDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<PagedResult<AuditLogDto>>> HandleAsync(ListAuditLogsQuery query, CancellationToken cancellationToken)
    {
        // AuditLog is deliberately not tenant-filtered globally (system events have no tenant), so scope it explicitly.
        var logs = db.AuditLogs.AsNoTracking().Where(a => a.TenantId == currentUser.TenantId);

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            logs = logs.Where(a => a.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            logs = logs.Where(a => a.EntityId == query.EntityId);
        }

        if (query.UserId is { } userId)
        {
            logs = logs.Where(a => a.UserId == userId);
        }

        if (query.From is { } from)
        {
            logs = logs.Where(a => a.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            logs = logs.Where(a => a.OccurredAt <= to);
        }

        var page = await logs.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .ToPagedAsync(query.Page, query.PageSize, cancellationToken);

        var userIds = page.Items.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var items = page.Items.Select(a => new AuditLogDto(
            a.Id,
            a.OccurredAt,
            a.UserId,
            a.UserId is { } id && names.TryGetValue(id, out var name) ? name : null,
            a.EntityType,
            a.EntityId,
            a.Action.ToString(),
            a.Changes is null ? null : JsonDocument.Parse(a.Changes).RootElement.Clone(),
            a.TraceId,
            a.IpAddress)).ToList();

        return new PagedResult<AuditLogDto>(items, page.Page, page.PageSize, page.TotalCount);
    }
}
