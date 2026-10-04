using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Application.Shipments;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Application.Tendering;

/// <summary>
/// What keeps a tender moving: missed deadlines, a refusal that passes the load to the next transporter, an acceptance that closes the
/// tender. Deadlines are checked whenever shipments or tenders are read or answered (there is no cross-tenant background worker), so the
/// shipment always shows where it stands.
/// </summary>
internal sealed class TenderLifecycle(
    ShipmentsDbContext db, QuoteShipmentHandler quoter, ITransporterPlanningPolicy policy, TimeProvider clock)
{
    /// <summary>The open tender of a shipment, tracked for change, or null.</summary>
    public Task<TenderRound?> OpenRoundAsync(Guid shipmentId, CancellationToken cancellationToken) =>
        db.TenderRounds.Include(r => r.Invitees).FirstOrDefaultAsync(r => r.ShipmentId == shipmentId && r.Status == TenderStatus.Open, cancellationToken);

    /// <summary>Expires every invitation past its deadline in this tenant and moves the affected tenders on.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var dueRounds = await db.TenderInvitees.AsNoTracking()
            .Where(i => i.Status == InviteeStatus.Sent && i.Deadline <= now)
            .Select(i => i.TenderId).Distinct().ToListAsync(cancellationToken);
        if (dueRounds.Count == 0)
        {
            return;
        }

        foreach (var roundId in dueRounds)
        {
            var round = await db.TenderRounds.Include(r => r.Invitees).FirstOrDefaultAsync(r => r.Id == roundId && r.Status == TenderStatus.Open, cancellationToken);
            if (round is null || round.ExpireDue(now).Count == 0)
            {
                continue;
            }

            var shipment = await db.Shipments.Include(s => s.Orders).FirstAsync(s => s.Id == round.ShipmentId, cancellationToken);
            if (round.Mode == TenderMode.Sequential)
            {
                shipment.ExpireTender(now);
                await AdvanceAsync(round, shipment, now, cancellationToken);
            }
            else
            {
                round.StartNext(now); // closes the tender when nobody can answer or be awarded
                if (!round.IsOpen)
                {
                    shipment.EndBidding();
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The tendered transporter refused a sequential offer (the shipment is back in Draft): try the next one.</summary>
    public async Task AfterRejectedAsync(Shipment shipment, Guid transporterId, string reason, CancellationToken cancellationToken)
    {
        var round = await OpenRoundAsync(shipment.Id, cancellationToken);
        if (round is not { Mode: TenderMode.Sequential } || round.Reject(transporterId, reason, clock.GetUtcNow()).IsFailure)
        {
            return;
        }

        await AdvanceAsync(round, shipment, clock.GetUtcNow(), cancellationToken);
    }

    public async Task AfterAcceptedAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        var round = await OpenRoundAsync(shipment.Id, cancellationToken);
        if (round is { Mode: TenderMode.Sequential } && shipment.TransporterId is { } carrier)
        {
            round.MarkAccepted(carrier, clock.GetUtcNow());
        }
    }

    /// <summary>The shipment was withdrawn or cancelled: close its tender too.</summary>
    public async Task AfterWithdrawnAsync(Shipment shipment, string reason, CancellationToken cancellationToken)
    {
        var round = await OpenRoundAsync(shipment.Id, cancellationToken);
        round?.Cancel(reason, clock.GetUtcNow());
    }

    /// <summary>Contacts the next waiting transporter of a sequential tender, skipping any that can no longer be offered the load.</summary>
    public async Task AdvanceAsync(TenderRound round, Shipment shipment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        FreightQuoteSet? set = null;
        IReadOnlyDictionary<Guid, TransporterStanding>? standings = null;
        while (round.StartNext(now) is { } next)
        {
            set ??= (await quoter.QuoteAsync(shipment, clock, cancellationToken)) is { IsSuccess: true } quoted ? quoted.Value : new FreightQuoteSet([], null);
            standings ??= await StandingsAsync(shipment, round.Invitees.Select(i => i.TransporterId).ToList(), cancellationToken);

            var chosen = set.Quotes.FirstOrDefault(q => q.ContractId == next.ContractId);
            if (standings.TryGetValue(next.TransporterId, out var standing) && !standing.Allowed)
            {
                round.Skip(next, standing.Reason ?? "Not eligible for this load.", now);
            }
            else if (chosen is null)
            {
                round.Skip(next, "Its contract no longer prices this load.", now);
            }
            else if (shipment.Tender(chosen.TransporterId, chosen, null, null, now) is { IsFailure: true } failed)
            {
                round.Skip(next, failed.Error.Description, now);
            }
            else
            {
                return;
            }
        }

        // Nobody left: the shipment is back in Draft for the planner (Tender and Reject already put it there).
    }

    /// <summary>Whether each transporter may be given this load under the planning rules (suspended, expired papers, a rule against it…).</summary>
    public async Task<IReadOnlyDictionary<Guid, TransporterStanding>> StandingsAsync(Shipment shipment, IReadOnlyCollection<Guid> transporterIds, CancellationToken cancellationToken)
    {
        var last = shipment.Orders.Where(o => !o.IsReturn).OrderByDescending(o => o.DropSequence).FirstOrDefault();
        var drop = last is null
            ? null
            : await db.Orders.AsNoTracking().Where(o => o.Id == last.OrderId).Select(o => new { o.DropState, o.DropCity }).FirstOrDefaultAsync(cancellationToken);
        return await policy.GetStandingsAsync(
            transporterIds, shipment.PlannedPickupDate, shipment.OriginState, shipment.OriginCity, drop?.DropState, drop?.DropCity, shipment.Mode, false, cancellationToken);
    }
}
