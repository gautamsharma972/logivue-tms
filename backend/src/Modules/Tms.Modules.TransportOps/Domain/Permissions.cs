using Tms.SharedKernel.Security;

namespace Tms.Modules.TransportOps.Domain;

public static class OpsPermissions
{
    /// <summary>The operational records: LRs, trips and the delivery timeline, POD, customers and locations. No prices.</summary>
    public const string Read = "ops.read";

    /// <summary>Money: customer freight and detention rates, billing, invoices and receivables.</summary>
    public const string BillingRead = "ops.billing.read";

    /// <summary>Delivery requests, LRs, trips and the CFA timeline.</summary>
    public const string Dispatch = "ops.dispatch";

    /// <summary>Customers and delivery locations (including coordinates and geofence radius).</summary>
    public const string Masters = "ops.masters.manage";

    /// <summary>Upload POD documents against an LR on the office side.</summary>
    public const string PodUpload = "ops.pod.upload";

    public const string PodReview = "ops.pod.review";

    /// <summary>Create and activate customer commercial terms (freight rates, detention, tax).</summary>
    public const string Commercial = "ops.commercial.manage";

    public const string BillingCalculate = "ops.billing.calculate";

    /// <summary>Create an LR whose freight is not taken from the agreement (cross billing). Given only through a specific role.</summary>
    public const string CrossBilling = "ops.crossbilling.create";

    public const string InvoiceManage = "ops.invoice.manage";

    public const string PaymentManage = "ops.payment.manage";

    /// <summary>Accept an arrival that failed the GPS check, with a reason (supervisor only).</summary>
    public const string ArrivalOverride = "ops.arrival.override";

    /// <summary>Record the vehicle arrival and unloading-completion times from the office when the driver could not. Does not include dispatching.</summary>
    public const string DestinationRecord = "ops.destination.record";

    public const string Configure = "ops.configure";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "Transporter operations", "View LRs, trips, the delivery timeline, POD, customers and locations (no prices or invoices)"),
        new(BillingRead, "Transporter operations", "View customer freight and detention rates, billing, invoices and receivables"),
        new(Dispatch, "Transporter operations", "Register delivery requests, generate LRs, assign vehicles and record CFA events"),
        new(Masters, "Transporter operations", "Maintain customers and delivery locations"),
        new(PodUpload, "Transporter operations", "Upload POD documents against an LR"),
        new(PodReview, "Transporter operations", "Accept or reject proof of delivery"),
        new(Commercial, "Transporter operations", "Maintain customer freight rates, detention rules and tax settings"),
        new(CrossBilling, "Transporter operations", "Create cross-billing LRs: freight at a lumpsum or per-kg price entered on the LR instead of the customer agreement"),
        new(BillingCalculate, "Transporter operations", "Calculate freight and detention and review billing holds"),
        new(InvoiceManage, "Transporter operations", "Generate invoices and track portal submission"),
        new(PaymentManage, "Transporter operations", "Record and reconcile customer payments"),
        new(DestinationRecord, "Transporter operations", "Record the vehicle arrival and unloading completion date and time when the driver could not"),
        new(ArrivalOverride, "Transporter operations", "Accept a destination arrival that failed the GPS check"),
        new(Configure, "Transporter operations", "Change operations settings (GPS accuracy, token validity, numbering)"),
    ];
}
