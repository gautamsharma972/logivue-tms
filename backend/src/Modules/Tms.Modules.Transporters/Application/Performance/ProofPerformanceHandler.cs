using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Application.Performance;

/// <summary>A null rate means there was nothing to measure, which is different from zero.</summary>
public sealed record ProofPerformanceDto(
    DateOnly From, DateOnly To, int Deliveries, int Delivered, decimal? OnTimeRate, decimal? ProofInTimeRate, decimal? FirstTimeAcceptanceRate, decimal? RejectionRate, decimal? ShortageRate,
    decimal? DamageRate, int Refusals, int Failures);

internal sealed class ProofPerformanceHandler(TransportersDbContext db, PerformanceAccess access, TimeProvider clock)
{
    public async Task<Result<ProofPerformanceDto>> HandleAsync(Guid transporterId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (access.CheckRead(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var end = to ?? clock.TodayInIndia();
        var start = from ?? end.AddDays(-90);
        var lower = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
        var upper = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
        var rows = await db.ProofPerformances.AsNoTracking().Where(p => p.TransporterId == transporterId && p.UpdatedAt >= lower && p.UpdatedAt < upper).ToListAsync(cancellationToken);

        static decimal? Rate(int part, int whole) => whole == 0 ? null : Math.Round((decimal)part / whole, 4);
        var delivered = rows.Where(r => r.DeliveredAt is not null).ToList();
        var judged = rows.Where(r => r.AcceptedAt is not null || r.Rejections > 0).ToList();
        return new ProofPerformanceDto(
            start, end, rows.Count, delivered.Count,
            Rate(delivered.Count(r => r.OnTime == true), delivered.Count(r => r.OnTime is not null)),
            Rate(rows.Count(r => r.SubmittedWithinSla == true), rows.Count(r => r.SubmittedWithinSla is not null)),
            Rate(rows.Count(r => r.AcceptedFirstTime == true), rows.Count(r => r.AcceptedFirstTime is not null)),
            Rate(judged.Count(r => r.Rejections > 0), judged.Count),
            Rate(delivered.Count(r => r.ShortQuantity > 0), delivered.Count),
            Rate(delivered.Count(r => r.DamagedQuantity > 0), delivered.Count),
            rows.Count(r => r.Refused), rows.Count(r => r.Failed));
    }
}
