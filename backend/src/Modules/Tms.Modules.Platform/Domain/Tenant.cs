using Tms.SharedKernel.Domain;

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

    public static Tenant Create(string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Tenant { Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), IsActive = true };
    }
}
