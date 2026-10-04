using FluentValidation;
using Tms.Modules.Platform.Domain;

namespace Tms.Modules.Platform.Application.Users;

public sealed record RoleSummary(Guid Id, string Name);

public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    UserType Type,
    Guid? TransporterId,
    bool IsActive,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    long Version,
    IReadOnlyList<RoleSummary> Roles);

public sealed record CreateUserRequest(
    string Email,
    string FullName,
    string Password,
    UserType Type,
    IReadOnlyList<Guid> RoleIds,
    Guid? TransporterId = null,
    bool RequirePasswordChange = false);

public sealed record UpdateUserRequest(string FullName, bool IsActive, IReadOnlyList<Guid> RoleIds, long Version);

public sealed record ListUsersQuery(string? Search, bool? IsActive, int Page = 1, int PageSize = 25);

internal sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).MustBeStrongPassword();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.RoleIds).NotNull();
    }
}

internal sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RoleIds).NotNull();
    }
}

internal static class UserMapping
{
    public static UserDto ToDto(this User user) =>
        new(
            user.Id,
            user.Email,
            user.FullName,
            user.Type,
            user.TransporterId,
            user.IsActive,
            user.LastLoginAt,
            user.CreatedAt,
            user.Version,
            user.Roles.OrderBy(r => r.Name, StringComparer.Ordinal).Select(r => new RoleSummary(r.Id, r.Name)).ToList());
}
