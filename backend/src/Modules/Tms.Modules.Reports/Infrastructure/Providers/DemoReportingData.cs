using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Reports.Infrastructure.Providers;

/// <summary>
/// The local demonstration provider: serves a generated dataset through all five reporting contracts, so Reports runs, and can be shown and tested,
/// without the other modules (and is the stand-in for any module that is not installed).
/// </summary>
internal sealed class DemoReportingData(TimeProvider clock) :
    IPlanningReportingProvider, ITransporterReportingProvider, IPodReportingProvider, ITrackingReportingProvider, IFreightContractReportingProvider
{
    private static readonly object Gate = new();
    private static DateOnly _builtFor;
    private static DemoDataset? _dataset;

    private DemoDataset Data
    {
        get
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddMinutes(330));
            lock (Gate)
            {
                if (_dataset is null || _builtFor != today)
                {
                    _dataset = DemoDataGenerator.Build(today);
                    _builtFor = today;
                }

                return _dataset;
            }
        }
    }

    public DemoDataset Snapshot => Data;

    private static DateOnly D(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime.AddMinutes(330));

    private static Task<IReadOnlyList<T>> Done<T>(IEnumerable<T> rows) => Task.FromResult<IReadOnlyList<T>>(rows.ToList());

    private static bool Own(ReportingWindow w, Guid? id) => w.TransporterId is null || id == w.TransporterId;

    // planning
    public Task<IReadOnlyList<ShipmentReportFact>> ShipmentsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Shipments.Where(s => w.Contains(s.PlannedPickupDate) && Own(w, s.TransporterId)));

    public Task<IReadOnlyList<PlanningRunFact>> RunsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(w.TransporterId is null ? Data.Runs.Where(r => w.Contains(r.PlanningDate)) : []);

    public Task<IReadOnlyList<PlanVehicleFact>> VehiclesAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Vehicles.Where(v => w.Contains(v.PlanningDate) && Own(w, v.TransporterId)));

    public Task<IReadOnlyList<UnplannedOrderFact>> UnplannedAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(w.TransporterId is null ? Data.Unplanned.Where(u => w.Contains(u.PlanningDate)) : []);

    public Task<IReadOnlyList<TenderFact>> TendersAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Tenders.Where(t => w.Contains(D(t.OfferedAt)) && Own(w, t.TransporterId)));

    // transporters
    public Task<IReadOnlyList<ExecutionFact>> ExecutionsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done<ExecutionFact>([]);

    public Task<IReadOnlyList<TransporterFact>> TransportersAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Transporters.Where(t => Own(w, t.Id)));

    public Task<IReadOnlyList<ScorecardFact>> ScorecardsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Scorecards.Where(s => w.Contains(s.PeriodEnd) && Own(w, s.TransporterId)));

    public Task<IReadOnlyList<PlacementFact>> PlacementsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Placements.Where(p => w.Contains(D(p.RequiredBy)) && Own(w, p.TransporterId)));

    Task<IReadOnlyList<ExceptionFact>> ITransporterReportingProvider.ExceptionsAsync(ReportingWindow w, CancellationToken cancellationToken) => Done(Data.TransporterExceptions.Where(e => Own(w, e.TransporterId)));

    // deliveries
    public Task<IReadOnlyList<DeliveryFact>> DeliveriesAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Deliveries.Where(d => w.Contains(d.Date) && Own(w, d.TransporterId)));

    public Task<IReadOnlyList<PodFact>> PodsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Pods.Where(p => w.Contains(p.Date) && Own(w, p.TransporterId)));

    public Task<IReadOnlyList<DiscrepancyFact>> DiscrepanciesAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Discrepancies.Where(d => w.Contains(d.Date) && Own(w, d.TransporterId)));

    Task<IReadOnlyList<ExceptionFact>> IPodReportingProvider.ExceptionsAsync(ReportingWindow w, CancellationToken cancellationToken) => Done(Data.DeliveryExceptions.Where(e => Own(w, e.TransporterId)));

    // tracking
    public Task<IReadOnlyList<TrackFact>> TripsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Trips.Where(t => w.Contains(t.Date) && Own(w, t.TransporterId)));

    public Task<IReadOnlyList<DeviationFact>> DeviationsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Deviations.Where(d => w.Contains(D(d.DetectedAt)) && Own(w, d.TransporterId)));

    public Task<IReadOnlyList<DwellFact>> DwellsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Dwells.Where(d => w.Contains(D(d.At)) && Own(w, d.TransporterId)));

    public Task<IReadOnlyList<GapFact>> GapsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Gaps.Where(g => w.Contains(D(g.GapStart)) && Own(w, g.TransporterId)));

    Task<IReadOnlyList<ExceptionFact>> ITrackingReportingProvider.ExceptionsAsync(ReportingWindow w, CancellationToken cancellationToken) => Done(Data.TrackingExceptions.Where(e => Own(w, e.TransporterId)));

    // contracts
    public Task<IReadOnlyList<ContractFact>> ContractsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Contracts.Where(c => Own(w, c.TransporterId)));

    public Task<IReadOnlyList<RateFact>> RatesAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Rates.Where(r => Own(w, r.TransporterId)));

    public Task<IReadOnlyList<DphFact>> DphAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Dph.Where(d => Own(w, d.TransporterId)));

    public Task<IReadOnlyList<RatingFact>> RatingsAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(Data.Ratings.Where(r => w.Contains(D(r.RatedAt)) && Own(w, r.TransporterId)));

    public Task<IReadOnlyList<CoverageFact>> CoverageAsync(ReportingWindow w, CancellationToken cancellationToken = default) => Done(w.TransporterId is null ? Data.Coverage : []);
}
