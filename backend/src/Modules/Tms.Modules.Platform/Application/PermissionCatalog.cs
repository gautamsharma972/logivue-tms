using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application;

/// <summary>Every permission contributed by every module (collected from DI).</summary>
public sealed class PermissionCatalog(IEnumerable<PermissionDefinition> definitions)
{
    private readonly IReadOnlyList<PermissionDefinition> _all =
        definitions.OrderBy(d => d.Module, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ToList();

    private readonly HashSet<string> _codes = definitions.Select(d => d.Code).ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<PermissionDefinition> All => _all;

    public IReadOnlyCollection<string> Codes => _codes;

    private readonly HashSet<string> _external = definitions.Where(d => d.ExternalAllowed).Select(d => d.Code).ToHashSet(StringComparer.Ordinal);

    public bool Contains(string code) => _codes.Contains(code);

    public bool IsExternalAllowed(string code) => _external.Contains(code);
}
