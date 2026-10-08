using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Reports.Application;

public sealed class ReportsOptions
{
    public const string SectionName = "Reports";

    public bool WorkerEnabled { get; init; } = true;

    public int WorkerPollSeconds { get; init; } = 3;

    public bool SchedulerEnabled { get; init; } = true;

    public int SchedulerPollSeconds { get; init; } = 30;

    public bool AggregationEnabled { get; init; } = true;

    public int AggregationIntervalMinutes { get; init; } = 60;

    public int SummaryDays { get; init; } = 14;

    public int MaxJobAttempts { get; init; } = 2;

    /// <summary>Base address used in notification emails (the web app).</summary>
    public string PublicBaseUrl { get; init; } = "http://localhost:5173";
}

/// <summary>Builds and keeps the files behind an export, for the immediate path and the background worker alike.</summary>
internal sealed class ReportJobService(
    ReportsDbContext db, ReportExecutor executor, ReportSettingsStore settingsStore, IFileStore files, ReportAuditor auditor, TimeProvider clock, ILogger<ReportJobService> logger)
{
    public static string Key(Guid tenant, Guid job, string format) => $"reports/{tenant:N}/{job:N}.{format}";

    public static string Reference() => $"RJ-{Guid.CreateVersion7().ToString("N")[^8..].ToUpperInvariant()}";

    /// <summary>Runs the job's report as the person who asked, writes the file and marks the job finished.</summary>
    public async Task<bool> BuildAsync(ReportJob job, CancellationToken cancellationToken)
    {
        var principal = PrincipalFactory.Thaw(job.PrincipalJson);
        var request = JsonSerializer.Deserialize<StoredRequest>(job.ParametersJson, JsonColumn.Options)!;
        var settings = await settingsStore.GetAsync(cancellationToken);
        var result = await executor.ExecuteForExportAsync(job.ReportCode, new ReportRequest(job.ReportCode, request.Filters, request.GroupBy, request.Sort), principal, settings.ExportMaxRows, cancellationToken);
        if (result.IsFailure)
        {
            job.Fail(result.Error.Description, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        job.Advance(70);
        await db.SaveChangesAsync(cancellationToken);
        var file = ReportExporter.Build(result.Value, job.Format, clock.GetUtcNow());
        var key = Key(job.TenantId, job.Id, job.Format);
        await using (var stream = new MemoryStream(file.Content))
        {
            await files.SaveAsync(key, stream, cancellationToken);
        }

        job.Complete(key, file.FileName, file.ContentType, file.Content.LongLength, result.Value.TotalRows, clock.GetUtcNow(), TimeSpan.FromHours(settings.JobKeepHours));
        await db.SaveChangesAsync(cancellationToken);
        await auditor.WriteAsync(job.ReportCode, "Exported", job.ParametersJson, job.Format, rows: result.Value.TotalRows, outcome: job.JobReference, by: job.RequestedBy, cancellationToken: cancellationToken);
        logger.LogInformation("Report job {Job} built {File} ({Bytes} bytes, {Rows} rows)", job.JobReference, file.FileName, file.Content.LongLength, result.Value.TotalRows);
        return true;
    }

    internal sealed record StoredRequest(Dictionary<string, JsonElement>? Filters, IReadOnlyList<string>? GroupBy, IReadOnlyList<SortRequest>? Sort);

    public static string Store(ExportRequest r) => JsonSerializer.Serialize(new StoredRequest(r.Filters, r.GroupBy, r.Sort), JsonColumn.Options);
}

/// <summary>Exports: small ones are built while the person waits, large ones become a background job they are told about. Either way the file is kept for a while and only they can download it.</summary>
internal sealed class ExportHandler(
    ReportsDbContext db, ReportDefinitionService definitions, PrincipalFactory principals, ReportExecutor executor, ReportSettingsStore settingsStore, ReportJobService jobs, ReportAuditor auditor,
    IFileStore files, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<ExportOutcome>> ExportAsync(string code, ExportRequest request, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        var definition = await definitions.FindAsync(code, cancellationToken);
        if (principal is null || definition is null)
        {
            return ReportAccess.NotFound;
        }

        if (!ReportAccess.CanExport(definition, principal))
        {
            await auditor.WriteAsync(definition.Code, "ExportDenied", null, request.Format, cancellationToken: cancellationToken);
            return Error.Forbidden("reports.export_forbidden", "You are not allowed to export this report.");
        }

        var format = request.Format.ToLowerInvariant();
        if (!ReportExporter.IsKnown(format) || !CatalogueHandler.Formats(definition).Contains(format))
        {
            return Error.Validation("reports.format_invalid", $"This report can be exported as {string.Join(", ", CatalogueHandler.Formats(definition))}.");
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var job = ReportJob.Create(principal.TenantId, ReportJobService.Reference(), definition.Code, principal.UserId, ReportJobService.Store(request), format, PrincipalFactory.Freeze(principal), now);
        db.Jobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        if (!request.Background)
        {
            var probe = await executor.ExecuteAsync(definition.Code, new ReportRequest(definition.Code, request.Filters, request.GroupBy, request.Sort, 1, 1, Refresh: true), principal, audit: false, cancellationToken, 1);
            if (probe.IsFailure)
            {
                db.Jobs.Remove(job);
                await db.SaveChangesAsync(cancellationToken);
                return probe.Error;
            }

            if (probe.Value.TotalRows <= settings.SyncExportRows)
            {
                job.Start(now);
                if (!await jobs.BuildAsync(job, cancellationToken))
                {
                    return Error.Failure("REPORT_EXECUTION_ERROR", "Unable to generate the requested report.");
                }

                return new ExportOutcome(Map(job, definition.Name, principal), true);
            }
        }

        await auditor.WriteAsync(definition.Code, "ExportRequested", job.ParametersJson, format, outcome: job.JobReference, cancellationToken: cancellationToken);
        return new ExportOutcome(Map(job, definition.Name, principal), false);
    }

    public async Task<Result<IReadOnlyList<ReportJobDto>>> ListAsync(CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        if (principal is null)
        {
            return ReportAccess.Forbidden;
        }

        var rows = await db.Jobs.AsNoTracking().Where(j => j.RequestedBy == principal.UserId).OrderByDescending(j => j.RequestedAt).Take(100).ToListAsync(cancellationToken);
        var names = (await definitions.AllAsync(cancellationToken)).ToDictionary(d => d.Code, d => d.Name, StringComparer.OrdinalIgnoreCase);
        return Result.Success<IReadOnlyList<ReportJobDto>>(rows.Select(j => Map(j, names.GetValueOrDefault(j.ReportCode, j.ReportCode), principal)).ToList());
    }

    public async Task<Result<ReportJobDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var (job, principal) = await FindAsync(id, tracking: false, cancellationToken);
        if (job is null || principal is null)
        {
            return ReportAccess.NotFound;
        }

        return Map(job, (await definitions.FindAsync(job.ReportCode, cancellationToken))?.Name ?? job.ReportCode, principal);
    }

    public async Task<Result<ReportJobDto>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var (job, principal) = await FindAsync(id, tracking: true, cancellationToken);
        if (job is null || principal is null)
        {
            return ReportAccess.NotFound;
        }

        if (!job.Cancel(clock.GetUtcNow()))
        {
            return Error.Conflict("reports.job_finished", "This export has already finished.");
        }

        await db.SaveChangesAsync(cancellationToken);
        await auditor.WriteAsync(job.ReportCode, "ExportCancelled", null, job.Format, outcome: job.JobReference, cancellationToken: cancellationToken);
        return Map(job, job.ReportCode, principal);
    }

    public async Task<Result<ReportJobDto>> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var (job, principal) = await FindAsync(id, tracking: true, cancellationToken);
        if (job is null || principal is null)
        {
            return ReportAccess.NotFound;
        }

        if (!job.Retry())
        {
            return Error.Conflict("reports.job_not_retryable", "Only a failed or cancelled export can be tried again.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return Map(job, job.ReportCode, principal);
    }

    /// <summary>The file, for its owner only, while it has not expired. Every download is recorded.</summary>
    public async Task<Result<(Stream Content, string FileName, string ContentType)>> DownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var (job, principal) = await FindAsync(id, tracking: true, cancellationToken);
        if (job is null || principal is null)
        {
            return ReportAccess.NotFound;
        }

        if (job.Status != ReportJobStatus.Completed || job.OutputFileReference is null)
        {
            return Error.Conflict("reports.job_not_ready", job.Status == ReportJobStatus.Expired ? "This export has expired. Export it again." : "This export is not ready.");
        }

        if (job.ExpiresAt is { } e && e <= clock.GetUtcNow())
        {
            return Error.Conflict("reports.job_expired", "This export has expired. Export it again.");
        }

        var stream = await files.OpenReadAsync(job.OutputFileReference, cancellationToken);
        if (stream is null)
        {
            return Error.NotFound("reports.file_missing", "The file is no longer available. Export it again.");
        }

        job.RecordDownload(principal.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await auditor.WriteAsync(job.ReportCode, "Downloaded", null, job.Format, outcome: job.JobReference, cancellationToken: cancellationToken);
        return (stream, job.FileName ?? "report", job.ContentType ?? "application/octet-stream");
    }

    private async Task<(ReportJob? Job, ReportPrincipal? Principal)> FindAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        if (principal is null)
        {
            return (null, null);
        }

        var query = tracking ? db.Jobs.AsQueryable() : db.Jobs.AsNoTracking();
        var job = await query.FirstOrDefaultAsync(j => j.Id == id && j.RequestedBy == principal.UserId, cancellationToken);
        return (job, principal);
    }

    private ReportJobDto Map(ReportJob j, string name, ReportPrincipal principal) =>
        new(j.Id, j.JobReference, j.ReportCode, name, j.Format, j.Status.ToString(), j.RequestedAt, j.StartedAt, j.CompletedAt, j.Progress, j.RowCount, j.SizeBytes, j.FileName, j.ErrorMessage, j.ExpiresAt, j.DownloadCount,
            j.Status == ReportJobStatus.Completed && principal.UserId == j.RequestedBy && (j.ExpiresAt is null || j.ExpiresAt > clock.GetUtcNow()), j.SubscriptionId is not null);

    internal Guid? CurrentUser => user.UserId;
}
