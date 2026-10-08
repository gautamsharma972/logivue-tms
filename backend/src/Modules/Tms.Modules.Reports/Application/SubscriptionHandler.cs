using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Reports.Application;

/// <summary>Scheduled reports. A subscription keeps the filters, the schedule and the owner's authority at the time it was saved; each run produces the owner's file and notifies the listed people, who open the report with their own access.</summary>
internal sealed class SubscriptionHandler(
    ReportsDbContext db, ReportDefinitionService definitions, PrincipalFactory principals, ReportSettingsStore settingsStore, ReportAuditor auditor, IUserDirectory users, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<SubscriptionDto>>> ListAsync(bool all, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        if (principal is null || principal.IsExternal || !principal.Has(ReportingPermissions.Read))
        {
            return ReportAccess.Forbidden;
        }

        var query = db.Subscriptions.AsNoTracking();
        if (!(all && principal.Has(ReportingPermissions.Manage)))
        {
            query = query.Where(s => s.UserId == principal.UserId);
        }

        var rows = await query.OrderBy(s => s.ReportCode).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<SubscriptionDto>>(await MapAsync(rows, cancellationToken));
    }

    public async Task<Result<SubscriptionDto>> CreateAsync(SaveSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var checkedRequest = await ValidateAsync(request, cancellationToken);
        if (checkedRequest.IsFailure)
        {
            return checkedRequest.Error;
        }

        var (principal, definition, settings, type, format, zone, recipients) = checkedRequest.Value;
        if (await db.Subscriptions.CountAsync(s => s.UserId == principal.UserId, cancellationToken) >= settings.MaxSubscriptionsPerUser)
        {
            return Error.Conflict("reports.subscription_limit", $"You can keep up to {settings.MaxSubscriptionsPerUser} scheduled reports.");
        }

        var now = clock.GetUtcNow();
        var sub = ReportSubscription.Create(principal.TenantId, definition.Code, Title(request, definition, type), principal.UserId, FiltersJson(request), type, JsonSerializer.Serialize(request.Schedule, JsonColumn.Options), format, zone,
            JsonSerializer.Serialize(recipients, JsonColumn.Options), PrincipalFactory.Freeze(principal), ScheduleCalculator.Next(type, request.Schedule, zone, now));
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync(cancellationToken);
        await auditor.WriteAsync(definition.Code, "SubscriptionCreated", sub.ParametersJson, format, outcome: sub.Name, cancellationToken: cancellationToken);
        return (await MapAsync([sub], cancellationToken))[0];
    }

    public async Task<Result<SubscriptionDto>> UpdateAsync(Guid id, SaveSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var sub = await OwnAsync(id, cancellationToken);
        if (sub is null)
        {
            return Error.NotFound("reports.subscription_not_found", "Scheduled report not found.");
        }

        var checkedRequest = await ValidateAsync(request, cancellationToken);
        if (checkedRequest.IsFailure)
        {
            return checkedRequest.Error;
        }

        if (request.Version is { } v)
        {
            db.Entry(sub).Property(x => x.Version).OriginalValue = v;
        }

        var (principal, definition, _, type, format, zone, recipients) = checkedRequest.Value;
        var now = clock.GetUtcNow();
        sub.Change(Title(request, definition, type), FiltersJson(request), type, JsonSerializer.Serialize(request.Schedule, JsonColumn.Options), format, zone, JsonSerializer.Serialize(recipients, JsonColumn.Options), request.Active,
            PrincipalFactory.Freeze(principal), ScheduleCalculator.Next(type, request.Schedule, zone, now));
        await db.SaveChangesAsync(cancellationToken);
        await auditor.WriteAsync(definition.Code, "SubscriptionChanged", sub.ParametersJson, format, outcome: sub.Name, cancellationToken: cancellationToken);
        return (await MapAsync([sub], cancellationToken))[0];
    }

    /// <summary>Produces the scheduled report now, as if its time had come (the schedule itself does not move). The owner's file is built in the background and the listed people are notified.</summary>
    public async Task<Result<ReportJobDto>> RunNowAsync(Guid id, CancellationToken cancellationToken)
    {
        var sub = await OwnAsync(id, cancellationToken);
        var principal = await principals.CurrentAsync(cancellationToken);
        if (sub is null || principal is null)
        {
            return Error.NotFound("reports.subscription_not_found", "Scheduled report not found.");
        }

        var definition = await definitions.FindAsync(sub.ReportCode, cancellationToken);
        if (definition is null || !ReportAccess.CanExport(definition, principal) || sub.UserId != principal.UserId)
        {
            return Error.Forbidden("reports.schedule_forbidden", "You are not allowed to run this scheduled report.");
        }

        var now = clock.GetUtcNow();
        var filters = (JsonSerializer.Deserialize<Dictionary<string, string>>(sub.ParametersJson, JsonColumn.Options) ?? []).ToDictionary(f => f.Key, f => JsonSerializer.SerializeToElement(f.Value));
        var job = ReportJob.Create(principal.TenantId, ReportJobService.Reference(), sub.ReportCode, sub.UserId, ReportJobService.Store(new ExportRequest(filters, null, null, sub.Format, true)), sub.Format, PrincipalFactory.Freeze(principal), now, sub.Id);
        db.Jobs.Add(job);
        sub.Ran(job.Id, now, sub.NextRunAt);
        await db.SaveChangesAsync(cancellationToken);
        await auditor.WriteAsync(sub.ReportCode, "ScheduledRunQueued", sub.ParametersJson, sub.Format, outcome: job.JobReference, cancellationToken: cancellationToken);
        return new ReportJobDto(job.Id, job.JobReference, job.ReportCode, definition.Name, job.Format, job.Status.ToString(), job.RequestedAt, null, null, 0, null, null, null, null, null, 0, false, true);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var sub = await OwnAsync(id, cancellationToken);
        if (sub is null)
        {
            return Error.NotFound("reports.subscription_not_found", "Scheduled report not found.");
        }

        db.Subscriptions.Remove(sub);
        await db.SaveChangesAsync(cancellationToken);
        await auditor.WriteAsync(sub.ReportCode, "SubscriptionDeleted", sub.ParametersJson, sub.Format, outcome: sub.Name, cancellationToken: cancellationToken);
        return Result.Success();
    }

    private async Task<ReportSubscription?> OwnAsync(Guid id, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        if (principal is null)
        {
            return null;
        }

        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return sub is not null && (sub.UserId == principal.UserId || principal.Has(ReportingPermissions.Manage)) ? sub : null;
    }

    private static string FiltersJson(SaveSubscriptionRequest r) => JsonSerializer.Serialize(ReportFilters.FromJson(r.Filters).Values, JsonColumn.Options);

    private static string Title(SaveSubscriptionRequest r, ReportDefinition d, ScheduleType type) => string.IsNullOrWhiteSpace(r.Name) ? $"{d.Name} ({type.ToString().ToLowerInvariant()})" : r.Name.Trim();

    private async Task<Result<(ReportPrincipal Principal, ReportDefinition Definition, ReportSettings Settings, ScheduleType Type, string Format, string Zone, List<string> Recipients)>> ValidateAsync(SaveSubscriptionRequest r, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        var definition = await definitions.FindAsync(r.ReportCode, cancellationToken);
        if (principal is null || definition is null)
        {
            return ReportAccess.NotFound;
        }

        if (!ReportAccess.CanSchedule(definition, principal))
        {
            return Error.Forbidden("reports.schedule_forbidden", "You are not allowed to schedule this report.");
        }

        if (!Enum.TryParse<ScheduleType>(r.ScheduleType, ignoreCase: true, out var type))
        {
            return Error.Validation("reports.schedule_type", "Choose Daily, Weekly, Monthly or Custom.");
        }

        var zone = string.IsNullOrWhiteSpace(r.TimeZone) ? "Asia/Kolkata" : r.TimeZone.Trim();
        if (ScheduleCalculator.Validate(type, r.Schedule, zone) is { } problem)
        {
            return Error.Validation("reports.schedule_invalid", problem);
        }

        var format = (r.Format ?? "xlsx").ToLowerInvariant();
        if (!CatalogueHandler.Formats(definition).Contains(format) || !ReportExporter.IsKnown(format))
        {
            return Error.Validation("reports.format_invalid", $"This report can be sent as {string.Join(", ", CatalogueHandler.Formats(definition))}.");
        }

        var recipients = (r.Recipients ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (recipients.Count > 20 || recipients.Any(x => !MailAddress.TryCreate(x, out _)))
        {
            return Error.Validation("reports.recipients_invalid", "Give up to 20 valid email addresses.");
        }

        return (principal, definition, await settingsStore.GetAsync(cancellationToken), type, format, zone, recipients);
    }

    private async Task<IReadOnlyList<SubscriptionDto>> MapAsync(IReadOnlyList<ReportSubscription> rows, CancellationToken cancellationToken)
    {
        var names = (await definitions.AllAsync(cancellationToken)).ToDictionary(d => d.Code, d => d.Name, StringComparer.OrdinalIgnoreCase);
        var people = await users.GetDisplayNamesAsync(rows.Select(s => s.UserId).Distinct(), cancellationToken);
        _ = user;
        return rows.Select(s =>
        {
            var schedule = JsonSerializer.Deserialize<ScheduleDefinition>(s.ScheduleDefinitionJson, JsonColumn.Options) ?? new ScheduleDefinition();
            return new SubscriptionDto(s.Id, s.ReportCode, names.GetValueOrDefault(s.ReportCode, s.ReportCode), s.Name, JsonSerializer.Deserialize<Dictionary<string, string>>(s.ParametersJson, JsonColumn.Options) ?? [],
                s.ScheduleType.ToString(), schedule, s.Format, s.TimeZone, JsonSerializer.Deserialize<List<string>>(s.RecipientsJson, JsonColumn.Options) ?? [], s.Active, s.NextRunAt, s.LastRunAt, s.ConsecutiveFailures, s.UserId,
                people.GetValueOrDefault(s.UserId), s.Version, Describe(s.ScheduleType, schedule, s.TimeZone));
        }).ToList();
    }

    public static string Describe(ScheduleType type, ScheduleDefinition d, string zone) => type switch
    {
        ScheduleType.Daily => $"Every day at {d.Time} ({zone})",
        ScheduleType.Weekly => $"Every {d.DayOfWeek} at {d.Time} ({zone})",
        ScheduleType.Monthly => $"{(d.DayOfMonth == 0 ? "On the last day" : $"On day {d.DayOfMonth}")} of every month at {d.Time} ({zone})",
        _ => $"Every {d.EveryMinutes} minutes",
    };
}
