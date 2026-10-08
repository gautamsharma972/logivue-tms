using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Reports.Domain;

/// <summary>The five modules' reporting providers, as the engine sees them.</summary>
public sealed record ReportingProviders(
    IPlanningReportingProvider Planning,
    ITransporterReportingProvider Transporters,
    IPodReportingProvider Pod,
    ITrackingReportingProvider Tracking,
    IFreightContractReportingProvider Freight);

/// <summary>
/// The facts one report run (or one KPI) works from, for one period. Each kind is fetched once on first use, then narrowed by the request's
/// filters and by what the caller is allowed to see. Because the limits are applied here, every report and KPI is held to them without each repeating the rule.
/// </summary>
public sealed class ReportFacts
{
    private readonly ReportingProviders _providers;
    private readonly FactStore _store;

    /// <param name="span">Every date any period of this run (the period, its comparisons, trend history) will look at. Providers are asked once for the whole span and each period reads its own part.</param>
    public ReportFacts(ReportingProviders providers, DateRange range, ReportFilters filters, ReportPrincipal principal, ReportSettings settings, DateTimeOffset now, DateRange? span = null, FactStore? store = null)
    {
        _providers = providers;
        Range = range;
        Filters = filters;
        Principal = principal;
        Settings = settings;
        Now = now;
        Today = DateOnly.FromDateTime(now.UtcDateTime.AddMinutes(330));
        Span = span ?? range;
        _store = store ?? new FactStore();
    }

    public DateRange Span { get; }

    public DateRange Range { get; }

    public ReportFilters Filters { get; }

    public ReportPrincipal Principal { get; }

    public ReportSettings Settings { get; }

    /// <summary>When the run started (fixed for the whole run, so every figure shares one "now").</summary>
    public DateTimeOffset Now { get; }

    /// <summary>Today in India.</summary>
    public DateOnly Today { get; }

    public ReportingProviders Providers => _providers;

    /// <summary>The same filters and caller for another period (a comparison).</summary>
    public ReportFacts ForPeriod(DateRange range) => new(_providers, range, Filters, Principal, Settings, Now, new DateRange(Span.From < range.From ? Span.From : range.From, Span.To > range.To ? Span.To : range.To), _store);

    /// <summary>The same period and caller with one more filter (a per-carrier or per-lane figure).</summary>
    public ReportFacts With(string filter, string value) => new(_providers, Range, Filters.With(filter, value), Principal, Settings, Now, Span, _store);

    /// <summary>The same period without one of the filters (a company-wide figure to benchmark against).</summary>
    public ReportFacts Without(params string[] filters) => new(_providers, Range, filters.Aggregate(Filters, (f, name) => f.With(name, null)), Principal, Settings, Now, Span, _store);

    private ReportingWindow Window(bool wide)
    {
        // Only the caller's own company is pushed down to the providers. Filters stay in memory so variants of one run (a figure per carrier) can share the same rows.
        var transporter = Principal.TransporterId;
        return wide ? new ReportingWindow(Span.To.AddDays(-365), Span.To, transporter) : new ReportingWindow(Span.From, Span.To, transporter);
    }

