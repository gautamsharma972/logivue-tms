using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Reports.Application;

/// <summary>Tells people a report is ready. Notices carry a link, never the data: a recipient opens the report with their own access.</summary>
internal sealed class ReportNotifier(IEmailSender email, IUserDirectory users, IOptions<ReportsOptions> options, ILogger<ReportNotifier> logger)
{
    private string Base => options.Value.PublicBaseUrl.TrimEnd('/');

    public async Task OwnerReadyAsync(ReportJob job, string reportName, CancellationToken cancellationToken)
    {
        if ((await users.GetContactsAsync([job.RequestedBy], cancellationToken)).GetValueOrDefault(job.RequestedBy) is not { } contact)
        {
            return;
        }

        await SendAsync(contact.Email, $"Your report is ready: {reportName}",
            $"Hello {contact.FullName},\n\nYour {job.Format.ToUpperInvariant()} export of \"{reportName}\" ({job.JobReference}) is ready. It is kept until {job.ExpiresAt:dd MMM yyyy HH:mm} UTC.\n\nOpen {Base}/reports/exports to download it (you must be signed in).", cancellationToken);
    }

    public async Task OwnerFailedAsync(ReportJob job, string reportName, CancellationToken cancellationToken)
    {
        if ((await users.GetContactsAsync([job.RequestedBy], cancellationToken)).GetValueOrDefault(job.RequestedBy) is not { } contact)
        {
            return;
        }

        await SendAsync(contact.Email, $"Your report could not be produced: {reportName}", $"Hello {contact.FullName},\n\nThe export \"{reportName}\" ({job.JobReference}) failed. You can try it again at {Base}/reports/exports.", cancellationToken);
    }

    public async Task SubscribersAsync(ReportSubscription sub, string reportName, string ownerName, CancellationToken cancellationToken)
    {
        var filters = JsonSerializer.Deserialize<Dictionary<string, string>>(sub.ParametersJson, JsonColumn.Options) ?? [];
        var query = filters.Count == 0 ? string.Empty : "?" + string.Join('&', filters.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
        var link = $"{Base}/reports/{sub.ReportCode}{query}";
        foreach (var to in JsonSerializer.Deserialize<List<string>>(sub.RecipientsJson, JsonColumn.Options) ?? [])
        {
            await SendAsync(to, $"Scheduled report: {reportName}", $"{ownerName} scheduled \"{reportName}\" ({sub.Name}).\n\nOpen it here, with your own access: {link}", cancellationToken);
        }
    }

    private async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(new EmailMessage(to, subject, body), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Report notice to {To} could not be sent", to);
        }
    }
}

/// <summary>Builds queued export files in the background, as the person who asked, and clears expired files.</summary>
internal sealed class ReportJobWorker(IServiceScopeFactory scopes, IOptions<ReportsOptions> options, TimeProvider clock, ILogger<ReportJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.WorkerPollSeconds)));
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Report job worker pass failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        List<(Guid Id, Guid Tenant, Guid User)> queued;
        List<(Guid Id, Guid Tenant, Guid User)> expired;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportsDbContext>();
            var now = clock.GetUtcNow();
            queued = (await db.Jobs.IgnoreQueryFilters().AsNoTracking().Where(j => j.Status == ReportJobStatus.Queued).OrderBy(j => j.RequestedAt).Take(5).Select(j => new { j.Id, j.TenantId, j.RequestedBy }).ToListAsync(cancellationToken))
                .Select(j => (j.Id, j.TenantId, j.RequestedBy)).ToList();
            expired = (await db.Jobs.IgnoreQueryFilters().AsNoTracking().Where(j => j.Status == ReportJobStatus.Completed && j.ExpiresAt != null && j.ExpiresAt <= now).Take(20).Select(j => new { j.Id, j.TenantId, j.RequestedBy }).ToListAsync(cancellationToken))
                .Select(j => (j.Id, j.TenantId, j.RequestedBy)).ToList();
        }

        foreach (var (id, tenant, user) in queued)
        {
            await ProcessAsync(id, tenant, user, cancellationToken);
        }

        foreach (var (id, tenant, user) in expired)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IAmbientUserContext>().RunAs(tenant, user, $"reports:expire:{id:N}");
            var db = scope.ServiceProvider.GetRequiredService<ReportsDbContext>();
            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
            if (job?.OutputFileReference is { } key)
            {
                await scope.ServiceProvider.GetRequiredService<IFileStore>().DeleteAsync(key, cancellationToken);
                job.Expire();
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    internal async Task ProcessAsync(Guid id, Guid tenant, Guid user, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<IAmbientUserContext>().RunAs(tenant, user, $"reports:job:{id:N}");
        var db = sp.GetRequiredService<ReportsDbContext>();
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null || job.Status != ReportJobStatus.Queued)
        {
            return;
        }

        job.Start(clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return; // another instance took it
        }

        var jobs = sp.GetRequiredService<ReportJobService>();
        var notifier = sp.GetRequiredService<ReportNotifier>();
        var name = (await sp.GetRequiredService<ReportDefinitionService>().FindAsync(job.ReportCode, cancellationToken))?.Name ?? job.ReportCode;
        try
        {
            var ok = await jobs.BuildAsync(job, cancellationToken);
            await AfterAsync(sp, db, job, name, ok, notifier, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Report job {Job} failed", job.JobReference);
            db.ChangeTracker.Clear();
            var fresh = await db.Jobs.FirstAsync(j => j.Id == id, cancellationToken);
            fresh.Fail("The report could not be produced.", clock.GetUtcNow());
            if (fresh.Attempts < options.Value.MaxJobAttempts)
            {
                fresh.Retry();
            }

            await db.SaveChangesAsync(cancellationToken);
            if (fresh.Status == ReportJobStatus.Failed)
            {
                await notifier.OwnerFailedAsync(fresh, name, cancellationToken);
            }
        }
    }

    private static async Task AfterAsync(IServiceProvider sp, ReportsDbContext db, ReportJob job, string name, bool ok, ReportNotifier notifier, CancellationToken cancellationToken)
    {
        if (!ok)
        {
            await notifier.OwnerFailedAsync(job, name, cancellationToken);
            return;
        }

        if (job.SubscriptionId is { } subId && await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subId, cancellationToken) is { } sub)
        {
            var names = await sp.GetRequiredService<IUserDirectory>().GetDisplayNamesAsync([sub.UserId], cancellationToken);
            await notifier.SubscribersAsync(sub, name, names.GetValueOrDefault(sub.UserId) ?? "A colleague", cancellationToken);
        }

        await notifier.OwnerReadyAsync(job, name, cancellationToken);
    }
}

