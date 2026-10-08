using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.India;

namespace Tms.Modules.Reports.Infrastructure.Providers;

/// <summary>Everything the demonstration dataset holds, as the same facts the modules' own providers would give.</summary>
internal sealed class DemoDataset
{
    public List<ShipmentReportFact> Shipments { get; } = [];

    public List<PlanningRunFact> Runs { get; } = [];

    public List<PlanVehicleFact> Vehicles { get; } = [];

    public List<UnplannedOrderFact> Unplanned { get; } = [];

    public List<TenderFact> Tenders { get; } = [];

    public List<TransporterFact> Transporters { get; } = [];

    public List<ScorecardFact> Scorecards { get; } = [];

    public List<PlacementFact> Placements { get; } = [];

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

    public List<CoverageFact> Coverage { get; } = [];

    public List<ExceptionFact> TransporterExceptions { get; } = [];

    public List<ExceptionFact> DeliveryExceptions { get; } = [];

    public List<ExceptionFact> TrackingExceptions { get; } = [];

    /// <summary>Location events the demonstration "recorded" (the tracking pipeline's raw points), counted for the dataset summary.</summary>
    public int LocationEvents { get; set; }
}

/// <summary>
/// Builds a believable operation: carriers of different quality and price, lanes, customers, shipments in every state, deliveries and proofs, tracking,
/// plans, contracts, rates and ratings, all linked by reference. Deterministic for a given day, so numbers are stable between runs. Carriers deliberately differ
/// (cheap and poor, dear and good) so the dashboards are not flat.
/// </summary>
internal static class DemoDataGenerator
{
    private sealed record Carrier(Guid Id, string Code, string Name, string Region, decimal Reliability, decimal Price, decimal Podq, decimal Trackq);

    private sealed record Lane(string FromCity, string FromState, string ToCity, string ToState, int Km, double Lat1, double Lon1, double Lat2, double Lon2);

    private sealed record VType(string Name, int PayloadKg, decimal Cbm, decimal PerKm);

    private static readonly string[] Customers =
    [
        "Reliance Retail", "Tata Consumer Products", "Hindustan Unilever", "Asian Paints", "Godrej Appliances", "Dabur India", "ITC Foods", "Havells India", "Pidilite Industries", "Marico Limited", "Berger Paints", "Voltas Limited",
    ];

    private static readonly string[] Drivers = ["Ramesh Yadav", "Sunil Kumar", "Anil Shinde", "Mohd Salim", "Gurpreet Singh", "Vijay Nair", "Prakash Rao"];

    private static readonly string[] TransporterIssues = ["Vehicle document expiring", "Driver licence expired", "Repeated late placement", "Insurance lapsed", "GPS device not reporting"];

    private static readonly string[] Skus = ["SKU-1001 Detergent 1kg", "SKU-1020 Edible oil 5L", "SKU-2004 Paint 20L", "SKU-2310 LED bulb pack", "SKU-3300 Biscuits carton", "SKU-4120 Cooler 40L", "SKU-5005 Cable drum", "SKU-6150 Soap carton"];

    private static readonly string[] ShortageReasons = ["Short loaded at origin", "Pilferage in transit", "Count mismatch at unloading", "Consignment split"];

    private static readonly string[] DamageTypes = ["Crushed carton", "Water damage", "Leakage", "Broken pack", "Dented unit"];

    private static readonly string[] RejectionReasons = ["Signature missing", "Receiver stamp missing", "Quantities do not match", "Photo unreadable", "Wrong document uploaded"];

    private static readonly string[] FailureReasons = ["Customer premises closed", "Address not found", "Customer asked to reschedule", "Goods refused: wrong item", "Goods refused: damaged", "No one to receive"];

    private static readonly string[] DelayReasons = ["Traffic congestion", "Vehicle breakdown", "Customer delayed unloading", "Weather", "Driver rest", "Toll queue", "Late loading at origin"];

    private static readonly VType[] Types =
    [
        new("Pickup 1T", 1000, 5m, 17m), new("17 FT (4T)", 4000, 18m, 30m), new("20 FT (7T)", 7000, 28m, 40m), new("24 FT (10T)", 10000, 38m, 52m), new("32 FT MXL (16T)", 16000, 62m, 74m), new("40 FT Trailer (25T)", 25000, 80m, 94m),
    ];

    private static readonly (int Back, decimal Price)[] DieselRevisions = [(110, 90.2m), (60, 93.8m), (15, 97.4m)];

    private static readonly VType[] RateTypes = [Types[2], Types[3], Types[4]];

    private static readonly Lane[] Lanes =
    [
        new("Mumbai", "Maharashtra", "Pune", "Maharashtra", 150, 19.07, 72.88, 18.52, 73.86), new("Mumbai", "Maharashtra", "Delhi", "Delhi", 1400, 19.07, 72.88, 28.61, 77.21),
        new("Delhi", "Delhi", "Jaipur", "Rajasthan", 280, 28.61, 77.21, 26.91, 75.79), new("Bengaluru", "Karnataka", "Chennai", "Tamil Nadu", 350, 12.97, 77.59, 13.08, 80.27),
        new("Chennai", "Tamil Nadu", "Hyderabad", "Telangana", 630, 13.08, 80.27, 17.39, 78.49), new("Ahmedabad", "Gujarat", "Mumbai", "Maharashtra", 530, 23.02, 72.57, 19.07, 72.88),
        new("Kolkata", "West Bengal", "Patna", "Bihar", 590, 22.57, 88.36, 25.59, 85.14), new("Pune", "Maharashtra", "Bengaluru", "Karnataka", 840, 18.52, 73.86, 12.97, 77.59),
        new("Delhi", "Delhi", "Lucknow", "Uttar Pradesh", 550, 28.61, 77.21, 26.85, 80.95), new("Hyderabad", "Telangana", "Bengaluru", "Karnataka", 570, 17.39, 78.49, 12.97, 77.59),
        new("Mumbai", "Maharashtra", "Ahmedabad", "Gujarat", 530, 19.07, 72.88, 23.02, 72.57), new("Delhi", "Delhi", "Chandigarh", "Chandigarh", 250, 28.61, 77.21, 30.73, 76.78),
        new("Kolkata", "West Bengal", "Bhubaneswar", "Odisha", 440, 22.57, 88.36, 20.30, 85.82), new("Nagpur", "Maharashtra", "Hyderabad", "Telangana", 500, 21.15, 79.09, 17.39, 78.49),
        new("Indore", "Madhya Pradesh", "Mumbai", "Maharashtra", 600, 22.72, 75.86, 19.07, 72.88), new("Surat", "Gujarat", "Mumbai", "Maharashtra", 280, 21.17, 72.83, 19.07, 72.88),
    ];

