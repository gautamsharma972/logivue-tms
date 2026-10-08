using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Reports;

/// <summary>A provider holding exactly the facts a test puts in it, so a KPI can be checked against numbers worked out by hand.</summary>
internal sealed class TestProvider : IPlanningReportingProvider, ITransporterReportingProvider, IPodReportingProvider, ITrackingReportingProvider, IFreightContractReportingProvider
{
    public List<ShipmentReportFact> Shipments { get; } = [];
    public List<PlanningRunFact> Runs { get; } = [];
    public List<PlanVehicleFact> Vehicles { get; } = [];
    public List<UnplannedOrderFact> Unplanned { get; } = [];
    public List<TenderFact> Tenders { get; } = [];
    public List<TransporterFact> Transporters { get; } = [];
    public List<ScorecardFact> Scorecards { get; } = [];
    public List<PlacementFact> Placements { get; } = [];
    public List<ExecutionFact> Executions { get; } = [];
    public List<DeliveryFact> Deliveries { get; } = [];
    public List<PodFact> Pods { get; } = [];
    public List<DiscrepancyFact> Discrepancies { get; } = [];
    public List<TrackFact> Trips { get; } = [];
    public List<DeviationFact> Deviations { get; } = [];
    public List<DwellFact> Dwells { get; } = [];
    public List<GapFact> Gaps { get; } = [];
    public List<ContractFact> Contracts { get; } = [];
    public List<RateFact> Rates { get; } = [];
    public List<DphFact> Dph { get; } = [];
    public List<RatingFact> Ratings { get; } = [];
    public List<CoverageFact> CoverageRows { get; } = [];
    public List<ExceptionFact> Exceptions { get; } = [];

    private static Task<IReadOnlyList<T>> Done<T>(List<T> rows) => Task.FromResult<IReadOnlyList<T>>(rows.ToList());

    public Task<IReadOnlyList<ShipmentReportFact>> ShipmentsAsync(ReportingWindow w, CancellationToken c = default) => Done(Shipments);
    public Task<IReadOnlyList<PlanningRunFact>> RunsAsync(ReportingWindow w, CancellationToken c = default) => Done(Runs);
    public Task<IReadOnlyList<PlanVehicleFact>> VehiclesAsync(ReportingWindow w, CancellationToken c = default) => Done(Vehicles);
    public Task<IReadOnlyList<UnplannedOrderFact>> UnplannedAsync(ReportingWindow w, CancellationToken c = default) => Done(Unplanned);
    public Task<IReadOnlyList<TenderFact>> TendersAsync(ReportingWindow w, CancellationToken c = default) => Done(Tenders);
    public Task<IReadOnlyList<TransporterFact>> TransportersAsync(ReportingWindow w, CancellationToken c = default) => Done(Transporters);
    public Task<IReadOnlyList<ScorecardFact>> ScorecardsAsync(ReportingWindow w, CancellationToken c = default) => Done(Scorecards);
    public Task<IReadOnlyList<PlacementFact>> PlacementsAsync(ReportingWindow w, CancellationToken c = default) => Done(Placements);
    public Task<IReadOnlyList<ExecutionFact>> ExecutionsAsync(ReportingWindow w, CancellationToken c = default) => Done(Executions);
    Task<IReadOnlyList<ExceptionFact>> ITransporterReportingProvider.ExceptionsAsync(ReportingWindow w, CancellationToken c) => Done(Exceptions.Where(e => e.Module == "Transporter").ToList());
    public Task<IReadOnlyList<DeliveryFact>> DeliveriesAsync(ReportingWindow w, CancellationToken c = default) => Done(Deliveries);
    public Task<IReadOnlyList<PodFact>> PodsAsync(ReportingWindow w, CancellationToken c = default) => Done(Pods);
    public Task<IReadOnlyList<DiscrepancyFact>> DiscrepanciesAsync(ReportingWindow w, CancellationToken c = default) => Done(Discrepancies);
    Task<IReadOnlyList<ExceptionFact>> IPodReportingProvider.ExceptionsAsync(ReportingWindow w, CancellationToken c) => Done(Exceptions.Where(e => e.Module == "Delivery").ToList());
    public Task<IReadOnlyList<TrackFact>> TripsAsync(ReportingWindow w, CancellationToken c = default) => Done(Trips);
    public Task<IReadOnlyList<DeviationFact>> DeviationsAsync(ReportingWindow w, CancellationToken c = default) => Done(Deviations);
    public Task<IReadOnlyList<DwellFact>> DwellsAsync(ReportingWindow w, CancellationToken c = default) => Done(Dwells);
    public Task<IReadOnlyList<GapFact>> GapsAsync(ReportingWindow w, CancellationToken c = default) => Done(Gaps);
    Task<IReadOnlyList<ExceptionFact>> ITrackingReportingProvider.ExceptionsAsync(ReportingWindow w, CancellationToken c) => Done(Exceptions.Where(e => e.Module == "Tracking").ToList());
    public Task<IReadOnlyList<ContractFact>> ContractsAsync(ReportingWindow w, CancellationToken c = default) => Done(Contracts);
    public Task<IReadOnlyList<RateFact>> RatesAsync(ReportingWindow w, CancellationToken c = default) => Done(Rates);
    public Task<IReadOnlyList<DphFact>> DphAsync(ReportingWindow w, CancellationToken c = default) => Done(Dph);
    public Task<IReadOnlyList<RatingFact>> RatingsAsync(ReportingWindow w, CancellationToken c = default) => Done(Ratings);
    public Task<IReadOnlyList<CoverageFact>> CoverageAsync(ReportingWindow w, CancellationToken c = default) => Done(CoverageRows);
}
