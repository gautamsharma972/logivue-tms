using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Shipments.Domain;

/// <summary>
/// Checks a load against every vehicle type and says, for each, whether it fits and — if not — by how much it fails.
/// Weight and volume are independent hard limits: failing either rejects the vehicle.
/// </summary>
/// <summary>What a load needs from the vehicle beyond weight and volume.</summary>
public sealed record LoadRequirements(bool Hazardous = false, bool TemperatureControlled = false, decimal? LongestItemM = null)
{
    public static LoadRequirements From(IEnumerable<PlannableOrder> orders)
    {
        var list = orders.ToList();
        return new LoadRequirements(
            list.Any(o => o.IsHazardous), list.Any(o => o.Handling == HandlingType.TemperatureControlled), list.Where(o => o.LongestItemM.HasValue).Select(o => o.LongestItemM).Max());
    }
}

public static class VehicleEvaluator
{
    public static IReadOnlyList<VehicleTypeEvaluation> Evaluate(decimal weightKg, decimal? volumeCbm, IEnumerable<VehicleTypeInfo> types, LoadRequirements? requirements = null) =>
        types
            .Where(t => t.IsActive && t.PayloadKg > 0)
            .OrderBy(t => t.PayloadKg).ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => Evaluate(weightKg, volumeCbm, t, requirements))
            .ToList();

    public static VehicleTypeEvaluation Evaluate(decimal weightKg, decimal? volumeCbm, VehicleTypeInfo type, LoadRequirements? requirements = null)
    {
        var weightUtil = Math.Round(weightKg / type.PayloadKg, 4);
        decimal? volumeUtil = volumeCbm is { } v && type.VolumeCbm is { } cap and > 0 ? Math.Round(v / cap, 4) : null;

        if (weightKg > type.PayloadKg)
        {
            return new(type.Id, type.Name, type.PayloadKg, type.VolumeCbm, false,
                $"Payload exceeded by {weightKg - type.PayloadKg:0.##} kg (load {weightKg:0.##} kg, capacity {type.PayloadKg} kg).",
                UnplannedCodes.PayloadExceeded, weightUtil, volumeUtil);
        }

        if (volumeCbm is { } vol && type.VolumeCbm is { } capacity && vol > capacity)
        {
            return new(type.Id, type.Name, type.PayloadKg, type.VolumeCbm, false,
                $"Volume exceeded by {vol - capacity:0.##} CBM (load {vol:0.##} CBM, capacity {capacity:0.##} CBM).",
                UnplannedCodes.VolumeExceeded, weightUtil, volumeUtil);
        }

        // Beyond weight and volume: what the body can carry and whether the longest item fits.
        if (requirements is { Hazardous: true } && !type.AllowsHazardous)
        {
            return new(type.Id, type.Name, type.PayloadKg, type.VolumeCbm, false, $"{type.Name} is not approved for hazardous goods.", UnplannedCodes.NotCompatible, weightUtil, volumeUtil);
        }

        if (requirements is { TemperatureControlled: true } && !type.SupportsTemperatureControl)
        {
            return new(type.Id, type.Name, type.PayloadKg, type.VolumeCbm, false, $"{type.Name} has no temperature control, which this load needs.", UnplannedCodes.NotCompatible, weightUtil, volumeUtil);
        }

        decimal? lengthUtil = null;
        if (requirements?.LongestItemM is { } longest && type.LengthM is { } length and > 0)
        {
            if (longest > length)
            {
                return new(type.Id, type.Name, type.PayloadKg, type.VolumeCbm, false,
                    $"The longest item ({longest:0.##} m) is longer than {type.Name} ({length:0.##} m inside).", UnplannedCodes.TooLong, weightUtil, volumeUtil);
            }

            lengthUtil = Math.Round(longest / length, 4);
        }

        return new(type.Id, type.Name, type.PayloadKg, type.VolumeCbm, true, null, null, weightUtil, volumeUtil, lengthUtil);
    }

    /// <summary>The fill that matters is the tighter of weight and volume.</summary>
    public static decimal BindingUtilisation(VehicleTypeEvaluation e) => Math.Max(e.WeightUtilisation, e.VolumeUtilisation ?? 0m);
}