/// <summary>Turns due subscriptions into export jobs. The owner's access is read afresh each time: someone who has left, or lost the permission, stops receiving the report.</summary>
internal sealed class ReportScheduler(IServiceScopeFactory scopes, IOptions<ReportsOptions> options, TimeProvider clock, ILogger<ReportScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SchedulerEnabled)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.SchedulerPollSeconds)));
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Report scheduler pass failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        List<(Guid Id, Guid Tenant, Guid User)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportsDbContext>();
            var now = clock.GetUtcNow();
            due = (await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().Where(s => s.Active && s.NextRunAt != null && s.NextRunAt <= now).OrderBy(s => s.NextRunAt).Take(25).Select(s => new { s.Id, s.TenantId, s.UserId }).ToListAsync(cancellationToken))
                .Select(s => (s.Id, s.TenantId, s.UserId)).ToList();
        }

        var made = 0;
        foreach (var (id, tenant, user) in due)
        {
            if (await RunOneAsync(id, tenant, user, cancellationToken))
            {
                made++;
            }
        }

        return made;
    }

    private async Task<bool> RunOneAsync(Guid id, Guid tenant, Guid user, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<IAmbientUserContext>().RunAs(tenant, user, $"reports:schedule:{id:N}");
        var db = sp.GetRequiredService<ReportsDbContext>();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sub is null || !sub.Active)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        var settings = await sp.GetRequiredService<ReportSettingsStore>().GetAsync(cancellationToken);
        var schedule = JsonSerializer.Deserialize<ScheduleDefinition>(sub.ScheduleDefinitionJson, JsonColumn.Options) ?? new ScheduleDefinition();
        var next = ScheduleCalculator.Next(sub.ScheduleType, schedule, sub.TimeZone, now);
        try
        {
            var frozen = PrincipalFactory.Thaw(sub.PrincipalJson);
            var permissions = await sp.GetRequiredService<IUserDirectory>().GetPermissionsAsync(sub.UserId, cancellationToken);
            var definition = await sp.GetRequiredService<ReportDefinitionService>().FindAsync(sub.ReportCode, cancellationToken);
            if (definition is null || permissions.Count == 0)
            {
                sub.RunFailed(null, 1);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Scheduled report {Subscription} stopped: its owner is inactive or the report is gone", sub.Name);
                return false;
            }

            var principal = frozen with { Permissions = permissions, Scopes = await sp.GetRequiredService<PrincipalFactory>().ScopesAsync(sub.UserId, cancellationToken) };
            if (!ReportAccess.CanExport(definition, principal))
            {
                sub.RunFailed(null, 1);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Scheduled report {Subscription} stopped: its owner may no longer export it", sub.Name);
                return false;
            }

            var filters = (JsonSerializer.Deserialize<Dictionary<string, string>>(sub.ParametersJson, JsonColumn.Options) ?? []).ToDictionary(f => f.Key, f => JsonSerializer.SerializeToElement(f.Value));
            var request = new ExportRequest(filters, null, null, sub.Format, true);
            var job = ReportJob.Create(tenant, ReportJobService.Reference(), sub.ReportCode, sub.UserId, ReportJobService.Store(request), sub.Format, PrincipalFactory.Freeze(principal), now, sub.Id);
            db.Jobs.Add(job);
            sub.Ran(job.Id, now, next);
            await db.SaveChangesAsync(cancellationToken);
            await sp.GetRequiredService<ReportAuditor>().WriteAsync(sub.ReportCode, "ScheduledRunQueued", sub.ParametersJson, sub.Format, outcome: job.JobReference, by: sub.UserId, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduled report {Subscription} could not be queued", sub.Name);
            db.ChangeTracker.Clear();
            var again = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            again?.RunFailed(next, settings.ScheduleStopAfterFailures);
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }
    }
}

