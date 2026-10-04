using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Eligibility;

/// <summary>
/// Estimates the cost of a shipment from a transporter rate. This is an estimate for eligibility and ranking;
/// the freight audit module owns the final charge.
/// </summary>
public static class RateCalculator
{
    /// <returns>The estimated total, or null when a quantity the rate depends on is not supplied.</returns>
    public static decimal? EstimateCost(TransporterRate rate, decimal weightKg, decimal? volumeM3, decimal? distanceKm)
    {
        decimal? basis = rate.RateType switch
        {
            RateType.PerTrip or RateType.PerShipment => rate.RateValue,
            RateType.PerKm when distanceKm is { } km => rate.RateValue * km,
            RateType.PerKg => rate.RateValue * weightKg,
            RateType.PerTon => rate.RateValue * weightKg / 1000m,
            RateType.PerCbm when volumeM3 is { } cbm => rate.RateValue * cbm,
            _ => null
        };

        if (basis is null)
        {
            return null;
        }

        var charged = Math.Max(basis.Value, rate.MinimumCharge ?? 0m);
        var total = charged + (rate.FuelSurcharge ?? 0m) + (rate.TollAmount ?? 0m) + (rate.OtherCharges ?? 0m);
        return Math.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    public static string? MissingQuantityNote(RateType rateType) => rateType switch
    {
        RateType.PerKm => "Distance is needed to price this rate.",
        RateType.PerCbm => "Volume is needed to price this rate.",
        _ => null
    };
}
