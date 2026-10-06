using Microsoft.EntityFrameworkCore;
using Tms.BuildingBlocks.Web.Http;
using Tms.Modules.Deliveries.Infrastructure.Persistence;
using Tms.Modules.Tracking;
using Tms.SharedKernel.Contracts;

namespace Tms.Api;

/// <summary>
/// Conveniences for development and tests: they exist only until real document modules call <see cref="IApprovalGateway"/> themselves, and so a demo or a test can
/// make a delivery old without waiting. Mapped in Development and Testing only — never in production.
/// </summary>
internal static class DevEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/dev/approvals/submit", async (SubmitApproval request, IApprovalGateway gateway, CancellationToken ct) =>
                (await gateway.SubmitAsync(request, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithTags("Dev")
            .WithName("DevSubmitApproval");

        // Moves every timestamp of one delivery (and its proofs) back in time, so ageing and overdue notices can be seen. The caller's tenant only.
        app.MapPost("/api/v1/dev/deliveries/{id:guid}/age", async (Guid id, AgeRequest body, DeliveriesDbContext db, CancellationToken ct) =>
            {
                var hours = -Math.Abs(body.Hours);
                await db.Deliveries.Where(d => d.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.PlannedDeliveryAt, d => d.PlannedDeliveryAt.AddHours(hours))
                    .SetProperty(d => d.WindowEnd, d => d.WindowEnd == null ? null : d.WindowEnd.Value.AddHours(hours))
                    .SetProperty(d => d.ActualArrivalAt, d => d.ActualArrivalAt == null ? null : d.ActualArrivalAt.Value.AddHours(hours))
                    .SetProperty(d => d.ActualDeliveryAt, d => d.ActualDeliveryAt == null ? null : d.ActualDeliveryAt.Value.AddHours(hours)), ct);
                await db.Pods.Where(p => p.DeliveryId == id).ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.FirstSubmittedAt, p => p.FirstSubmittedAt == null ? null : p.FirstSubmittedAt.Value.AddHours(hours))
                    .SetProperty(p => p.SubmittedAt, p => p.SubmittedAt == null ? null : p.SubmittedAt.Value.AddHours(hours))
                    .SetProperty(p => p.ApprovedAt, p => p.ApprovedAt == null ? null : p.ApprovedAt.Value.AddHours(hours))
                    .SetProperty(p => p.ReviewedAt, p => p.ReviewedAt == null ? null : p.ReviewedAt.Value.AddHours(hours))
                    .SetProperty(p => p.ReturnedAt, p => p.ReturnedAt == null ? null : p.ReturnedAt.Value.AddHours(hours))
                    .SetProperty(p => p.ResubmittedAt, p => p.ResubmittedAt == null ? null : p.ResubmittedAt.Value.AddHours(hours))
                    .SetProperty(p => p.DeliveryCompletedAt, p => p.DeliveryCompletedAt == null ? null : p.DeliveryCompletedAt.Value.AddHours(hours)), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithTags("Dev")
            .WithName("DevAgeDelivery");

        // Runs the tracking demo day (trips in every condition) for the caller's tenant, through the real pipeline. Carriers come from the caller (the seed script).
        app.MapPost("/api/v1/dev/tracking/seed-demo", async (SeedTrackingRequest body, ITrackingDemoSeeder seeder, CancellationToken ct) =>
                Results.Ok(await seeder.SeedAsync(new DemoSeedRequest([.. (body.Carriers ?? []).Select(c => new DemoCarrier(c.Id, c.Name))]), ct)))
            .RequireAuthorization()
            .WithTags("Dev")
            .WithName("DevSeedTrackingDemo");
    }

    internal sealed record AgeRequest(double Hours);

    internal sealed record SeedTrackingCarrier(Guid Id, string Name);

    internal sealed record SeedTrackingRequest(List<SeedTrackingCarrier>? Carriers);
}
