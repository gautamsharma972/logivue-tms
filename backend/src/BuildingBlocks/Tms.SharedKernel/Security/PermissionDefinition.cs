namespace Tms.SharedKernel.Security;

/// <summary>A permission a module contributes to the tenant-wide catalog used by role management.</summary>
/// <param name="Code">Stable identifier, <c>{resource}.{action}</c>, e.g. <c>transporters.manage</c>.</param>
/// <param name="Module">Display group, e.g. "Transporters".</param>
/// <param name="ExternalAllowed">May be granted to external (vendor-portal / driver) roles. Default false: internal staff only.</param>
public sealed record PermissionDefinition(string Code, string Module, string Description, bool ExternalAllowed = false);
