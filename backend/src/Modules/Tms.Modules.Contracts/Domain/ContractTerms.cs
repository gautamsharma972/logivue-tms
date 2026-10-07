using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

/// <summary>The commercial terms around the rate cards: how weight is converted, and the accessorial charges the contract allows.</summary>
public sealed record ContractTerms(
    decimal VolumetricKgPerCbm,
    decimal DetentionFreeHours,
    decimal DetentionRatePerHour,
    decimal LoadingCharge,
    decimal UnloadingCharge,
    decimal MultiDropChargePerPoint,
    decimal MinChargePerConsignment,
    string? Notes,
    RoundingRule? Rounding = null,
    decimal? DiscountPercent = null)
{
    public static ContractTerms Default { get; } = new(250m, 24m, 0m, 0m, 0m, 0m, 0m, null);

    public Result Validate()
    {
        static Result Fail(string field, string message) =>
            Error.Validation("contracts.terms_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { [field] = [message] } };

        if (VolumetricKgPerCbm is < 50 or > 1000)
        {
            return Fail("volumetricKgPerCbm", "The volumetric factor should be between 50 and 1000 kg per cubic metre.");
        }

        if (DetentionFreeHours < 0 || DetentionRatePerHour < 0)
        {
            return Fail("detentionRatePerHour", "Detention terms cannot be negative.");
        }

        if (LoadingCharge < 0 || UnloadingCharge < 0 || MultiDropChargePerPoint < 0 || MinChargePerConsignment < 0)
        {
            return Fail("loadingCharge", "Charges cannot be negative.");
        }

        if (DiscountPercent is < 0 or > 100)
        {
            return Fail("discountPercent", "A discount is between 0 and 100%.");
        }

        if (Rounding?.Validate() is { IsFailure: true } rounding)
        {
            return rounding.Error;
        }

        return Notes?.Length > 2000 ? Fail("notes", "Notes can be at most 2000 characters.") : Result.Success();
    }
}
