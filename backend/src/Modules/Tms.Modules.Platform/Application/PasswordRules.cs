using FluentValidation;

namespace Tms.Modules.Platform.Application;

internal static class PasswordRules
{
    public const int MinLength = 12;

    public static IRuleBuilderOptions<T, string> MustBeStrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinLength).WithMessage($"Password must be at least {MinLength} characters.")
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
}
