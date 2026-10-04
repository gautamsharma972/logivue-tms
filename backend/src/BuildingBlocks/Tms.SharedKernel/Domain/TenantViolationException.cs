namespace Tms.SharedKernel.Domain;

/// <summary>Thrown when a write would cross a tenant boundary. This is always a bug or an attack.</summary>
public sealed class TenantViolationException(string message) : InvalidOperationException(message);
