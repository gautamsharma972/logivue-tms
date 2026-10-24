using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Domain;

/// <summary>A customer organisation (shipper). Every other business record hangs off one tenant.</summary>
public sealed class Tenant : AggregateRoot
{
    private Tenant()
    {
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; }

    /// <summary>The operator's own organisation: its administrators may give other organisations their modules. Never a customer.</summary>
    public bool IsPlatform { get; private set; }

    /// <summary>Comma-separated module keys (see <see cref="TmsModules"/>). Null means every module, so organisations that existed before modules were allocated keep everything.</summary>
    public string? EnabledModules { get; private set; }

    /// <summary>The modules this organisation has; null means all of them.</summary>
    public IReadOnlySet<string>? Modules => IsPlatform ? null : TmsModules.Parse(EnabledModules);

    public static Tenant Create(string code, string name, IEnumerable<string>? modules = null, bool isPlatform = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var tenant = new Tenant { Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), IsActive = true, IsPlatform = isPlatform };
        if (modules is not null)
        {
            tenant.SetModules(modules);
        }

        return tenant;
    }

    /// <summary>Replaces the organisation's modules. Unknown keys are ignored; an empty list means no business modules (core administration only).</summary>
    public void SetModules(IEnumerable<string> modules) =>
        EnabledModules = string.Join(',', TmsModules.All.Select(m => m.Key).Where(k => modules.Contains(k, StringComparer.Ordinal)));
}
