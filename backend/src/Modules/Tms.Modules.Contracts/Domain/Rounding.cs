using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

public enum RoundingMode
{
    Nearest = 1,
    Up = 2,
    Down = 3,
}

/// <summary>
/// How a rated freight total is rounded: to a multiple of <see cref="Increment"/> (₹10, ₹0.50, ₹0.01), in the given direction. The default is the paise rounding the
/// engine always did, so a contract that says nothing is rated as before.
/// </summary>
public sealed record RoundingRule(RoundingMode Mode = RoundingMode.Nearest, decimal Increment = 0.01m, int Decimals = 2)
{
    public static RoundingRule Paise { get; } = new();

    public Result Validate() =>
        !Enum.IsDefined(Mode) || Increment <= 0 || Increment > 1000 || Decimals is < 0 or > 4
            ? Error.Validation("contracts.rounding_invalid", "Rounding needs a direction, an increment above zero and up to 1,000, and 0 to 4 decimals.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["rounding"] = ["Rounding needs a direction, an increment above zero and up to 1,000, and 0 to 4 decimals."] },
            }
            : Result.Success();

    public decimal Apply(decimal amount)
    {
        var steps = amount / Increment;
        var whole = Mode switch
        {
            RoundingMode.Up => Math.Ceiling(steps),
            RoundingMode.Down => Math.Floor(steps),
            _ => Math.Round(steps, 0, MidpointRounding.AwayFromZero),
        };
        return Math.Round(whole * Increment, Decimals, MidpointRounding.AwayFromZero);
    }
}
