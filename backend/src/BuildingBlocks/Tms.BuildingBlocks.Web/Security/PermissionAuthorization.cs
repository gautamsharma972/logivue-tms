using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace Tms.BuildingBlocks.Web.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(TmsClaimTypes.Permission, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Builds a <c>perm:{name}</c> policy on demand so permissions never need to be pre-registered.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
            .Build();
    }
}

public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization($"{PermissionPolicyProvider.Prefix}{permission}");
}
