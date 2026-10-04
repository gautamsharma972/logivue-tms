using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application.PlanningRuns;

internal sealed record PlanningJob(Guid TenantId, Guid? UserId, Guid RunId);

/// <summary>In-process queue of runs waiting to be calculated. A run that is lost on restart stays Running and can be cancelled.</summary>
internal interface IPlanningJobQueue
{
    ValueTask EnqueueAsync(PlanningJob job, CancellationToken cancellationToken);

    IAsyncEnumerable<PlanningJob> ReadAllAsync(CancellationToken cancellationToken);
}

internal sealed class PlanningJobQueue : IPlanningJobQueue
{
    private readonly Channel<PlanningJob> _channel = Channel.CreateUnbounded<PlanningJob>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(PlanningJob job, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<PlanningJob> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Calculates queued plans one at a time, as the tenant and user who asked. Each step the planner reports is saved to the run's log,
/// so a user watching the run sees progress; if the run is cancelled meanwhile, the next save notices and the work stops.
/// </summary>
internal sealed class PlanningWorker(IServiceScopeFactory scopes, IPlanningJobQueue queue, TimeProvider clock, ILogger<PlanningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.ReadAllAsync(stoppingToken))
            {
                await ProcessAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    internal async Task ProcessAsync(PlanningJob job, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IAmbientUserContext>().RunAs(job.TenantId, job.UserId, $"planning:{job.RunId:N}");
        var db = services.GetRequiredService<ShipmentsDbContext>();

        var run = await db.PlanningRuns.FirstOrDefaultAsync(r => r.Id == job.RunId, cancellationToken);
        if (run is null || !run.IsRunning)
        {
            return;
        }

        var started = clock.GetTimestamp();
        logger.LogInformation("Planning run {Run} started: {Orders} order(s), tenant {Tenant}", run.Number, run.OrderIds.Count, job.TenantId);

        async Task Note(string message)
        {
            run.AddLog(message, clock.GetUtcNow());
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new OperationCanceledException("The run was changed or cancelled while it was being calculated.");
            }
        }

        try
        {
            await Note("Started.");
            var input = await services.GetRequiredService<PlanInputBuilder>().BuildAsync(run.PlanningDate, run.OrderIds, run.Options, [], cancellationToken);
            if (input.IsFailure)
            {
                run.Fail($"Could not start: {input.Error.Description}", clock.GetUtcNow());
            }
            else
            {
                var plan = await services.GetRequiredService<IPlanningOptimizer>().OptimizeAsync(input.Value with { Progress = Note }, cancellationToken);
                run.Complete(plan, clock.GetUtcNow());
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Planning run {Run} finished as {Status} in {Elapsed:0} ms: {Vehicles} vehicle(s), {Unplanned} unplanned",
                run.Number, run.Status, clock.GetElapsedTime(started).TotalMilliseconds, run.Plan.Vehicles.Count, run.Plan.Unplanned.Count);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Planning run {Run} stopped: it was cancelled while running", run.Number);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Planning run {Run} was cancelled before its result could be saved", run.Number);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Planning run {Run} failed", run.Number);
            await FailAsync(job, ex.Message, cancellationToken);
        }
    }

    private async Task FailAsync(PlanningJob job, string message, CancellationToken cancellationToken)
    {
        // A fresh scope: the one that failed may hold a broken context.
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IAmbientUserContext>().RunAs(job.TenantId, job.UserId, $"planning:{job.RunId:N}");
        var db = scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>();
        var run = await db.PlanningRuns.FirstOrDefaultAsync(r => r.Id == job.RunId, cancellationToken);
        if (run?.Fail($"Planning failed: {(message.Length > 300 ? message[..300] : message)}", clock.GetUtcNow()).IsSuccess == true)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // cancelled in the meantime: nothing more to record
            }
        }
    }
}
