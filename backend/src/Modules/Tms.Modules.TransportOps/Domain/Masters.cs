using Tms.SharedKernel.Domain;

namespace Tms.Modules.TransportOps.Domain;

/// <summary>A billed customer (for example an FMCG company). Billing rules live on the agreement, not here.</summary>
public sealed class Customer : AggregateRoot, ITenantScoped
{
    private Customer()
    {
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Gstin { get; private set; }

    public string? BillingLine1 { get; private set; }

    public string? BillingCity { get; private set; }

    public string? BillingState { get; private set; }

    public string? BillingPincode { get; private set; }

    public string? BillingContactName { get; private set; }

    public string? BillingContactEmail { get; private set; }

    public string? BillingContactPhone { get; private set; }

    public string? DeliveryContactName { get; private set; }

    public string? DeliveryContactPhone { get; private set; }

    public string? CommercialContactName { get; private set; }

    public string? CommercialContactEmail { get; private set; }

    /// <summary>Default days from invoice date to due date.</summary>
    public int DueDays { get; private set; } = 30;

    /// <summary>Free-text billing requirements (for example "one invoice per PO").</summary>
    public string? BillingRequirements { get; private set; }

    /// <summary>How invoices reach the customer: manual upload to their portal, or email.</summary>
    public string? PortalMethod { get; private set; }

    public string? PortalReference { get; private set; }

    /// <summary>Comma-separated request fields this customer insists on (CustomerReference, Dimensions, Volume), checked when a delivery request is validated.</summary>
    public string? RequiredRequestFields { get; private set; }

    public ActiveStatus Status { get; private set; } = ActiveStatus.Active;

    public IReadOnlyList<string> RequiredFields => string.IsNullOrWhiteSpace(RequiredRequestFields) ? [] : RequiredRequestFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Customer Create(Guid tenantId, string code, string name) => new() { TenantId = tenantId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim() };

    public void Update(string name, string? gstin, string? line1, string? city, string? state, string? pincode, string? billingContactName, string? billingContactEmail, string? billingContactPhone,
        string? deliveryContactName, string? deliveryContactPhone, string? commercialContactName, string? commercialContactEmail, int dueDays, string? billingRequirements,
        string? portalMethod, string? portalReference, string? requiredRequestFields, ActiveStatus status)
    {
        Name = name.Trim();
        Gstin = Clean(gstin)?.ToUpperInvariant();
        BillingLine1 = Clean(line1);
        BillingCity = Clean(city);
        BillingState = Clean(state);
        BillingPincode = Clean(pincode);
        BillingContactName = Clean(billingContactName);
        BillingContactEmail = Clean(billingContactEmail);
        BillingContactPhone = Clean(billingContactPhone);
        DeliveryContactName = Clean(deliveryContactName);
        DeliveryContactPhone = Clean(deliveryContactPhone);
        CommercialContactName = Clean(commercialContactName);
        CommercialContactEmail = Clean(commercialContactEmail);
        DueDays = dueDays;
        BillingRequirements = Clean(billingRequirements);
        PortalMethod = Clean(portalMethod);
        PortalReference = Clean(portalReference);
        RequiredRequestFields = Clean(requiredRequestFields);
        Status = status;
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A CFA or a distributor. Coordinates and the geofence radius are what the arrival check uses.</summary>
public sealed class OpsLocation : AggregateRoot, ITenantScoped
{
    public const int DefaultRadiusM = 150;

    private OpsLocation()
    {
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public LocationKind Kind { get; private set; }

    public Guid? CustomerId { get; private set; }

    public string Line1 { get; private set; } = null!;

    public string City { get; private set; } = null!;

    public string State { get; private set; } = null!;

    public string? Pincode { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public int GeofenceRadiusM { get; private set; } = DefaultRadiusM;

    public string? AccessNotes { get; private set; }

    /// <summary>The HSN/SAC code of the service billed for this CFA or distributor; printed on the customer's invoice.</summary>
    public string? SacCode { get; private set; }

    public ActiveStatus Status { get; private set; } = ActiveStatus.Active;

    public bool HasCoordinates => Latitude is not null && Longitude is not null;

    public static bool InIndia(double latitude, double longitude) => latitude is >= 6 and <= 38 && longitude is >= 68 and <= 98;

    public static OpsLocation Create(Guid tenantId, string code, string name, LocationKind kind) =>
        new() { TenantId = tenantId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), Kind = kind };

    public void Update(string name, LocationKind kind, Guid? customerId, string line1, string city, string state, string? pincode, double? latitude, double? longitude, int radiusM, string? accessNotes, ActiveStatus status, string? sacCode = null)
    {
        SacCode = Customer.Clean(sacCode);
        Name = name.Trim();
        Kind = kind;
        CustomerId = customerId;
        Line1 = line1.Trim();
        City = city.Trim();
        State = state.Trim();
        Pincode = Customer.Clean(pincode);
        Latitude = latitude;
        Longitude = longitude;
        GeofenceRadiusM = radiusM;
        AccessNotes = Customer.Clean(accessNotes);
        Status = status;
    }
}

/// <summary>A unit of measure the office can pick on an LR line (box, carton, bag...). Per-tenant data; <see cref="UnitsOfMeasure.Defaults"/> apply until a tenant keeps its own list.</summary>
public sealed class UnitOfMeasure : Entity, ITenantScoped
{
    private UnitOfMeasure()
    {
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; } = true;

    public static UnitOfMeasure Create(Guid tenantId, string code, string name) =>
        new() { TenantId = tenantId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim() };

    public void Edit(string name, bool isActive)
    {
        Name = name.Trim();
        IsActive = isActive;
    }
}

public static class UnitsOfMeasure
{
    public static IReadOnlyList<(string Code, string Name)> Defaults { get; } =
    [
        ("PKG", "Package"), ("BOX", "Box"), ("CTN", "Carton"), ("BAG", "Bag"), ("PCS", "Pieces"), ("NOS", "Numbers"), ("DRM", "Drum"), ("PLT", "Pallet"), ("BDL", "Bundle"), ("CAS", "Case"), ("KG", "Kilogram"), ("LTR", "Litre"),
    ];
}