/// <summary>Keeps each day's KPI numerators and denominators so long trends are read, not recalculated from every transaction.</summary>
internal sealed class DailyKpiAggregator(IServiceScopeFactory scopes, IOptions<ReportsOptions> options, TimeProvider clock, ILogger<DailyKpiAggregator> logger) : BackgroundService
{
    /// <summary>KPIs whose daily parts add up to the parts of any longer period. State-at-a-moment figures (open exceptions) are not kept.</summary>
    public static readonly string[] Kept =
    [
        "SHIPMENTS", "FREIGHT_SPEND", "COST_PER_SHIPMENT", "COST_PER_TON", "COST_PER_TON_KM", "FTL_PCT", "PTL_PCT", "DEDICATED_PCT", "OTP", "OTD", "TENDER_ACCEPTANCE", "WEIGHT_UTIL", "VOLUME_UTIL", "POD_COMPLIANCE",
        "PLACEMENT_COMPLIANCE", "CLAIMS_RATE", "SHORTAGE_PCT", "DAMAGE_PCT", "ETA_ACCURACY", "ETA_ERROR_MIN", "CONSOLIDATION_SAVINGS", "PLANNING_SAVINGS",
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AggregationEnabled)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, options.Value.AggregationIntervalMinutes)));
            do
            {
                try
                {
                    await RunOnceAsync(options.Value.SummaryDays, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "KPI aggregation failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    internal async Task<int> RunOnceAsync(int days, CancellationToken cancellationToken)
    {
        List<Guid> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ReportsDbContext>().Definitions.IgnoreQueryFilters().AsNoTracking().Select(d => d.TenantId).Distinct().ToListAsync(cancellationToken);
        }

        var written = 0;
        foreach (var tenant in tenants)
        {
            written += await RebuildAsync(tenant, null, days, cancellationToken);
        }

        return written;
    }

    /// <summary>Recalculates the last <paramref name="days"/> days for one organisation. Safe to run again.</summary>
    internal async Task<int> RebuildAsync(Guid tenant, Guid? by, int days, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<IAmbientUserContext>().RunAs(tenant, by, $"reports:aggregate:{tenant:N}");
        var db = sp.GetRequiredService<ReportsDbContext>();
        var settings = await sp.GetRequiredService<ReportSettingsStore>().GetAsync(cancellationToken);
        var (providers, _) = sp.GetRequiredService<ReportingProviderResolver>().Resolve(settings);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime.AddMinutes(330));
        var system = new ReportPrincipal(Guid.Empty, tenant, new HashSet<string>(ReportingPermissions.All.Select(p => p.Code)), null, new Dictionary<string, IReadOnlyList<string>>());
        var span = new DateRange(today.AddDays(-days), today);
        var store = new FactStore();
        var existing = await db.DailyKpis.Where(k => k.TransporterId == Guid.Empty && k.Date >= span.From).ToListAsync(cancellationToken);
        var written = 0;
        for (var d = span.From; d <= span.To; d = d.AddDays(1))
        {
            var facts = new ReportFacts(providers, new DateRange(d, d), new ReportFilters(), system, settings, now, span, store);
            foreach (var code in Kept)
            {
                var kpi = KpiCatalogue.Find(code)!;
                var parts = await kpi.Evaluate(facts);
                var row = existing.FirstOrDefault(e => e.Date == d && e.KpiCode == code);
                if (row is null)
                {
                    db.DailyKpis.Add(DailyTransportKpi.Create(tenant, d, Guid.Empty, code, parts.Numerator, parts.Denominator, kpi.Version, now));
                }
                else
                {
                    row.Recompute(parts.Numerator, parts.Denominator, kpi.Version, now);
                }

                written++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("KPI summary rebuilt for tenant {Tenant}: {Rows} daily values over {Days} days", tenant, written, days);
        return written;
    }
}

/// <summary>Reads the kept daily KPI parts when every day of a range is held at the version in force.</summary>
internal sealed class DailyKpiTrendSource(ReportsDbContext db, TimeProvider clock) : ITrendSource
{
    public async Task<KpiParts?> PartsAsync(string kpiCode, DateRange range)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddMinutes(330));
        // Only a period that is wholly in the past is read from the kept values; the period still running (which also holds planned days ahead) is always calculated from the facts.
        var to = range.To;
        if (to >= today || to < range.From || KpiCatalogue.Find(kpiCode) is not { } kpi)
        {
            return null;
        }

        var rows = await db.DailyKpis.AsNoTracking().Where(k => k.TransporterId == Guid.Empty && k.KpiCode == kpiCode && k.Date >= range.From && k.Date <= to).ToListAsync();
        var needed = to.DayNumber - range.From.DayNumber + 1;
        if (rows.Count != needed || rows.Any(r => r.CalculationVersion != kpi.Version))
        {
            return null;
        }

        return new KpiParts(rows.Sum(r => r.Numerator), rows.Sum(r => r.Denominator));
    }
}
