using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Application.Shipments;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application.Tendering;

internal static class TenderErrors
{
    public static readonly Error NotFound = Error.NotFound("tenders.not_found", "No tender found for this shipment.");
}

/// <summary>Shapes tenders for the screen. A vendor sees only its own invitation, never the contract price or who else was invited.</summary>
internal sealed class TenderMapper(ShipmentAccess access, ITransporterDirectory transporters)
{
    public async Task<TenderDto?> ToDtoAsync(TenderRound round, CancellationToken cancellationToken)
    {
        var mine = access.IsVendor ? access.VendorTransporterId : null;
        var invitees = mine is { } id ? round.Invitees.Where(i => i.TransporterId == id).ToList() : round.Invitees.ToList();
        if (access.IsVendor && invitees.Count == 0)
        {
            return null;
        }

        var names = await transporters.GetAsync(invitees.Select(i => i.TransporterId), cancellationToken);
        string Name(Guid t) => names.TryGetValue(t, out var info) ? info.LegalName : "Unknown transporter";
        var showCost = !access.IsVendor;
        var inviteeIds = invitees.Select(i => i.Id).ToHashSet();

        return new TenderDto(
            round.Id, round.Number, round.ShipmentId, round.ShipmentNumber, round.Mode, round.Status, round.ResponseMinutes, round.Notes,
            showCost || round.AwardedTransporterId == mine ? round.AwardedTransporterId : null, round.ClosedAt, showCost ? round.CloseReason : null, round.CreatedAt,
            invitees.OrderBy(i => i.Sequence).Select(i => new TenderInviteeDto(
                i.Id, i.TransporterId, Name(i.TransporterId), i.Sequence, i.Status, i.SentAt, i.Deadline, i.RespondedAt, i.Reason,
                showCost ? i.ContractReference : null, showCost ? i.QuotedTotal : null, i.CounterRate, i.CounterComment, i.CounterStatus, i.AgreedRate,
                i.BidVehicleId, i.BidVehicleRegistration, i.BidDriverId, i.BidDriverName)).ToList(),
            round.Events.Where(e => showCost || (e.InviteeId is { } v && inviteeIds.Contains(v))).OrderBy(e => e.At)
                .Select(e => new TenderEventDto(e.At, e.Type, e.InviteeId, e.InviteeId is { } v ? Name(round.Invitees.First(i => i.Id == v).TransporterId) : null, e.Comments)).ToList());
    }
}

/// <summary>Opens a sequential or broadcast tender on a draft shipment.</summary>
internal sealed class StartTenderHandler(
    ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader, QuoteShipmentHandler quoter, TenderLifecycle lifecycle, TenderMapper mapper,
    ISequenceGenerator sequences, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<TenderDto>> HandleAsync(Guid shipmentId, StartTenderRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var found = await loader.FindAsync(shipmentId, tracked: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var shipment = found.Value;
        if (shipment.Status != ShipmentStatus.Draft)
        {
            return Error.Conflict("shipments.not_draft", "Only a draft shipment can be tendered. Withdraw it from the transporter first.");
        }

        var set = await quoter.QuoteAsync(shipment, clock, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var picked = new List<FreightQuoteResult>();
        foreach (var contractId in request.ContractIds)
        {
            var quote = set.Value.Quotes.FirstOrDefault(q => q.ContractId == contractId);
            if (quote is null)
            {
                return Error.Validation("shipments.quote_unavailable", set.Value.Message ?? "One of those contracts can no longer price this shipment. Refresh the quotes.");
            }

            picked.Add(quote);
        }

        var standings = await lifecycle.StandingsAsync(shipment, picked.Select(q => q.TransporterId).Distinct().ToList(), cancellationToken);
        var barred = picked.Where(q => standings.TryGetValue(q.TransporterId, out var s) && !s.Allowed).ToList();
        if (barred.Count > 0)
        {
            var why = string.Join(" ", barred.Select(q => $"{q.TransporterName}: {standings[q.TransporterId].Reason?.TrimEnd('.')}."));
            return Error.Conflict("tenders.transporter_barred", $"These transporters cannot be offered this load. {why}");
        }

        var now = clock.GetUtcNow();
        var number = $"TND-{await sequences.NextAsync(tenantId, "tender", cancellationToken):D5}";
        var round = TenderRound.Start(tenantId, number, shipment.Id, shipment.Number, request.Mode, request.ResponseMinutes, request.Notes, picked, now);
        if (round.IsFailure)
        {
            return round.Error;
        }

        var allocated = request.Mode == TenderMode.Sequential
            ? shipment.Tender(picked[0].TransporterId, picked[0], null, null, now)
            : shipment.StartBidding(now);
        if (allocated.IsFailure)
        {
            return allocated.Error;
        }

        db.TenderRounds.Add(round.Value);
        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(round.Value, cancellationToken) ?? (Result<TenderDto>)TenderErrors.NotFound;
    }
}

/// <summary>The tenders of a shipment, newest first (a vendor sees only those it was invited to).</summary>
internal sealed class ListTendersHandler(ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader, TenderLifecycle lifecycle, TenderMapper mapper)
{
    public async Task<Result<IReadOnlyList<TenderDto>>> HandleAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        if (!access.CanRead && !access.IsVendor)
        {
            return ShipmentAccess.Forbidden;
        }

        await lifecycle.SweepAsync(cancellationToken);
        var shipment = await loader.FindAsync(shipmentId, tracked: false, cancellationToken);
        if (shipment.IsFailure)
        {
            return shipment.Error;
        }

        var rounds = await db.TenderRounds.AsNoTracking().Include(r => r.Invitees).Include(r => r.Events)
            .Where(r => r.ShipmentId == shipmentId).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Number).ToListAsync(cancellationToken);
        var result = new List<TenderDto>();
        foreach (var round in rounds)
        {
            if (await mapper.ToDtoAsync(round, cancellationToken) is { } dto)
            {
                result.Add(dto);
            }
        }

        return result;
    }
}

