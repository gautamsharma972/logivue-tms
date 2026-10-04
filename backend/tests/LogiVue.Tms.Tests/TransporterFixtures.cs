using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.Tests;

/// <summary>A lane unique to one test, so transporters seeded by other tests never match it.</summary>
public sealed record FixtureLane(long Origin, long Destination, long VehicleType)
{
    public static FixtureLane Unique() => new(
        Random.Shared.NextInt64(100_000, 900_000),
        Random.Shared.NextInt64(900_001, 999_999),
        Random.Shared.NextInt64(10_000, 99_999));
}

public sealed record FixtureProfile(
    string Name,
    decimal Rate,
    bool Lane = true,
    TransporterStatus Status = TransporterStatus.Active,
    decimal Capacity = 12000m,
    decimal? OtdPct = 96.4m,
    decimal? OtdSample = 120m);

/// <summary>A seeded transporter and its fleet vehicle, for tests that need to act on the vehicle.</summary>
public sealed record SeededTransporter(long Id, string VehicleRegistration);

/// <summary>Seeds transporters that are active, compliant and serve a lane, directly in the test database.</summary>
public static class TransporterFixtures
{
    public static async Task<long> SeedAsync(TestHost host, FixtureLane lane, FixtureProfile profile) =>
        (await SeedDetailedAsync(host, lane, profile)).Id;

    public static async Task<SeededTransporter> SeedDetailedAsync(TestHost host, FixtureLane lane, FixtureProfile profile)
    {
        var registration = $"MH{Random.Shared.Next(10, 99)}{Guid.NewGuid():N}"[..10].ToUpperInvariant();

        return await host.WithTransporterDbAsync(async db =>
        {
            var transporter = new Transporter
            {
                TransporterCode = $"E{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                LegalName = profile.Name,
                Status = profile.Status,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, CreatedBy = "seed", UpdatedBy = "seed"
            };
            db.Transporters.Add(transporter);
            await db.SaveChangesAsync();

            foreach (var typeId in new long[] { 1, 2, 3 })
            {
                db.TransporterDocuments.Add(new TransporterDocument
                {
                    TransporterId = transporter.Id, DocumentTypeId = typeId,
                    VerificationStatus = DocumentVerificationStatus.Verified, VerifiedBy = "seed", VerifiedAt = DateTime.UtcNow
                });
            }

            if (profile.Lane)
            {
                db.TransporterLanes.Add(new TransporterLane
                {
                    TransporterId = transporter.Id, OriginLocationReference = lane.Origin, DestinationLocationReference = lane.Destination,
                    ServiceType = "FTL", VehicleTypeReference = lane.VehicleType, TransitSlaMinutes = 1440,
                    EffectiveFrom = new DateTime(2026, 1, 1), Status = RecordStatus.Active
                });
            }

            db.TransporterRates.Add(new TransporterRate
            {
                TransporterId = transporter.Id, OriginLocationReference = lane.Origin, DestinationLocationReference = lane.Destination,
                VehicleTypeReference = lane.VehicleType, ServiceType = "FTL", RateType = RateType.PerTrip,
                RateValue = profile.Rate, Currency = "INR", EffectiveFrom = new DateTime(2026, 1, 1), Status = RecordStatus.Active
            });

            var vehicle = new TransporterVehicle
            {
                TransporterId = transporter.Id, RegistrationNumber = registration,
                VehicleTypeReference = lane.VehicleType, PayloadCapacityKg = profile.Capacity,
                OwnershipType = VehicleOwnershipType.Owned, AvailabilityStatus = VehicleAvailabilityStatus.Available, Status = RecordStatus.Active
            };
            db.TransporterVehicles.Add(vehicle);
            await db.SaveChangesAsync();

            // Insurance is mandatory for vehicles; a vehicle without a valid policy is blocked from allocation.
            db.TransporterDocuments.Add(new TransporterDocument
            {
                TransporterId = transporter.Id, VehicleId = vehicle.Id, DocumentTypeId = 4,
                ExpiryDate = new DateOnly(2027, 6, 30), VerificationStatus = DocumentVerificationStatus.Verified,
                VerifiedBy = "seed", VerifiedAt = DateTime.UtcNow
            });

            if (profile.OtdPct is { } otd)
            {
                var sample = profile.OtdSample ?? 120m;
                foreach (var type in new[] { KpiType.OnTimePickup, KpiType.OnTimeDelivery, KpiType.PlacementCompliance,
                    KpiType.PodCompliance, KpiType.TenderAcceptance, KpiType.ClaimsRate })
                {
                    var value = type == KpiType.OnTimeDelivery ? otd : type == KpiType.ClaimsRate ? 0.6m : 95m;
                    db.PerformanceKpis.Add(new PerformanceKpi
                    {
                        TransporterId = transporter.Id, KpiType = type, KpiValue = value, Denominator = sample,
                        Numerator = Math.Round(value * sample / 100m, 2), PeriodStart = new DateOnly(2026, 7, 1),
                        PeriodEnd = new DateOnly(2026, 9, 30), CalculationVersion = 1
                    });
                }
            }

            await db.SaveChangesAsync();
            return new SeededTransporter(transporter.Id, registration);
        });
    }
}