    /// <summary>The caller's limits: a company user sees only their company; a user limited to customers, regions or business units sees only those, and nothing that does not carry the dimension.</summary>
    private bool Allowed(Dims dims)
    {
        if (Principal.TransporterId is { } own && dims.TransporterId != own)
        {
            return false;
        }

        foreach (var (dimension, allowed) in Principal.Scopes)
        {
            if (allowed.Count == 0)
            {
                continue;
            }

            if (!dims.Carries(dimension) || dims.Get(dimension) is not { } value || !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<IReadOnlyList<T>> Load<T>(string key, Func<ReportingWindow, Task<IReadOnlyList<T>>> fetch, Func<T, Dims> dims, bool anyDate)
    {
        // The raw rows are fetched once per run (and kept for every period of it); each period then narrows them itself.
        var raw = await _store.GetAsync(key, anyDate, () => fetch(Window(anyDate)));
        var from = anyDate ? DateOnly.MinValue : Range.From;
        var to = anyDate ? DateOnly.MaxValue : Range.To;
        return raw.Where(r =>
        {
            var d = dims(r);
            return Allowed(d) && Filters.Matches(d, from, to);
        }).ToList();
    }

    /// <summary>Shipments, with planned and actual pickup times filled in from Transporter Management where Planning does not hold them.</summary>
    public async Task<IReadOnlyList<ShipmentReportFact>> Shipments()
    {
        var shipments = await Load("ship", w => _providers.Planning.ShipmentsAsync(w), f => FactDims.Of(f, Settings), false);
        if (!shipments.Any(s => s.PlannedPickupAt is null && s.TransporterId is not null))
        {
            return shipments;
        }

        var executions = (await _store.GetAsync("exec", false, () => _providers.Transporters.ExecutionsAsync(Window(false)))).GroupBy(e => e.ShipmentRef, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return shipments.Select(s => s.PlannedPickupAt is null && executions.TryGetValue(s.ShipmentRef, out var e) ? s with { PlannedPickupAt = e.PlannedPickupAt, ActualPickupAt = e.ActualPickupAt ?? s.ActualPickupAt } : s).ToList();
    }

    public Task<IReadOnlyList<PlanningRunFact>> Runs() => Load("runs", w => _providers.Planning.RunsAsync(w), FactDims.Of, false);

    public Task<IReadOnlyList<PlanVehicleFact>> Vehicles() => Load("veh", w => _providers.Planning.VehiclesAsync(w), FactDims.Of, false);

    public Task<IReadOnlyList<UnplannedOrderFact>> Unplanned() => Load("unp", w => _providers.Planning.UnplannedAsync(w), f => FactDims.Of(f, Settings), false);

    public Task<IReadOnlyList<TenderFact>> Tenders() => Load("tend", w => _providers.Planning.TendersAsync(w), f => FactDims.Of(f, Settings), false);

    public Task<IReadOnlyList<TransporterFact>> Transporters() => Load("trn", w => _providers.Transporters.TransportersAsync(w), FactDims.Of, true);

    public Task<IReadOnlyList<ScorecardFact>> Scorecards() => Load("score", w => _providers.Transporters.ScorecardsAsync(w), FactDims.Of, false);

    public Task<IReadOnlyList<PlacementFact>> Placements() => Load("plc", w => _providers.Transporters.PlacementsAsync(w), FactDims.Of, false);

    public Task<IReadOnlyList<DeliveryFact>> Deliveries() => Load("del", w => _providers.Pod.DeliveriesAsync(w), f => FactDims.Of(f, Settings), false);

    public Task<IReadOnlyList<PodFact>> Pods(bool anyDate = false) => Load("pod", w => _providers.Pod.PodsAsync(w), FactDims.Of, anyDate);

    public Task<IReadOnlyList<DiscrepancyFact>> Discrepancies() => Load("dsc", w => _providers.Pod.DiscrepanciesAsync(w), FactDims.Of, false);

    public Task<IReadOnlyList<TrackFact>> Trips(bool anyDate = false) => Load("trk", w => _providers.Tracking.TripsAsync(w), f => FactDims.Of(f, Settings), anyDate);

    public Task<IReadOnlyList<DeviationFact>> Deviations() => Load("dev", w => _providers.Tracking.DeviationsAsync(w), FactDims.Of, false);

    public Task<IReadOnlyList<DwellFact>> Dwells() => Load("dwl", w => _providers.Tracking.DwellsAsync(w), FactDims.Of, false);

    public Task<IReadOnlyList<GapFact>> Gaps() => Load("gap", w => _providers.Tracking.GapsAsync(w), FactDims.Of, false);

    public Task<IReadOnlyList<ContractFact>> Contracts() => Load("ctr", w => _providers.Freight.ContractsAsync(w), FactDims.Of, true);

    public Task<IReadOnlyList<RateFact>> Rates() => Load("rate", w => _providers.Freight.RatesAsync(w), FactDims.Of, true);

    public Task<IReadOnlyList<DphFact>> Dph() => Load("dph", w => _providers.Freight.DphAsync(w), FactDims.Of, true);

    public Task<IReadOnlyList<RatingFact>> Ratings() => Load("rtg", w => _providers.Freight.RatingsAsync(w), f => FactDims.Of(f, Settings), false);

    /// <summary>
    /// How lanes with real loads are covered. The Contracts module reports lanes it has rated; lanes that carried loads but were never rated (the ones that matter most)
    /// are added here from the shipments, judged against the rates it holds: an exact city lane, a state or zone rate, a rate for anywhere, or none.
    /// </summary>
    public async Task<IReadOnlyList<CoverageFact>> Coverage()
    {
        if (Principal.IsExternal)
        {
            return []; // coverage is the organisation's commercial position, never a company's own
        }

        var provided = await Load("cov", w => _providers.Freight.CoverageAsync(w), f => FactDims.Of(f, Settings), true);
        var rates = await Rates();
        var known = provided.Select(c => (c.Lane, c.Service)).ToHashSet();
        var derived = new List<CoverageFact>();
        foreach (var g in (await Shipments()).Where(s => !s.IsCancelled).GroupBy(s => (s.Lane, s.Service)).Where(g => !known.Contains(g.Key)))
        {
            var s = g.First();
            var matching = rates.Where(r => r.Service.Equals(g.Key.Service, StringComparison.OrdinalIgnoreCase) || g.Key.Service == "Dedicated").ToList();
            bool From(RateFact r, string? any) => r.Origin.Equals(s.OriginCity, StringComparison.OrdinalIgnoreCase) || r.Origin.Equals(s.OriginState, StringComparison.OrdinalIgnoreCase) || r.Origin == any;
            bool To(RateFact r, string? any) => r.Destination.Equals(s.DestinationCity, StringComparison.OrdinalIgnoreCase) || r.Destination.Equals(s.DestinationState, StringComparison.OrdinalIgnoreCase) || r.Destination == any;
            var lane = matching.Count(r => r.Origin.Equals(s.OriginCity, StringComparison.OrdinalIgnoreCase) && r.Destination.Equals(s.DestinationCity, StringComparison.OrdinalIgnoreCase));
            var zone = matching.Count(r => r.Zone is not null || (From(r, null) && To(r, null) && !(r.Origin.Equals(s.OriginCity, StringComparison.OrdinalIgnoreCase) && r.Destination.Equals(s.DestinationCity, StringComparison.OrdinalIgnoreCase))));
            var anywhere = matching.Count(r => r.Origin == "Anywhere" || r.Destination == "Anywhere");
            var cover = lane > 0 ? "Lane" : zone > 0 ? "Zone" : anywhere > 0 ? "Fallback" : "None";
            var loads = g.Count();
            derived.Add(new CoverageFact(g.Key.Lane, s.OriginCity, s.DestinationCity, g.Key.Service, loads, cover, lane, zone, cover == "None" ? loads : 0));
        }

        return provided.Concat(derived).ToList();
    }

    /// <summary>Exceptions of every module together. Open ones are what matters, whenever they were raised, so by default no date limit applies.</summary>
    public async Task<IReadOnlyList<ExceptionFact>> Exceptions(bool anyDate = true)
    {
        // One after another: the providers may share a database connection per module, which cannot serve two queries at once.
        var parts = new List<IReadOnlyList<ExceptionFact>>
        {
            await Load("exc-t", w => _providers.Transporters.ExceptionsAsync(w), FactDims.Of, anyDate),
            await Load("exc-d", w => _providers.Pod.ExceptionsAsync(w), FactDims.Of, anyDate),
            await Load("exc-k", w => _providers.Tracking.ExceptionsAsync(w), FactDims.Of, anyDate),
        };
        return parts.SelectMany(p => p).OrderByDescending(e => e.CreatedAt).ToList();
    }
}

/// <summary>Rows fetched from the providers during one run, shared by every period of it. Concurrent asks for the same rows share one fetch.</summary>
public sealed class FactStore
{
    private readonly Dictionary<string, Lazy<Task<object>>> _rows = [];
    private readonly object _gate = new();

    public async Task<IReadOnlyList<T>> GetAsync<T>(string key, bool anyDate, Func<Task<IReadOnlyList<T>>> fetch)
    {
        Lazy<Task<object>> entry;
        lock (_gate)
        {
            var cacheKey = $"{key}|{anyDate}";
            if (!_rows.TryGetValue(cacheKey, out entry!))
            {
                entry = new Lazy<Task<object>>(async () => await fetch());
                _rows[cacheKey] = entry;
            }
        }

        return (IReadOnlyList<T>)await entry.Value;
    }
}
