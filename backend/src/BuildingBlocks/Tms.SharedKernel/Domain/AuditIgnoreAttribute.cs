namespace Tms.SharedKernel.Domain;

/// <summary>
/// On a property: excludes it from audit-log change snapshots (e.g. a password hash).
/// On a class: the entity is not audited at all (high-churn technical rows such as refresh tokens).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class AuditIgnoreAttribute : Attribute;

/// <summary>
/// The audit trail records that this property changed, and shows only the last four characters of each value, so
/// investigators can tell what changed without the log becoming a second copy of the secret.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditMaskAttribute : Attribute;
