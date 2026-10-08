using Tms.Modules.Reports.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Reports;

/// <summary>Short builders for facts, with sensible defaults, so each test states only what it is about.</summary>
internal static class F
{
    public static readonly DateOnly Day = new(2026, 10, 1);
    public static readonly DateTimeOffset Noon = new(2026, 10, 1, 6, 30, 0, TimeSpan.Zero);
    public static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    public static ShipmentReportFact Ship(string reference = "SH-1", string service = "FTL", decimal weight = 10_000m, decimal? volume = 30m, decimal? km = 500m, decimal? freight = 50_000m, Guid? carrier = null, string? carrierName = "Alpha",
        string customer = "Acme", string origin = "Mumbai", string destination = "Pune", string status = "Delivered", int? payload = 16_000, DateTimeOffset? plannedPickup = null, DateTimeOffset? actualPickup = null, DateOnly? day = null) => new(
        reference, Guid.NewGuid(), status, service, day ?? Day, customer, origin, "Maharashtra", destination, "Maharashtra", "West", weight, volume, km, 1, 2, carrier ?? A, carrierName, "MH12AB1234", "32 FT", payload, 62m, "Ravi",
        freight, freight, "CN-1", plannedPickup, actualPickup, Day.AddDays(2), Noon.AddDays(2), Noon.AddDays(-2), Noon.AddDays(-1), Noon, 1);

    public static DeliveryFact Delivery(string reference = "DLV-1", bool? onTime = true, string status = "Delivered", Guid? carrier = null, string name = "Alpha", string customer = "Acme", string? shipment = "SH-1", string? responsibility = null) => new(
        reference, shipment, carrier ?? A, name, customer, "Mumbai", "Pune", Noon, Noon.AddHours(onTime == false ? 5 : -1), status, onTime, 1, true, null, null, null, Day, responsibility);

    public static PodFact Pod(string reference = "POD-1", bool required = true, bool? withinSla = true, string status = "Accepted", Guid? carrier = null, string name = "Alpha", DateTimeOffset? submitted = null, DateTimeOffset? completed = null) => new(
        reference, "DLV-1", "SH-1", carrier ?? A, name, "Acme", required, completed ?? Noon, submitted, null, status, null, null, null, null, withinSla, Day);

    public static PlacementFact Placement(string outcome = "OnTime", Guid? carrier = null, string name = "Alpha") => new("PLC-1", "SH-1", carrier ?? A, name, "Mumbai → Pune", "32 FT", "FTL", Noon.AddDays(-2), Noon.AddDays(-2), Noon, Noon, Noon, outcome, outcome == "OnTime" ? 0 : 30);

    public static TenderFact Tender(string outcome, Guid? carrier = null, string name = "Alpha") => new("SH-1", carrier ?? A, name, "Mumbai → Pune", "32 FT", "FTL", Noon, Noon, Noon, outcome);

    public static PlanVehicleFact Plan(decimal kg = 8_000m, int capacityKg = 16_000, decimal cbm = 20m, decimal capacityCbm = 60m, bool consolidated = false, decimal cost = 40_000m, decimal? separate = null, Guid? carrier = null) => new(
        "PLN-1", Day, "TRIP-1", "SH-1", "MH12AB1234", "32 FT", carrier ?? A, "Alpha", "FTL", "Mumbai → Pune", "West", 2, 3, kg, cbm, Math.Round(kg / capacityKg * 100m, 1), Math.Round(cbm / capacityCbm * 100m, 1), cost, 500m, 450m, 50m,
        consolidated, separate, 20m, 1, capacityKg, capacityCbm);

    public static TrackFact Trip(string health = "Healthy", string execution = "InTransit", string risk = "OnTime", DateTimeOffset? plannedEta = null, DateTimeOffset? latestEta = null, DateTimeOffset? actual = null, Guid? carrier = null, string name = "Alpha") => new(
        "TRP-1", "SH-1", "MH12AB1234", carrier ?? A, name, "Mumbai → Pune", health, execution, risk, plannedEta, latestEta, actual, 500m, 510m, 600, 620, 3, 3, 0, 60, 0m, null, null, Noon, 0, Day);

    public static ReportingProviders All(TestProvider p) => new(p, p, p, p, p);
}