/// <summary>A transporter's answers to a tender: bid (broadcast), counter-offer, or decline. Planners may answer on a transporter's behalf.</summary>
internal sealed class TenderResponseHandler(
    ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader, TenderLifecycle lifecycle, TenderMapper mapper, IFleetDirectory fleet, TimeProvider clock)
{
    public Task<Result<TenderDto>> BidAsync(Guid shipmentId, BidRequest request, CancellationToken cancellationToken) =>
        RespondAsync(shipmentId, request.TransporterId, async (round, shipment, carrier, now) =>
        {
            var vehicle = await fleet.GetVehicleAsync(request.VehicleId, cancellationToken);
            var driver = await fleet.GetDriverAsync(request.DriverId, cancellationToken);
            if (vehicle is null || driver is null)
            {
                return Error.NotFound("shipments.fleet_not_found", "That vehicle or driver was not found.");
            }

            var usable = Shipment.CheckFleet(vehicle, driver, carrier, shipment.LoadKg);
            return usable.IsFailure ? usable : round.Bid(carrier, vehicle, driver, request.CounterRate, request.Comments, now);
        }, cancellationToken);

    public Task<Result<TenderDto>> CounterAsync(Guid shipmentId, CounterOfferRequest request, CancellationToken cancellationToken) =>
        RespondAsync(shipmentId, request.TransporterId, (round, _, carrier, now) => Task.FromResult(round.Counter(carrier, request.Rate, request.Comments, now)), cancellationToken);

    public Task<Result<TenderDto>> DeclineAsync(Guid shipmentId, DeclineTenderRequest request, CancellationToken cancellationToken) =>
        RespondAsync(shipmentId, request.TransporterId, async (round, shipment, carrier, now) =>
        {
            var declined = round.Reject(carrier, request.Reason, now);
            if (declined.IsFailure || round.Mode == TenderMode.Broadcast)
            {
                if (declined.IsSuccess)
                {
                    round.StartNext(now); // closes the tender once nobody is left
                }

                return declined;
            }

            // Sequential: the shipment itself was tendered to this transporter; take it back and pass the load on.
            var back = shipment.Reject(request.Reason, now);
            if (back.IsSuccess)
            {
                await lifecycle.AdvanceAsync(round, shipment, now, cancellationToken);
            }

            return back;
        }, cancellationToken);

    private async Task<Result<TenderDto>> RespondAsync(
        Guid shipmentId, Guid? onBehalfOf, Func<TenderRound, Shipment, Guid, DateTimeOffset, Task<Result>> act, CancellationToken cancellationToken)
    {
        if (!access.CanRespond)
        {
            return ShipmentAccess.Forbidden;
        }

        await lifecycle.SweepAsync(cancellationToken);
        var shipment = await loader.FindAsync(shipmentId, tracked: true, cancellationToken);
        if (shipment.IsFailure)
        {
            return shipment.Error;
        }

        var carrier = access.IsVendor ? access.VendorTransporterId : onBehalfOf;
        if (carrier is not { } transporterId)
        {
            return Error.Validation("tenders.transporter_required", "Say which transporter is answering.");
        }

        var round = await lifecycle.OpenRoundAsync(shipmentId, cancellationToken);
        if (round is null || round.InviteeFor(transporterId) is null)
        {
            return TenderErrors.NotFound;
        }

        var result = await act(round, shipment.Value, transporterId, clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error;
        }

        if (!round.IsOpen && shipment.Value.Status == ShipmentStatus.Bidding)
        {
            shipment.Value.EndBidding();
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(round, cancellationToken) ?? (Result<TenderDto>)TenderErrors.NotFound;
    }
}

/// <summary>A planner's decisions on a tender: decide a counter-offer, award a bid, cancel.</summary>
internal sealed class TenderDecisionHandler(
    ShipmentsDbContext db, ShipmentAccess access, ShipmentLoader loader, QuoteShipmentHandler quoter, TenderLifecycle lifecycle, TenderMapper mapper,
    IFleetDirectory fleet, TimeProvider clock)
{
    public async Task<Result<TenderDto>> DecideCounterAsync(Guid shipmentId, CounterDecisionRequest request, CancellationToken cancellationToken)
    {
        return await RunAsync(shipmentId, async (round, shipment, now) =>
        {
            if (!request.Agree)
            {
                var declined = round.DeclineCounter(request.InviteeId, request.Comments, now);
                if (declined.IsFailure)
                {
                    return declined;
                }

                if (round.Mode == TenderMode.Sequential)
                {
                    var back = shipment.Reject(declined.Value.Reason!, now);
                    if (back.IsFailure)
                    {
                        return back;
                    }

                    await lifecycle.AdvanceAsync(round, shipment, now, cancellationToken);
                }
                else
                {
                    round.StartNext(now);
                    if (!round.IsOpen)
                    {
                        shipment.EndBidding();
                    }
                }

                return Result.Success();
            }

            var agreed = round.AgreeCounter(request.InviteeId, request.Comments, now);
            if (agreed.IsFailure)
            {
                return agreed;
            }

            // Sequential: the shipment is already tendered to this transporter, so the agreed rate becomes its estimate now. Broadcast: at the award.
            return round.Mode == TenderMode.Sequential ? shipment.ApplyAgreedRate(agreed.Value.AgreedRate!.Value) : Result.Success();
        }, cancellationToken);
    }

    public async Task<Result<TenderDto>> AwardAsync(Guid shipmentId, AwardTenderRequest request, CancellationToken cancellationToken)
    {
        return await RunAsync(shipmentId, async (round, shipment, now) =>
        {
            var winner = round.Invitees.FirstOrDefault(i => i.Id == request.InviteeId);
            if (winner?.BidVehicleId is not { } vehicleId || winner.BidDriverId is not { } driverId)
            {
                return round.Award(request.InviteeId, now); // reports why
            }

            var vehicle = await fleet.GetVehicleAsync(vehicleId, cancellationToken);
            var driver = await fleet.GetDriverAsync(driverId, cancellationToken);
            if (vehicle is null || driver is null)
            {
                return Error.NotFound("shipments.fleet_not_found", "The vehicle or driver in that bid no longer exists.");
            }

            // Prices and papers are judged again now: rates, diesel and expiry dates may have moved since the bid.
            var set = await quoter.QuoteAsync(shipment, clock, cancellationToken);
            var chosen = set.IsSuccess ? set.Value.Quotes.FirstOrDefault(q => q.ContractId == winner.ContractId) : null;
            if (chosen is null)
            {
                return Error.Validation("shipments.quote_unavailable", "That transporter's contract can no longer price this shipment.");
            }

            var awarded = round.Award(request.InviteeId, now);
            if (awarded.IsFailure)
            {
                return awarded;
            }

            var agreedRate = winner.CounterStatus == CounterStatus.Agreed ? winner.AgreedRate : null;
            return shipment.Award(winner.TransporterId, chosen, agreedRate, vehicle, driver, now);
        }, cancellationToken);
    }

    public async Task<Result<TenderDto>> CancelAsync(Guid shipmentId, ReasonRequest request, CancellationToken cancellationToken)
    {
        return await RunAsync(shipmentId, (round, shipment, now) =>
        {
            var cancelled = round.Cancel(request.Reason, now);
            if (cancelled.IsFailure)
            {
                return Task.FromResult(cancelled);
            }

            var back = shipment.Status switch
            {
                ShipmentStatus.Bidding => shipment.EndBidding(),
                ShipmentStatus.Tendered => shipment.Withdraw(),
                _ => Result.Success(),
            };
            return Task.FromResult(back);
        }, cancellationToken);
    }

    private async Task<Result<TenderDto>> RunAsync(Guid shipmentId, Func<TenderRound, Shipment, DateTimeOffset, Task<Result>> act, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        await lifecycle.SweepAsync(cancellationToken);
        var shipment = await loader.FindAsync(shipmentId, tracked: true, cancellationToken);
        if (shipment.IsFailure)
        {
            return shipment.Error;
        }

        var round = await lifecycle.OpenRoundAsync(shipmentId, cancellationToken);
        if (round is null)
        {
            return TenderErrors.NotFound;
        }

        var result = await act(round, shipment.Value, clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await mapper.ToDtoAsync(round, cancellationToken) ?? (Result<TenderDto>)TenderErrors.NotFound;
    }
}
