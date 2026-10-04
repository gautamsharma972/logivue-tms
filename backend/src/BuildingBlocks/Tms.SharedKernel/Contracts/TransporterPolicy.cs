namespace Tms.SharedKernel.Contracts;

/// <param name="Allowed">False when the transporter must not be given this load (suspended, expired documents, a planning rule, urgency, a performance limit).</param>
/// <param name="Reason">Why not, in words a planner can act on.</param>
/// <param name="Preferred">A planning rule favours this transporter here. Planning uses it to break ties between equal prices.</param>
/// <param name="Score">The transporter's latest overall performance score, if one has been generated.</param>
public sealed record TransporterStanding(Guid TransporterId, bool Allowed, string? Reason, bool Preferred, decimal? Score);

/// <summary>
/// What planning may do with each transporter: owned by Transporters, consumed by Planning. A transporter that is barred here is not offered a load, whatever
/// its price; the price and the fleet are still judged by Contracts and the fleet directory.
/// </summary>
public interface ITransporterPlanningPolicy
{
    Task<IReadOnlyDictionary<Guid, TransporterStanding>> GetStandingsAsync(
        IReadOnlyCollection<Guid> transporterIds, DateOnly date, string originState, string? originCity, string? destinationState, string? destinationCity, FreightMode mode, bool urgent,
        CancellationToken cancellationToken = default);
}
