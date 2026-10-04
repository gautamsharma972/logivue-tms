using FluentValidation;
using Tms.Modules.Platform.Domain;

namespace Tms.Modules.Platform.Application.Roles;

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    RoleAudience Audience,
    IReadOnlyList<string> Permissions,
    int UserCount,
    long Version);

/// <param name="Version">Required when updating (optimistic concurrency); ignored on create.</param>
/// <param name="Audience">Chosen at creation (default Internal) and fixed afterwards.</param>
public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions, long? Version, RoleAudience? Audience = null);

internal sealed class SaveRoleRequestValidator : AbstractValidator<SaveRoleRequest>
{
    public SaveRoleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Permissions).NotNull();
    }
}

internal static class RoleMapping
{
    public static RoleDto ToDto(this Role role, int userCount) =>
        new(role.Id, role.Name, role.Description, role.IsSystem, role.Audience, role.Permissions, userCount, role.Version);
}
