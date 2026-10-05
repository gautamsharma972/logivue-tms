using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Domain;

public static class DeliveryPermissions
{
    public const string Read = "deliveries.read";

    /// <summary>Create deliveries, assign them, reschedule, close and cancel.</summary>
    public const string Manage = "deliveries.manage";

    /// <summary>Vendor portal / drivers: run your own company's deliveries on the road and submit proof.</summary>
    public const string Execute = "deliveries.execute";

    /// <summary>Accept, reject or correct proofs of delivery. Staff only: a vendor cannot approve its own paperwork.</summary>
    public const string PodReview = "deliveries.pod.review";

    public const string ExceptionsManage = "deliveries.exceptions.manage";

    public const string Configure = "deliveries.configure";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "Deliveries", "View deliveries, proofs of delivery and exceptions"),
        new(Manage, "Deliveries", "Create, assign, reschedule and close deliveries"),
        new(Execute, "Deliveries", "Vendor portal: run own deliveries, capture proof and submit it", ExternalAllowed: true),
        new(PodReview, "Deliveries", "Review, accept or reject proofs of delivery and check OCR"),
        new(ExceptionsManage, "Deliveries", "Assign, escalate and resolve delivery exceptions"),
        new(Configure, "Deliveries", "Change delivery and proof-of-delivery rules"),
    ];
}