    public static DemoDataset Build(DateOnly today)
    {
        var rng = new Random(20261007);
        var data = new DemoDataset();
        var tz = TimeSpan.FromMinutes(330);
        DateTimeOffset At(DateOnly d, int hour, int minute = 0) => new(d.ToDateTime(new TimeOnly(hour, minute)), tz);
        double U(double a, double b) => a + (rng.NextDouble() * (b - a));
        decimal D(double a, double b) => (decimal)U(a, b);
        T Pick<T>(IReadOnlyList<T> list) => list[rng.Next(list.Count)];
        var now = At(today, 15, 0);

        // ---- carriers, vehicles
        (string Name, string Region, double Rel, double Price, double Pod, double Track)[] profiles =
        [
            ("Shree Roadlines", "West", .94, 1.12, .95, .92), ("Bharat Freight Carriers", "North", .90, 1.05, .90, .88), ("Gati Express Logistics", "North", .88, 1.18, .93, .90), ("Safexpress Carriers", "North", .96, 1.28, .97, .96),
            ("VRL Transport", "South", .91, 1.00, .88, .85), ("Patel Roadways", "West", .84, .92, .80, .78), ("TCI Freight", "South", .93, 1.15, .94, .93), ("Om Logistics", "North", .78, .88, .70, .66),
            ("Sai Cargo Movers", "South", .82, .90, .75, .70), ("Rajdhani Transport", "North", .72, .84, .62, .60), ("Eastern Carriers", "East", .80, .95, .76, .72), ("Kolkata Haulers", "East", .76, .89, .70, .65),
            ("Deccan Transport Co", "South", .89, 1.02, .86, .84), ("Western Cargo Lines", "West", .86, .97, .83, .80), ("Maharashtra Express Cargo", "West", .92, 1.08, .92, .90), ("Punjab Freight Services", "North", .87, 1.00, .84, .82),
            ("Gujarat Roadways", "West", .83, .93, .79, .75), ("Central India Carriers", "Central", .79, .91, .73, .70), ("Karnataka Transport", "South", .90, 1.04, .89, .87), ("Madras Movers", "South", .85, .99, .81, .79),
            ("Bengal Cargo Express", "East", .74, .86, .66, .62), ("Telangana Haulers", "South", .88, 1.01, .85, .83),
        ];
        var carriers = profiles.Select((p, i) => new Carrier(Guid.NewGuid(), $"TR-{i + 1:000}", p.Name, p.Region, (decimal)p.Rel, (decimal)p.Price, (decimal)p.Pod, (decimal)p.Track)).ToList();
        var vehicles = new Dictionary<Guid, List<(string Reg, VType Type)>>();
        var regPrefix = new Dictionary<string, string> { ["West"] = "MH12", ["North"] = "DL1C", ["South"] = "KA01", ["East"] = "WB02", ["Central"] = "MP09" };
        foreach (var c in carriers)
        {
            var count = 2 + rng.Next(3);
            vehicles[c.Id] = Enumerable.Range(0, count).Select(_ => ($"{regPrefix[c.Region]}{(char)('A' + rng.Next(26))}{(char)('A' + rng.Next(26))}{rng.Next(1000, 9999)}", Types[Math.Min(Types.Length - 1, 1 + rng.Next(5))])).ToList();
            data.Transporters.Add(new TransporterFact(c.Id, c.Code, c.Name, c.Region, true, count, count + 1));
        }

        // ---- shipments and everything that hangs off them
        var runs = new Dictionary<DateOnly, List<ShipmentReportFact>>();
        var tripNo = 0;
        var onTheRoad = 0;
        var deliveryNo = 0;
        var podNo = 0;
        var placementNo = 0;
        var exceptionNo = 0;
        var ratingMonthsBack = 4;
        const int shipmentCount = 570;
        for (var i = 0; i < shipmentCount; i++)
        {
            var offset = rng.Next(-119, 4);
            var pickup = today.AddDays(offset);
            var lane = Pick(Lanes);
            var region = IndiaRegions.OfState(lane.FromState);
            var eligible = carriers.Where(c => c.Region == region || rng.NextDouble() < .25).ToList();
            var carrier = eligible.Count == 0 ? Pick(carriers) : Pick(eligible);
            var roll = rng.NextDouble();
            var service = roll < .55 ? "FTL" : roll < .90 ? "PTL" : "Dedicated";
            VType type;
            decimal weight;
            if (service == "PTL")
            {
                weight = D(400, 4600);
                type = Types.First(t => t.PayloadKg >= (int)weight * 2);
            }
            else
            {
                type = Types[Math.Min(Types.Length - 1, 2 + rng.Next(4))];
                weight = Math.Round(type.PayloadKg * D(.50, 1.0), 0);
            }

            var volume = Math.Round(Math.Min(type.Cbm, weight / 1000m * D(2.2, 5.0)), 1);
            var km = (decimal)lane.Km;
            var orders = service == "PTL" ? 2 + rng.Next(5) : service == "FTL" ? 1 + rng.Next(3) : 1 + rng.Next(2);
            var customer = Pick(Customers);
            var cancelled = rng.NextDouble() < .05;
            var transitDays = (int)Math.Ceiling(lane.Km / 450.0) + 1;
            var deliverBy = pickup.AddDays(transitDays);
            var plannedPickup = At(pickup, 9);
            var ref_ = $"SH-{20001 + i}";
            var shipmentId = Guid.NewGuid();

            string status;
            DateTimeOffset? tendered = null;
            DateTimeOffset? accepted = null;
            DateTimeOffset? dispatched = null;
            DateTimeOffset? actualPickup = null;
            DateTimeOffset? deliveredAt = null;
            Guid? trId = carrier.Id;
            string? trName = carrier.Name;
            if (cancelled)
            {
                status = "Cancelled";
                tendered = plannedPickup.AddDays(-2);
            }
            else if (offset > 0)
            {
                var s = rng.NextDouble();
                status = s < .25 ? "Draft" : s < .6 ? "Tendered" : "Accepted";
                if (status == "Draft")
                {
                    trId = null;
                    trName = null;
                }

                tendered = status == "Draft" ? null : now.AddHours(-rng.Next(2, 30));
                accepted = status == "Accepted" ? tendered?.AddHours(3) : null;
            }
            else
            {
                tendered = plannedPickup.AddDays(-2).AddHours(rng.Next(0, 6));
                accepted = tendered.Value.AddHours(rng.Next(1, 20));
                var lateProbability = (double)(1 - carrier.Reliability) * 1.1;
                var delayMin = rng.NextDouble() < lateProbability ? (int)(-Math.Log(1 - rng.NextDouble()) * 100 * (1.6 - (double)carrier.Reliability)) : -rng.Next(0, 25);
                actualPickup = plannedPickup.AddMinutes(delayMin);
                dispatched = actualPickup.Value.AddMinutes(rng.Next(10, 50));
                var deliveryDelayH = rng.NextDouble() < (1 - (double)carrier.Reliability) * 1.15 ? -Math.Log(1 - rng.NextDouble()) * 9 * (1.7 - (double)carrier.Reliability) : -U(0.5, 18);
                var arrival = At(deliverBy, 18).AddHours(deliveryDelayH);
                if (arrival <= now)
                {
                    deliveredAt = arrival;
                    status = "Delivered";
                }
                else
                {
                    status = "Dispatched";
                }
            }

            var typeBase = type.PerKm;
            var baseFreight = service == "PTL" ? Math.Round(weight * km * 0.0105m * carrier.Price * D(.94, 1.06), 0) : Math.Round(km * typeBase * carrier.Price * D(.94, 1.06) * (service == "Dedicated" ? 1.12m : 1m), 0);
            baseFreight = Math.Max(baseFreight, 1800m);
            var dphPct = Math.Round(1.2m + ((119 + offset) / 123m * 3.6m), 2);
            var dph = Math.Round(baseFreight * dphPct / 100m, 0);
            var accessorials = rng.NextDouble() < .35 ? Math.Round(baseFreight * D(.01, .045), 0) : 0m;
            var discount = rng.NextDouble() < .25 ? Math.Round(baseFreight * D(.005, .02), 0) : 0m;
            var finalFreight = baseFreight + dph + accessorials - discount;
            var plannedCost = Math.Round(finalFreight * D(.92, 1.08), 0);
            var priced = trId is not null && status != "Draft" && !cancelled;
            var vehicle = trId is not null && status is "Accepted" or "Dispatched" or "Delivered" ? Pick(vehicles[carrier.Id]) : default;
            var regVehicle = vehicle.Reg;
            var driverName = regVehicle is null ? null : Pick(Drivers);
            var consolidated = orders > 1 && service != "Dedicated";

            var fact = new ShipmentReportFact(
                ref_, shipmentId, status, service, pickup, customer, lane.FromCity, lane.FromState, lane.ToCity, lane.ToState, region, weight, volume, km, orders, orders + 1, trId, trName, regVehicle,
                regVehicle is null ? type.Name : vehicle.Type.Name, regVehicle is null ? type.PayloadKg : vehicle.Type.PayloadKg, regVehicle is null ? type.Cbm : vehicle.Type.Cbm, driverName, plannedCost, priced ? finalFreight : null,
                priced ? $"CN-{carriers.IndexOf(carrier) + 1:0000}" : null, plannedPickup, actualPickup, deliverBy, deliveredAt, tendered, accepted, dispatched, consolidated ? orders : 1, null, null, null);
            data.Shipments.Add(fact);
            if (offset <= 0 || status != "Draft")
            {
                if (!runs.TryGetValue(pickup.AddDays(-1), out var list))
                {
                    list = [];
                    runs[pickup.AddDays(-1)] = list;
                }

                list.Add(fact);
            }

            // ---- tenders
            if (tendered is { } t0 && !cancelled)
            {
                var first = rng.NextDouble() < .22 ? Pick(carriers.Where(c => c.Id != carrier.Id).ToList()) : null;
                var tenderStart = t0.AddMinutes(-rng.Next(30, 240));
                if (first is not null)
                {
                    var rejected = rng.NextDouble() < .55;
                    data.Tenders.Add(new TenderFact(ref_, first.Id, first.Name, fact.Lane, fact.VehicleType, service, tenderStart, rng.NextDouble() < .8 ? tenderStart.AddMinutes(rng.Next(5, 90)) : null, rejected ? tenderStart.AddHours(rng.Next(1, 5)) : null, rejected ? "Rejected" : "Expired"));
                }

                if (trId is not null)
                {
                    var outcome = status switch { "Tendered" => "Pending", _ => "Accepted" };
                    if (status != "Tendered" && rng.NextDouble() > (double)carrier.Reliability + .06)
                    {
                        outcome = rng.NextDouble() < .5 ? "Rejected" : "Expired";
                    }

                    data.Tenders.Add(new TenderFact(ref_, carrier.Id, carrier.Name, fact.Lane, fact.VehicleType, service, t0, rng.NextDouble() < .9 ? t0.AddMinutes(rng.Next(5, 60)) : null,
                        outcome is "Accepted" or "Rejected" ? t0.AddHours(rng.Next(1, 14)) : null, outcome));
                }
            }

            // ---- placements
            if (accepted is not null && !cancelled && offset <= 0)
            {
                var required = plannedPickup.AddHours(-1);
                var late = rng.NextDouble() > (double)carrier.Reliability + .02;
                var noShow = late && rng.NextDouble() < .12;
                var replaced = !late && rng.NextDouble() < .06;
                var delay = late ? rng.Next(25, 240) : 0;
                var requested = tendered!.Value.AddHours(2);
                data.Placements.Add(new PlacementFact($"PLC-{++placementNo:00000}", ref_, carrier.Id, carrier.Name, fact.Lane, fact.VehicleType, service, requested, accepted.Value, noShow ? null : required.AddMinutes(delay - 10), noShow ? null : required.AddMinutes(delay),
                    required, noShow ? "NoShow" : replaced ? "Replaced" : late ? "Late" : "OnTime", noShow ? null : delay));
            }

            // ---- tracking, deliveries, proofs
            if (status is "Dispatched" or "Delivered")
            {
                var tripRef = $"TRP-{++tripNo:00000}";
                var inTransit = status == "Dispatched";
                // A spread of every tracking condition among the trips still on the road, so the control tower always has something to show.
                var health = !inTransit ? "Completed" : ++onTheRoad % 7 == 0 ? "Lost" : onTheRoad % 7 == 1 ? "Stale" : onTheRoad % 13 == 2 ? "NotStarted" : "Healthy";
                var plannedEta = At(deliverBy, 18);
                var drift = inTransit ? (int)U(-90, 480) : 0;
                var latestEta = inTransit ? plannedEta.AddMinutes(drift) : deliveredAt?.AddMinutes(rng.Next(-40, 40));
                var risk = inTransit ? (drift <= 30 ? "OnTime" : drift <= 120 ? "AtRisk" : drift <= 360 ? "Delayed" : "SeverelyDelayed") : deliveredAt > plannedEta.AddMinutes(30) ? "Delayed" : "OnTime";
                var deviates = rng.NextDouble() < (1 - (double)carrier.Trackq) * .8 + .04;
                var deviationKm = deviates ? Math.Round(D(2.5, 38), 1) : 0m;
                var plannedDur = (int)(lane.Km / 40.0 * 60);
                var actualKm = health == "NotStarted" ? (decimal?)null : Math.Round(km * D(1.0, 1.04) + deviationKm, 1);
                var progress = inTransit ? Math.Clamp((now - dispatched!.Value).TotalMinutes / plannedDur, .05, .95) : 1.0;
                var lat = lane.Lat1 + ((lane.Lat2 - lane.Lat1) * progress) + U(-.05, .05);
                var lon = lane.Lon1 + ((lane.Lon2 - lane.Lon1) * progress) + U(-.05, .05);
                var unplanned = rng.NextDouble() < .22 ? 1 : 0;
                var dwellMin = (int)U(45, 140) + (unplanned * (int)U(40, 120));
                IReadOnlyList<TrackPoint>? plannedRoute = null;
                IReadOnlyList<TrackPoint>? actualRoute = null;
                if (tripNo % 4 == 0)
                {
                    plannedRoute = Enumerable.Range(0, 7).Select(k => new TrackPoint(lane.Lat1 + ((lane.Lat2 - lane.Lat1) * k / 6.0), lane.Lon1 + ((lane.Lon2 - lane.Lon1) * k / 6.0))).ToList();
                    actualRoute = Enumerable.Range(0, 7).Select(k => new TrackPoint(lane.Lat1 + ((lane.Lat2 - lane.Lat1) * k / 6.0) + (k is 2 or 3 or 4 ? (deviates ? U(.15, .4) : U(-.01, .01)) : 0), lane.Lon1 + ((lane.Lon2 - lane.Lon1) * k / 6.0) + (k is 3 ? (deviates ? U(.2, .5) : 0) : 0))).ToList();
                    if (inTransit)
                    {
                        actualRoute = actualRoute.Take(Math.Max(2, (int)(7 * progress))).ToList();
                    }
                }

                data.Trips.Add(new TrackFact(tripRef, ref_, regVehicle, carrier.Id, carrier.Name, fact.Lane, health, inTransit ? "InTransit" : "Completed", risk, plannedEta, latestEta, deliveredAt, km, actualKm, plannedDur,
                    inTransit ? null : (int)(deliveredAt!.Value - actualPickup!.Value).TotalMinutes, orders + 1, inTransit ? null : orders + 1 + unplanned, unplanned, dwellMin, deviationKm, health is "Stale" or "Lost" or "Healthy" ? lat : null,
                    health is "Stale" or "Lost" or "Healthy" ? lon : null, inTransit ? (health == "Healthy" ? now.AddMinutes(-rng.Next(1, 8)) : health == "Stale" ? now.AddMinutes(-rng.Next(25, 80)) : health == "Lost" ? now.AddHours(-rng.Next(4, 14)) : null) : deliveredAt,
                    0, pickup, plannedRoute, actualRoute));
                data.LocationEvents += health == "NotStarted" ? 0 : (int)Math.Min(lane.Km / 3.0, 400);
                if (deviates)
                {
                    data.Deviations.Add(new DeviationFact(tripRef, ref_, regVehicle, carrier.Id, carrier.Name, fact.Lane, deviationKm, rng.Next(12, 140), (dispatched ?? now).AddMinutes(rng.Next(60, 600)), rng.NextDouble() < .5 ? "Road closure" : null, inTransit && rng.NextDouble() < .5 ? "Open" : "Closed"));
                }

                if (health == "Lost" || rng.NextDouble() < .1)
                {
                    var gapStart = (dispatched ?? now).AddMinutes(rng.Next(90, 700));
                    var dur = health == "Lost" ? rng.Next(240, 800) : rng.Next(30, 180);
                    data.Gaps.Add(new GapFact(tripRef, ref_, regVehicle, carrier.Id, carrier.Name, rng.NextDouble() < .5 ? lane.FromCity + " outskirts" : "NH highway stretch", gapStart, health == "Lost" && inTransit ? null : gapStart.AddMinutes(dur), dur,
                        dur > 240 ? "Critical" : dur > 90 ? "High" : "Warning", health == "Lost" && inTransit ? "Open" : "Closed"));
                }

                data.Dwells.Add(new DwellFact(tripRef, ref_, carrier.Id, "Origin", lane.FromCity + " loading", 60, (int)U(40, 150), (dispatched ?? now).AddMinutes(-60)));
                data.Dwells.Add(new DwellFact(tripRef, ref_, carrier.Id, "Customer", customer, 30, (int)U(20, 110), (deliveredAt ?? now).AddMinutes(-40)));
                if (rng.NextDouble() < .3)
                {
                    data.Dwells.Add(new DwellFact(tripRef, ref_, carrier.Id, "Hub", lane.ToCity + " hub", 45, (int)U(30, 130), (dispatched ?? now).AddHours(5)));
                }

                if (unplanned == 1)
                {
                    data.Dwells.Add(new DwellFact(tripRef, ref_, carrier.Id, "Unplanned", "Roadside", 0, (int)U(30, 120), (dispatched ?? now).AddHours(3)));
                }

                if (health == "Lost" && inTransit)
                {
                    data.TrackingExceptions.Add(new ExceptionFact("Tracking", $"TEX-{++exceptionNo:0000}", "TrackingLost", "Critical", ref_, carrier.Id, carrier.Name, fact.Lane, now.AddHours(-rng.Next(2, 12)), null, null, "Open"));
                }
                else if (risk is "Delayed" or "SeverelyDelayed" && inTransit)
                {
                    data.TrackingExceptions.Add(new ExceptionFact("Tracking", $"TEX-{++exceptionNo:0000}", "DeliveryDelayed", risk == "SeverelyDelayed" ? "Critical" : "High", ref_, carrier.Id, carrier.Name, fact.Lane, now.AddHours(-rng.Next(1, 8)), null,
                        rng.NextDouble() < .5 ? "Control tower" : null, rng.NextDouble() < .4 ? "InProgress" : "Open"));
                }
                else if (deviates && inTransit)
                {
                    data.TrackingExceptions.Add(new ExceptionFact("Tracking", $"TEX-{++exceptionNo:0000}", "RouteDeviation", "Warning", ref_, carrier.Id, carrier.Name, fact.Lane, now.AddHours(-rng.Next(1, 20)), null, null, "Open"));
                }

                // deliveries (one per order drop)
                var dropCount = service == "PTL" ? Math.Min(orders, 3) : 1;
                for (var d = 0; d < dropCount; d++)
                {
                    var promised = At(deliverBy, 18);
                    var noPlan = rng.NextDouble() < .04;
                    var dStatus = inTransit ? "InTransit" : rng.NextDouble() switch { < .035 => "Failed", < .055 => "Refused", < .11 => "PartiallyDelivered", _ => "Delivered" };
                    var completed = inTransit ? (DateTimeOffset?)null : deliveredAt!.Value.AddMinutes(d * 25);
                    bool? onTime = inTransit || noPlan || dStatus is "Failed" or "Refused" ? null : completed <= promised;
                    var dRef = $"DLV-{++deliveryNo:00000}";
                    var cust = d == 0 ? customer : Pick(Customers);
                    string? resp = onTime == false ? (rng.NextDouble() < .55 ? "Carrier" : rng.NextDouble() < .55 ? "NonCarrier" : null) : null;
                    data.Deliveries.Add(new DeliveryFact(dRef, ref_, carrier.Id, carrier.Name, cust, lane.FromCity, lane.ToCity, noPlan ? null : promised, completed, dStatus, onTime, dStatus is "Failed" ? 1 + rng.Next(3) : 1, rng.NextDouble() < .95,
                        dStatus is "Failed" or "Refused" ? Pick(FailureReasons) : null, dStatus is "Failed" or "Refused" ? lane.ToCity : null, dStatus is "Failed" ? "Reattempt next day" : dStatus == "Refused" ? "Return to origin" : null,
                        completed is { } cd ? Day(cd) : pickup, resp, onTime == false ? Pick(DelayReasons) : null));
                    if (dStatus is "Failed" or "Refused")
                    {
                        data.DeliveryExceptions.Add(new ExceptionFact("Delivery", $"DEX-{++exceptionNo:0000}", dStatus == "Failed" ? "FailedDelivery" : "CustomerRefused", "High", ref_, carrier.Id, carrier.Name, fact.Lane, completed ?? now, rng.NextDouble() < .5 ? completed?.AddHours(20) : null, "POD team", rng.NextDouble() < .5 ? "Resolved" : "Open"));
                    }

                    if (completed is { } cAt && dStatus is "Delivered" or "PartiallyDelivered")
                    {
                        var ordered = (decimal)rng.Next(40, 400);
                        if (dStatus == "PartiallyDelivered" || rng.NextDouble() < .025)
                        {
                            var shortQty = Math.Round(ordered * D(.02, .12), 0);
                            var sku = Pick(Skus);
                            data.Discrepancies.Add(new DiscrepancyFact(dRef, ref_, cust, carrier.Id, carrier.Name, sku, ordered, ordered, ordered - shortQty, shortQty, "Shortage", Pick(ShortageReasons), null, rng.NextDouble() < .7,
                                rng.NextDouble() < .55 ? $"CLM-{rng.Next(1000, 9999)}" : null, Math.Round(shortQty * D(180, 900), 0), Day(cAt)));
                        }

                        if (dStatus == "PartiallyDelivered" || rng.NextDouble() < .02)
                        {
                            var dmgQty = Math.Round(ordered * D(.01, .08), 0);
                            data.Discrepancies.Add(new DiscrepancyFact(dRef, ref_, cust, carrier.Id, carrier.Name, Pick(Skus), ordered, ordered, ordered, dmgQty, "Damage", null, Pick(DamageTypes), rng.NextDouble() < .8,
                                rng.NextDouble() < .5 ? $"CLM-{rng.Next(1000, 9999)}" : null, Math.Round(dmgQty * D(220, 1100), 0), Day(cAt)));
                        }

                        // proof of delivery
                        var required = rng.NextDouble() < .95;
                        var slaHours = 48;
                        var submitHours = (double)(1 - carrier.Podq) * 120 * rng.NextDouble() + U(2, 30);
                        var submittedAt = cAt.AddHours(submitHours);
                        var submitted = submittedAt <= now && rng.NextDouble() < (double)carrier.Podq + .08;
                        var rejected = submitted && rng.NextDouble() < (1 - (double)carrier.Podq) * .35 + .03;
                        var reviewDelay = rng.Next(3, 72);
                        var reviewed = submitted && submittedAt.AddHours(reviewDelay) <= now;
                        var resubmitted = rejected && rng.NextDouble() < .55 && submittedAt.AddHours(reviewDelay + 30) <= now;
                        var pStatus = !required ? "Pending" : !submitted ? "Pending" : rejected ? (resubmitted ? "Submitted" : rng.NextDouble() < .5 ? "Rejected" : "ResubmissionRequired") : reviewed ? "Accepted" : "Submitted";
                        bool? within = !required ? null : submitted ? submitHours <= slaHours : (now - cAt).TotalHours > slaHours ? false : null;
                        data.Pods.Add(new PodFact($"POD-{++podNo:00000}", dRef, ref_, carrier.Id, carrier.Name, cust, required, cAt, required && submitted ? submittedAt : null, reviewed ? submittedAt.AddHours(reviewDelay) : null, pStatus,
                            rejected ? submittedAt.AddHours(reviewDelay) : null, rejected ? "POD reviewer" : null, rejected ? Pick(RejectionReasons) : null, resubmitted ? submittedAt.AddHours(reviewDelay + 30) : null, within, Day(cAt)));
                    }
                }
            }

            // ---- ratings (kept for priced shipments)
            if (priced && accepted is not null && offset <= 0)
            {
                data.Ratings.Add(new RatingFact(ref_, carrier.Id, carrier.Name, $"CN-{carriers.IndexOf(carrier) + 1:0000}", 1 + (offset < -70 ? 0 : 1), $"R-{lane.FromCity[..3].ToUpperInvariant()}{lane.ToCity[..3].ToUpperInvariant()}-{type.Name[..2]}", 1 + (offset < -45 ? 0 : 1),
                    fact.Lane, weight, km, volume, service == "PTL" ? $"{(weight < 1000 ? "0-1000" : weight < 2500 ? "1000-2500" : "2500+")} kg" : $"{type.Name}, {lane.Km} km", baseFreight, dph, accessorials, discount, finalFreight, "1.0", accepted.Value.AddHours(1), service, type.Name));
            }
        }

        // ---- planning runs: shipments grouped by the day before pickup, plus the orders the planner could not place
        var runNo = 0;
        var reasonCategories = new[] { "NoVehicle", "PayloadExceeded", "VolumeExceeded", "SlaImpossible", "NoCompatibleVehicle", "NoRate", "NoRoute", "LockedConflict" };
        var reasonText = new Dictionary<string, (string Text, string Action)>
        {
            ["NoVehicle"] = ("No vehicle of the right type was free on that day", "Add a vehicle or move the pickup to the next day"),
            ["PayloadExceeded"] = ("Heavier than the largest available vehicle", "Split the order across two vehicles"),
            ["VolumeExceeded"] = ("Bulkier than the cubic capacity available", "Use a larger body or split the order"),
            ["SlaImpossible"] = ("The delivery window cannot be met from this origin", "Agree a later window with the customer"),
            ["NoCompatibleVehicle"] = ("Product cannot share a vehicle with the rest of the load", "Plan it on its own vehicle"),
            ["NoRate"] = ("No contract rate covers this lane", "Add a rate or ask for a spot quote"),
            ["NoRoute"] = ("No route could be found for the stops", "Check the address and pincode"),
            ["LockedConflict"] = ("A locked assignment blocks this order", "Unlock the trip or re-plan"),
        };
        foreach (var (day, shipments) in runs.OrderBy(r => r.Key))
        {
            for (var chunk = 0; chunk < shipments.Count; chunk += 4)
            {
                var group = shipments.Skip(chunk).Take(4).ToList();
                var runRef = $"PLN-{day:yyyyMMdd}-{++runNo:000}";
                var unplannedHere = rng.NextDouble() < .45 ? 1 + rng.Next(3) : 0;
                var cost = group.Sum(s => s.PlannedCost ?? 0m);
                var totalKm = group.Sum(s => s.DistanceKm ?? 0m);
                var vehicleRows = new List<PlanVehicleFact>();
                foreach (var s in group)
                {
                    var type = Types.First(t => s.VehicleType is not null && s.VehicleType.StartsWith(t.Name[..Math.Min(5, t.Name.Length)], StringComparison.Ordinal));
                    var capacityKg = s.VehiclePayloadKg ?? type.PayloadKg;
                    var capacityCbm = s.VehicleVolumeCbm ?? type.Cbm;
                    var wUtil = Math.Round(s.WeightKg / capacityKg * 100m, 1);
                    var vUtil = s.VolumeCbm is { } v ? Math.Round(v / capacityCbm * 100m, 1) : (decimal?)null;
                    var empty = Math.Round((s.DistanceKm ?? 0m) * D(.04, .30), 0);
                    var cons = s.ConsolidatedFrom > 1;
                    vehicleRows.Add(new PlanVehicleFact(runRef, day.AddDays(0), $"TRIP-{runRef[^3..]}-{vehicleRows.Count + 1}", s.ShipmentRef, s.VehicleRef, s.VehicleType ?? type.Name, s.TransporterId, s.TransporterName, s.Service, s.Lane, s.Region, s.Orders, s.Stops,
                        s.WeightKg, s.VolumeCbm ?? 0m, wUtil, vUtil, s.PlannedCost ?? 0m, s.DistanceKm ?? 0m, s.DistanceKm ?? 0m, empty, cons,
                        cons ? Math.Round((s.PlannedCost ?? 0m) * D(1.12, 1.45), 0) : null, cons ? Math.Round((s.DistanceKm ?? 0m) * D(.02, .09), 0) : null, cons ? rng.Next(1, 3) : null, capacityKg, capacityCbm));
                }

                data.Vehicles.AddRange(vehicleRows);
                var consolidatedRows = vehicleRows.Where(v => v.Consolidated && v.CostIfSeparate is not null).ToList();
                var before = vehicleRows.Sum(v => v.CostIfSeparate ?? v.EstimatedCost);
                var utilW = vehicleRows.Where(v => v.WeightUtilisationPct is not null).Select(v => v.WeightUtilisationPct!.Value).DefaultIfEmpty().Average();
                var utilV = vehicleRows.Where(v => v.VolumeUtilisationPct is not null).Select(v => v.VolumeUtilisationPct!.Value).DefaultIfEmpty().Average();
                data.Runs.Add(new PlanningRunFact(runRef, Guid.NewGuid(), day, 1, runNo % 11 == 0 ? "Draft" : "Committed", group.Sum(s => s.Orders) + unplannedHere, group.Sum(s => s.Orders), unplannedHere, group.Count, totalKm, cost,
                    Math.Round(utilW, 1), Math.Round(utilV, 1), before, vehicleRows.Sum(v => v.EstimatedCost), consolidatedRows.Sum(v => v.CostIfSeparate!.Value - v.EstimatedCost) + (rng.NextDouble() < .6 ? Math.Round(cost * D(.01, .04), 0) : 0m)));
                for (var u = 0; u < unplannedHere; u++)
                {
                    var l = Pick(Lanes);
                    var cat = Pick(reasonCategories);
                    data.Unplanned.Add(new UnplannedOrderFact(runRef, day, $"ORD-{rng.Next(10000, 99999)}", Pick(Customers), l.FromCity, l.ToCity, Math.Round(D(300, 9000), 0), Math.Round(D(1, 40), 1), cat, reasonText[cat].Text, reasonText[cat].Action));
                }
            }
        }

        // ---- transporter exceptions (paperwork, placement, performance)
        foreach (var c in carriers.OrderBy(_ => rng.Next()).Take(14))
        {
            var kind = Pick(TransporterIssues);
            data.TransporterExceptions.Add(new ExceptionFact("Transporter", $"TXN-{++exceptionNo:0000}", kind, kind.Contains("expired", StringComparison.Ordinal) || kind.Contains("lapsed", StringComparison.Ordinal) ? "Critical" : "Warning", null, c.Id, c.Name, null,
                now.AddDays(-rng.Next(0, 20)), rng.NextDouble() < .3 ? now.AddDays(-rng.Next(0, 5)) : null, "Transport manager", rng.NextDouble() < .3 ? "Resolved" : "Open"));
        }

        // ---- scorecards: per month and carrier, from the facts above (the same weights for everyone)
        for (var back = 0; back < ratingMonthsBack; back++)
        {
            var end = new DateOnly(today.Year, today.Month, 1).AddMonths(-back).AddMonths(1).AddDays(-1);
            if (end > today)
            {
                end = today;
            }

            var start = new DateOnly(end.Year, end.Month, 1);
            foreach (var c in carriers)
            {
                var ships = data.Shipments.Where(s => s.TransporterId == c.Id && !s.IsCancelled && s.PlannedPickupDate >= start && s.PlannedPickupDate <= end).ToList();
                if (ships.Count < 3)
                {
                    continue;
                }

                decimal? Pct(int n, int dn) => dn == 0 ? null : Math.Round((decimal)n / dn * 100m, 1);
                var pickup = ships.Where(s => s.ActualPickupAt is not null && s.PlannedPickupAt is not null).ToList();
                var otp = Pct(pickup.Count(s => s.ActualPickupAt <= s.PlannedPickupAt), pickup.Count);
                var dels = data.Deliveries.Where(d => d.TransporterId == c.Id && d.OnTime is not null && d.Date >= start && d.Date <= end).ToList();
                var otd = Pct(dels.Count(d => d.OnTime == true), dels.Count);
                var plc = data.Placements.Where(p => p.TransporterId == c.Id && DateOnly.FromDateTime(p.RequiredBy.UtcDateTime) >= start && DateOnly.FromDateTime(p.RequiredBy.UtcDateTime) <= end).ToList();
                var placement = Pct(plc.Count(p => p.Outcome == "OnTime"), plc.Count);
                var tnd = data.Tenders.Where(t => t.TransporterId == c.Id && t.Outcome is "Accepted" or "Rejected" or "Expired" && DateOnly.FromDateTime(t.OfferedAt.UtcDateTime) >= start && DateOnly.FromDateTime(t.OfferedAt.UtcDateTime) <= end).ToList();
                var tenderAcc = Pct(tnd.Count(t => t.Outcome == "Accepted"), tnd.Count);
                var pods = data.Pods.Where(p => p.TransporterId == c.Id && p.Required && p.SubmittedWithinSla is not null && p.Date >= start && p.Date <= end).ToList();
                var podC = Pct(pods.Count(p => p.SubmittedWithinSla == true), pods.Count);
                var claims = data.Discrepancies.Where(d => d.TransporterId == c.Id && d.ClaimRef is not null && d.Date >= start && d.Date <= end).Select(d => d.DeliveryRef).Distinct().Count();
                var claimsRate = Pct(claims, dels.Count);
                var parts = new List<(decimal Weight, decimal Value)>();
                void Part(decimal w, decimal? v)
                {
                    if (v is not null)
                    {
                        parts.Add((w, v.Value));
                    }
                }

                Part(25, otd);
                Part(20, otp);
                Part(15, placement);
                Part(15, tenderAcc);
                Part(15, podC);
                Part(10, claimsRate is null ? null : Math.Max(0, 100 - (claimsRate.Value * 5)));
                var overall = parts.Count == 0 ? (decimal?)null : Math.Round(parts.Sum(p => p.Weight * p.Value) / parts.Sum(p => p.Weight), 1);
                var costPerf = Math.Round(Math.Clamp(110 - ((c.Price - 0.84m) * 100m), 40, 100), 1);
                data.Scorecards.Add(new ScorecardFact(c.Id, c.Name, start, end, null, overall, otp, otd, placement, tenderAcc, podC, claimsRate, costPerf, Math.Round(c.Reliability * 100m, 1), "1.0"));
            }
        }

        // ---- contracts, rates, diesel clauses, coverage
        var contractNo = 0;
        foreach (var c in carriers)
        {
            contractNo++;
            var end = contractNo switch { 3 => today.AddDays(9), 7 => today.AddDays(26), 11 => today.AddDays(48), 15 => today.AddDays(70), 19 => today.AddDays(-12), _ => today.AddDays(120 + (contractNo * 11)) };
            var status = contractNo == 19 ? "Expired" : contractNo == 21 ? "Pending approval" : contractNo == 22 ? "Draft" : "Active";
            var ct = contractNo % 5 == 0 ? "Dedicated" : contractNo % 2 == 0 ? "PTL" : "FTL";
            var cref = $"CN-{contractNo:0000}";
            var lanesOfCarrier = Lanes.Where(l => IndiaRegions.OfState(l.FromState) == c.Region || contractNo % 3 == 0).Take(6).ToList();
            var rateCount = 0;
            foreach (var l in lanesOfCarrier.Where(l => l.FromCity != "Nagpur"))
            {
                foreach (var vt in RateTypes)
                {
                    var perTrip = Math.Round(l.Km * vt.PerKm * c.Price, 0);
                    data.Rates.Add(new RateFact(cref, 2, c.Id, c.Name, $"R-{l.FromCity[..3].ToUpperInvariant()}{l.ToCity[..3].ToUpperInvariant()}-{vt.Name[..2]}", 1 + (rateCount % 2), l.FromCity, l.ToCity, null, vt.Name, ct == "PTL" ? "PTL" : "FTL",
                        null, $"{l.Km - 50}-{l.Km + 100} km", null, perTrip, "PerTrip", Math.Round(perTrip * .6m, 0), Math.Round(perTrip * 1.4m, 0), end.AddYears(-1), end));
                    rateCount++;
                }

                if (ct == "PTL" || contractNo % 4 == 0)
                {
                    data.Rates.Add(new RateFact(cref, 2, c.Id, c.Name, $"R-{l.FromCity[..3].ToUpperInvariant()}{l.ToCity[..3].ToUpperInvariant()}-W", 1, l.FromCity, l.ToCity, null, null, "PTL", "0-1000 kg", null, null, Math.Round(l.Km * 0.0105m * c.Price * 100, 2), "PerQuintal",
                        1200m, null, end.AddYears(-1), end));
                    rateCount++;
                }
            }

            data.Rates.Add(new RateFact(cref, 2, c.Id, c.Name, $"Z-{c.Region[..2].ToUpperInvariant()}", 1, c.Region + " zone", c.Region + " zone", c.Region + " zone", null, "FTL", null, "0-500 km", null, Math.Round(500 * 48m * c.Price, 0), "PerTrip", null, null, end.AddYears(-1), end));
            rateCount++;
            data.Contracts.Add(new ContractFact(cref, Guid.NewGuid(), 2, c.Id, c.Name, ct, end.AddYears(-1), end, status, status == "Expired" ? "Lapsed" : end <= today.AddDays(60) ? "Due" : "None", rateCount));
            if (contractNo % 4 == 1)
            {
                data.Contracts.Add(new ContractFact(cref, Guid.NewGuid(), 1, c.Id, c.Name, ct, end.AddYears(-2), end.AddYears(-1).AddDays(-1), "Superseded", "Renewed", rateCount - 1));
            }

            // diesel clause with more than one revision
            var baseDiesel = 88.5m;
            foreach (var (back, price) in DieselRevisions)
            {
                if (contractNo % 3 != 0 && back == 110)
                {
                    continue;
                }

                var variation = Math.Round((price - baseDiesel) / baseDiesel * 100m, 2);
                var fuelShare = 30m + (contractNo % 4 * 2m);
                data.Dph.Add(new DphFact(cref, c.Id, c.Name, baseDiesel, price, variation, fuelShare, Math.Round(variation * fuelShare / 100m, 2), today.AddDays(-back), contractNo % 2 == 0 ? "Toll, Detention, Unloading" : "Toll, Detention"));
            }
        }

        foreach (var g in data.Shipments.Where(s => !s.IsCancelled).GroupBy(s => (s.Lane, s.Service)))
        {
            var first = g.First();
            var rates = data.Rates.Where(r => r.Origin == first.OriginCity && r.Destination == first.DestinationCity).ToList();
            var zoneRates = data.Rates.Count(r => r.Zone is not null && r.Origin == first.Region + " zone");
            var sameRegion = IndiaRegions.OfState(first.OriginState) == IndiaRegions.OfState(first.DestinationState);
            var cover = rates.Count > 0 ? "Lane" : zoneRates > 0 && sameRegion ? "Zone" : first.OriginCity == "Nagpur" ? "None" : "Fallback";
            var loads = g.Count();
            data.Coverage.Add(new CoverageFact(g.Key.Lane, first.OriginCity, first.DestinationCity, g.Key.Service, loads, cover, rates.Count, cover == "Zone" ? zoneRates : 0, cover == "None" ? loads : 0));
        }

        return data;
    }

    private static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime.AddMinutes(330));
}
