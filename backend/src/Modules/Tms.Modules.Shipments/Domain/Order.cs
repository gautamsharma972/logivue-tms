using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Domain;

public enum OrderDirection
{
    /// <summary>Goods going out to a customer or branch.</summary>
    Forward = 1,

    /// <summary>Goods coming back (returns, empties, rejected deliveries).</summary>
    Reverse = 2,
}

public enum OrderPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4,
}

public enum HandlingType
{
    Standard = 1,
    Fragile = 2,
    TemperatureControlled = 3,
}

public enum ReturnType
{
    CustomerReturn = 1,
    DamagedMaterial = 2,
    RejectedMaterial = 3,
    EmptyPackaging = 4,
    SupplierReturn = 5,
    ReplacementPickup = 6,
}

public enum OrderStatus
{
    /// <summary>Waiting to be planned onto a shipment.</summary>
    Open = 1,

    /// <summary>Assigned to a shipment that has not left yet.</summary>
    Planned = 2,

    Dispatched = 3,

    Delivered = 4,

    Cancelled = 5,
}

/// <summary>What needs to move: a quantity of goods from one place to another by a date.</summary>
public sealed class Order : AggregateRoot, ITenantScoped
{
    private Order()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Tenant-unique reference such as <c>ORD-00042</c>.</summary>
    public string Number { get; private set; } = null!;

