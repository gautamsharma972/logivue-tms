using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tms.BuildingBlocks.Web.Email;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application.Lifecycle;

public sealed class ContractLifecycleOptions
{
    public const string SectionName = "Contracts";

    public bool LifecycleEnabled { get; init; } = true;

    public int LifecycleIntervalMinutes { get; init; } = 60;

    /// <summary>Reminders go out when a contract has this many days (or fewer) left, once each.</summary>
    public int[] ReminderDays { get; init; } = [60, 30, 15, 7];

    /// <summary>The bands the expiry watchlist groups things into (contracts, rates, DPH versions, documents, commitments).</summary>
    public int[] ExpiryBands { get; init; } = [90, 60, 30, 15, 7];
}

/// <summary>
/// Housekeeping that runs across all organisations: marks contracts past their end date as expired and emails the
/// contract owner a renewal reminder at each threshold (once). Safe to run repeatedly.
/// </summary>
internal sealed class ContractLifecycleService(
    IServiceScopeFactory scopes,
    IOptions<ContractLifecycleOptions> options,
    TimeProvider clock,
    ILogger<ContractLifecycleService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.LifecycleEnabled)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); // let the host finish starting
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.LifecycleIntervalMinutes)));
            do
            {
                try
                {
                    await RunOnceAsync(clock.TodayInIndia(), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Contract lifecycle run failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    internal async Task RunOnceAsync(DateOnly today, CancellationToken cancellationToken)
    {
        await ExpireAsync(today, cancellationToken);
        await RemindAsync(today, cancellationToken);
        await RenewalsAsync(today, cancellationToken);
    }

    private async Task ExpireAsync(DateOnly today, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();

        // No tenant is signed in here, so the tenant filter is deliberately bypassed; each row keeps its own tenant.
        var past = await db.Contracts.IgnoreQueryFilters()
            .Where(c => c.Status == ContractStatus.Active && c.EffectiveTo < today)
            .ToListAsync(cancellationToken);

        past.ForEach(c => c.ExpireIfPast(today));
        if (past.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Marked {Count} contract(s) as expired", past.Count);
        }
    }

    private async Task RemindAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var thresholds = options.Value.ReminderDays.OrderBy(d => d).ToArray();
        if (thresholds.Length == 0)
        {
            return;
        }

        var horizon = today.AddDays(thresholds[^1]);
        List<Guid> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ContractsDbContext>().Contracts.IgnoreQueryFilters()
                .Where(c => c.Status == ContractStatus.Active && c.EffectiveTo >= today && c.EffectiveTo <= horizon)
                .Select(c => c.TenantId).Distinct().ToListAsync(cancellationToken);
        }

        foreach (var tenantId in tenants)
        {
            await RemindTenantAsync(tenantId, today, thresholds, cancellationToken);
        }
    }

    private async Task RemindTenantAsync(Guid tenantId, DateOnly today, int[] thresholds, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IAmbientUserContext>().RunAs(tenantId, null, "contract-lifecycle");

        var db = services.GetRequiredService<ContractsDbContext>();
        var horizon = today.AddDays(thresholds[^1]);
        var expiring = await db.Contracts.AsNoTracking()
            .Where(c => c.Status == ContractStatus.Active && c.EffectiveTo >= today && c.EffectiveTo <= horizon)
            .ToListAsync(cancellationToken);

        var ids = expiring.Select(c => c.Id).ToList();
        var already = (await db.ExpiryAlerts.AsNoTracking().Where(a => ids.Contains(a.ContractId)).ToListAsync(cancellationToken))
            .Select(a => (a.ContractId, a.DaysBefore)).ToHashSet();

        var users = services.GetRequiredService<IUserDirectory>();
        var transporters = services.GetRequiredService<ITransporterDirectory>();
        var email = services.GetRequiredService<IEmailSender>();
        var baseUrl = services.GetRequiredService<IOptions<EmailOptions>>().Value.AppBaseUrl.TrimEnd('/');

        var owners = await users.GetContactsAsync(expiring.Where(c => c.OwnerUserId.HasValue).Select(c => c.OwnerUserId!.Value), cancellationToken);
        var names = await transporters.GetAsync(expiring.Select(c => c.TransporterId), cancellationToken);

        foreach (var contract in expiring)
        {
            var daysLeft = contract.EffectiveTo.DayNumber - today.DayNumber;
            var threshold = thresholds.First(t => daysLeft <= t); // the tightest threshold now reached
            if (already.Contains((contract.Id, threshold)))
            {
                continue;
            }

            if (contract.OwnerUserId is { } owner && owners.TryGetValue(owner, out var contact))
            {
                var transporter = names.TryGetValue(contract.TransporterId, out var t) ? t.LegalName : "the transporter";
                await email.SendAsync(new EmailMessage(
                    contact.Email,
                    $"Contract {contract.Reference} ends {(daysLeft == 0 ? "today" : $"in {daysLeft} day(s)")}",
                    $"Hello {contact.FullName},\n\nThe freight contract {contract.Reference} ({contract.Title}) with {transporter} ends on {contract.EffectiveTo:dd MMM yyyy}" +
                    $" ({(daysLeft == 0 ? "today" : $"{daysLeft} day(s) from now")}).\n\nTo keep rates in force without a gap, start a revision now so it can be approved before then:\n{baseUrl}/contracts/{contract.Id}\n"),
                    cancellationToken);
            }
            else
            {
                logger.LogWarning("Contract {Reference} is nearing expiry but has no owner with an email address to remind", contract.Reference);
            }

            // Record this threshold and every looser one so a contract created late does not trigger a burst of reminders.
            foreach (var crossed in thresholds.Where(x => x >= threshold && !already.Contains((contract.Id, x))))
            {
                db.ExpiryAlerts.Add(new ExpiryAlert { ContractId = contract.Id, DaysBefore = crossed, TenantId = tenantId, SentAt = clock.GetUtcNow() });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// When a contract enters its renewal notice period without a next term under way, other modules are told (once), and if the contract is set to renew automatically a
    /// renewal <em>draft</em> is prepared. Nothing is activated: the draft goes through approval like any other.
    /// </summary>
    private async Task RenewalsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        List<Guid> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var horizon = today.AddDays(365);
            tenants = await scope.ServiceProvider.GetRequiredService<ContractsDbContext>().Contracts.IgnoreQueryFilters()
                .Where(c => c.Status == ContractStatus.Active && c.EffectiveTo >= today && c.EffectiveTo <= horizon).Select(c => c.TenantId).Distinct().ToListAsync(cancellationToken);
        }

        foreach (var tenantId in tenants)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IAmbientUserContext>().RunAs(tenantId, null, "contract-lifecycle");
            var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
            var candidates = (await db.Contracts.Where(c => c.Status == ContractStatus.Active && c.EffectiveTo >= today && c.EffectiveTo <= today.AddDays(365)).ToListAsync(cancellationToken))
                .Where(c => c.EffectiveTo.DayNumber - today.DayNumber <= c.RenewalNoticeDays).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var ids = candidates.Select(c => c.Id).ToList();
            var underWay = (await db.Contracts.AsNoTracking().Where(c => c.RevisionOfId != null && ids.Contains(c.RevisionOfId.Value) && c.Status != ContractStatus.Cancelled).Select(c => c.RevisionOfId!.Value).ToListAsync(cancellationToken)).ToHashSet();
            var announced = (await db.ExpiryAlerts.AsNoTracking().Where(a => ids.Contains(a.ContractId) && a.DaysBefore == -1).Select(a => a.ContractId).ToListAsync(cancellationToken)).ToHashSet();
            foreach (var contract in candidates.Where(c => !underWay.Contains(c.Id) && !announced.Contains(c.Id)))
            {
                contract.AnnounceRenewalDue(today);
                db.ExpiryAlerts.Add(new ExpiryAlert { ContractId = contract.Id, DaysBefore = -1, TenantId = tenantId, SentAt = clock.GetUtcNow() });
                if (contract.AutoRenewal)
                {
                    foreach (var navigation in new[] { "RateCards", "DphRules", "Accessorials", "Capacities", "Slas" })
                    {
                        await db.Entry(contract).Collection(navigation).LoadAsync(cancellationToken);
                    }

                    var from = contract.EffectiveTo.AddDays(1);
                    var renewal = contract.CreateRevision(from, from.AddDays(Math.Max(contract.EffectiveTo.DayNumber - contract.EffectiveFrom.DayNumber, 30)), contract.OwnerUserId, RevisionKind.Renewal);
                    if (renewal.IsSuccess)
                    {
                        db.Contracts.Add(renewal.Value);
                        logger.LogInformation("Prepared a renewal draft of {Reference}", contract.Reference);
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
