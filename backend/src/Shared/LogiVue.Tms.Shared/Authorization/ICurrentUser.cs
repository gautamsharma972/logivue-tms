namespace LogiVue.Tms.Shared.Authorization;

/// <summary>
/// The authenticated caller. Supplied by the host's authentication mechanism; modules depend only on this abstraction.
/// </summary>
public interface ICurrentUser
{
    string UserId { get; }

    string DisplayName { get; }

    /// <summary>Set when the caller is a transporter (vendor portal) user; null for internal users.</summary>
    long? TransporterId { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);
}

/// <summary>Role names used across modules. Authorization policies are defined in the host.</summary>
public static class Roles
{
    // Internal
    public const string TransportAdmin = "Transport Admin";
    public const string TransportManager = "Transport Manager";
    public const string TransportExecutive = "Transport Executive";
    public const string ComplianceUser = "Compliance User";
    public const string FinanceUser = "Finance User";
    public const string OperationsUser = "Operations User";

    // Vendor (transporter-facing)
    public const string TransporterAdmin = "Transporter Admin";
    public const string TransporterOperationsUser = "Transporter Operations User";
    public const string TransporterViewer = "Transporter Viewer";
}