    public OrderDirection Direction { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>The customer's own reference (sales order, delivery note, ERP id).</summary>
    public string? Reference { get; private set; }

    public Party Pickup { get; private set; } = null!;

    public Party Drop { get; private set; } = null!;

    /// <summary>Optional links to the location master, which supplies coordinates for routing.</summary>
    public Guid? PickupLocationId { get; private set; }

    public Guid? DropLocationId { get; private set; }

    /// <summary>Optional daily window in which the consignee accepts deliveries (India time). Both or neither.</summary>
    public TimeOnly? DeliveryWindowFrom { get; private set; }

    public TimeOnly? DeliveryWindowTo { get; private set; }

    public OrderPriority Priority { get; private set; } = OrderPriority.Normal;

    /// <summary>Free-text product class used by the compatibility rules (e.g. FOOD, CHEMICALS). Upper-cased.</summary>
    public string? ProductCategory { get; private set; }

    public HandlingType Handling { get; private set; } = HandlingType.Standard;

    public bool IsHazardous { get; private set; }

    public bool IsStackable { get; private set; } = true;

    /// <summary>Length of the longest single item, in metres. Checked against a vehicle's internal length.</summary>
    public decimal? LongestItemM { get; private set; }

    /// <summary>Only for reverse orders: why the goods are coming back.</summary>
    public ReturnType? ReturnType { get; private set; }

    public string? ReturnReason { get; private set; }

    /// <summary>Daily window in which the goods can be collected (India time). Both or neither.</summary>
    public TimeOnly? PickupWindowFrom { get; private set; }

    public TimeOnly? PickupWindowTo { get; private set; }

    // Denormalised, normalised copies for filtering and grouping in SQL.
    public string PickupState { get; private set; } = null!;

    public string PickupCity { get; private set; } = null!;

    public string DropState { get; private set; } = null!;

    public string DropCity { get; private set; } = null!;

    public decimal WeightKg { get; private set; }

    public decimal? VolumeCbm { get; private set; }

    public int? Packages { get; private set; }

    public string Description { get; private set; } = null!;

    public DateOnly ReadyDate { get; private set; }

    public DateOnly? DeliverByDate { get; private set; }

    public string? Notes { get; private set; }

    public Guid? ShipmentId { get; private set; }

    public string? CancelReason { get; private set; }

    public static Result<Order> Create(
        Guid tenantId, string number, OrderDirection direction, string? reference, Party pickup, Party drop, decimal weightKg,
        decimal? volumeCbm, int? packages, string description, DateOnly readyDate, DateOnly? deliverBy, string? notes)
    {
        var order = new Order { TenantId = tenantId, Number = number, Status = OrderStatus.Open };
        var set = order.Set(direction, reference, pickup, drop, weightKg, volumeCbm, packages, description, readyDate, deliverBy, notes);
        return set.IsFailure ? set.Error : order;
    }

    public Result Update(
        OrderDirection direction, string? reference, Party pickup, Party drop, decimal weightKg, decimal? volumeCbm, int? packages,
        string description, DateOnly readyDate, DateOnly? deliverBy, string? notes) =>
        Status != OrderStatus.Open
            ? Error.Conflict("orders.not_editable", "Only an open order can be edited. Remove it from its shipment first.")
            : Set(direction, reference, pickup, drop, weightKg, volumeCbm, packages, description, readyDate, deliverBy, notes);

    private Result Set(
        OrderDirection direction, string? reference, Party pickup, Party drop, decimal weightKg, decimal? volumeCbm, int? packages,
        string description, DateOnly readyDate, DateOnly? deliverBy, string? notes)
    {
        var errors = new Dictionary<string, string[]>();

        var checkedPickup = pickup.Validated("pickup");
        var checkedDrop = drop.Validated("drop");
        foreach (var failure in new[] { checkedPickup, checkedDrop }.Where(r => r.IsFailure))
        {
            foreach (var (key, value) in failure.Error.ValidationErrors!)
            {
                errors[key] = value;
            }
        }

        if (weightKg is <= 0 or > 100_000)
        {
            errors["weightKg"] = ["Weight must be between 0 and 100,000 kg."];
        }

        if (volumeCbm is <= 0 or > 1_000)
        {
            errors["volumeCbm"] = ["Volume must be between 0 and 1,000 CBM."];
        }

        if (packages is < 0)
        {
            errors["packages"] = ["Packages cannot be negative."];
        }

        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 300)
        {
            errors["description"] = ["Describe the goods (up to 300 characters)."];
        }

        if (reference?.Length > 64)
        {
            errors["reference"] = ["Reference must be at most 64 characters."];
        }

        if (deliverBy is { } by && by < readyDate)
        {
            errors["deliverByDate"] = ["The delivery deadline cannot be before the ready date."];
        }

        if (!Enum.IsDefined(direction))
        {
            errors["direction"] = ["Choose forward or reverse."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        Direction = direction;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Pickup = checkedPickup.Value;
        Drop = checkedDrop.Value;
        PickupState = Pickup.NormalState;
        PickupCity = Pickup.NormalCity;
        DropState = Drop.NormalState;
        DropCity = Drop.NormalCity;
        WeightKg = Math.Round(weightKg, 2);
        VolumeCbm = volumeCbm is { } v ? Math.Round(v, 3) : null;
        Packages = packages;
        Description = description.Trim();
        ReadyDate = readyDate;
        DeliverByDate = deliverBy;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status != OrderStatus.Open)
        {
            return Error.Conflict("orders.not_cancellable", "Only an open order can be cancelled. Take it off its shipment first.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("orders.reason_required", "Say why the order is being cancelled.");
        }

        Status = OrderStatus.Cancelled;
        CancelReason = reason.Trim();
        return Result.Success();
    }

    /// <summary>Records which master locations the addresses came from. Only an open order can be re-linked.</summary>
    public Result LinkLocations(Guid? pickupLocationId, Guid? dropLocationId)
    {
        if (Status != OrderStatus.Open)
        {
            return Error.Conflict("orders.not_editable", "Only an open order can be edited. Remove it from its shipment first.");
        }

        PickupLocationId = pickupLocationId;
        DropLocationId = dropLocationId;
        return Result.Success();
    }

    public Result SetPlanningAttributes(
        OrderPriority priority, string? productCategory, HandlingType handling, bool hazardous, bool stackable, decimal? longestItemM,
        ReturnType? returnType, string? returnReason, TimeOnly? pickupFrom, TimeOnly? pickupTo)
    {
        if (Status != OrderStatus.Open)
        {
            return Error.Conflict("orders.not_editable", "Only an open order can be edited. Remove it from its shipment first.");
        }

        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(priority))
        {
            errors["priority"] = ["Choose a priority."];
        }

        if (!Enum.IsDefined(handling))
        {
            errors["handling"] = ["Choose a handling type."];
        }

        if (productCategory?.Trim().Length > 50)
        {
            errors["productCategory"] = ["The product category can be at most 50 characters."];
        }

        if (longestItemM is <= 0 or > 30)
        {
            errors["longestItemM"] = ["Enter the longest item in metres (up to 30), or leave it blank."];
        }

        if (Direction == OrderDirection.Reverse)
        {
            if (returnType is null || !Enum.IsDefined(returnType.Value))
            {
                errors["returnType"] = ["Choose why the goods are being returned."];
            }

            if (string.IsNullOrWhiteSpace(returnReason))
            {
                errors["returnReason"] = ["Say why the goods are being returned."];
            }
            else if (returnReason.Trim().Length > 300)
            {
                errors["returnReason"] = ["The reason can be at most 300 characters."];
            }
        }
        else if (returnType is not null || !string.IsNullOrWhiteSpace(returnReason))
        {
            errors["returnType"] = ["A return type and reason apply only to return (reverse) orders."];
        }

        if (pickupFrom is null != pickupTo is null)
        {
            errors["pickupWindowTo"] = ["Give both the opening and the closing time, or neither."];
        }
        else if (pickupFrom is { } f && pickupTo is { } t && t <= f)
        {
            errors["pickupWindowTo"] = ["The closing time must be after the opening time."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        Priority = priority;
        ProductCategory = string.IsNullOrWhiteSpace(productCategory) ? null : productCategory.Trim().ToUpperInvariant();
        Handling = handling;
        IsHazardous = hazardous;
        IsStackable = stackable;
        LongestItemM = longestItemM;
        ReturnType = Direction == OrderDirection.Reverse ? returnType : null;
        ReturnReason = Direction == OrderDirection.Reverse ? returnReason!.Trim() : null;
        PickupWindowFrom = pickupFrom;
        PickupWindowTo = pickupTo;
        return Result.Success();
    }

    public Result SetDeliveryWindow(TimeOnly? from, TimeOnly? to)
    {
        if (Status != OrderStatus.Open)
        {
            return Error.Conflict("orders.not_editable", "Only an open order can be edited. Remove it from its shipment first.");
        }

        if (from is null != to is null)
        {
            return Fail("Give both the opening and the closing time, or neither.");
        }

        if (from is { } f && to is { } t && t <= f)
        {
            return Fail("The closing time must be after the opening time.");
        }

        DeliveryWindowFrom = from;
        DeliveryWindowTo = to;
        return Result.Success();

        static Error Fail(string message) => Error.Validation("orders.window_invalid", message) with
        {
            ValidationErrors = new Dictionary<string, string[]> { ["deliveryWindowTo"] = [message] },
        };
    }

    internal void Plan(Guid shipmentId)
    {
        Status = OrderStatus.Planned;
        ShipmentId = shipmentId;
    }

    internal void Release()
    {
        Status = OrderStatus.Open;
        ShipmentId = null;
    }

    internal void MarkDispatched() => Status = OrderStatus.Dispatched;

    internal void MarkDelivered() => Status = OrderStatus.Delivered;
}
