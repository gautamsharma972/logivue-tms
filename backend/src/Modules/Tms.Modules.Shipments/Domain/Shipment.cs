using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Domain;

public enum ShipmentStatus
{
    /// <summary>Being planned; orders can still be added, removed and re-sequenced.</summary>
    Draft = 1,

    /// <summary>Offered to a transporter, waiting for them to accept with a vehicle and driver.</summary>
    Tendered = 2,

    /// <summary>A transporter committed a vehicle and driver; waiting to leave.</summary>
    Accepted = 3,

    Dispatched = 4,

    Delivered = 5,

    Cancelled = 6,

    /// <summary>Offered to several transporters at once (a broadcast tender); no one holds it until a planner awards a bid.</summary>
    Bidding = 7,
}

/// <summary>An order's place on a shipment: its drop sequence and, once the shipment leaves, its lorry-receipt number.</summary>
public sealed class ShipmentOrder : Entity, ITenantScoped
{
    private ShipmentOrder()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ShipmentId { get; private set; }

    public Guid OrderId { get; private set; }

    /// <summary>1-based position in the delivery run.</summary>
    public int DropSequence { get; internal set; }

    public string? LrNumber { get; internal set; }

    public OrderDirection Direction { get; private set; } = OrderDirection.Forward;

    /// <summary>A reverse order collected on the way back and carried to the shipment's origin. It loads weight instead of unloading it.</summary>
    public bool IsReturn { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public string? ReceiverName { get; private set; }

    public int? PackagesShipped { get; private set; }

    public int? DeliveredPackages { get; private set; }

    public int? DamagedPackages { get; private set; }

    public string? DeliveryRemarks { get; private set; }

    public PodStatus PodStatus { get; private set; } = PodStatus.Awaiting;

    /// <summary>How many times a proof of delivery was refused and had to be replaced.</summary>
    public int PodRejectionCount { get; private set; }

    public DateTimeOffset? PodReviewedAt { get; private set; }

    public Guid? PodReviewedBy { get; private set; }

    public string? PodRejectionReason { get; private set; }

    public bool IsDelivered => DeliveredAt is not null;

    public int? ShortagePackages => PackagesShipped is { } s && DeliveredPackages is { } d ? s - d : null;

    public bool HasException => ShortagePackages is > 0 || DamagedPackages is > 0;

    internal void MarkDelivered(DateTimeOffset at, string? receiver, int? shipped, int? delivered, int? damaged, string? remarks)
    {
        DeliveredAt = at;
        ReceiverName = receiver?.Trim();
        PackagesShipped = shipped;
        DeliveredPackages = delivered;
        DamagedPackages = damaged;
        DeliveryRemarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
    }

    internal Result ProofAdded()
    {
        if (!IsDelivered)
        {
            return Error.Conflict("pod.not_delivered", "Record the delivery before attaching proof of it.");
        }

        if (PodStatus == PodStatus.Verified)
        {
            return Error.Conflict("pod.verified", "This proof has already been verified.");
        }

        PodStatus = PodStatus.Uploaded;
        PodRejectionReason = null;
        return Result.Success();
    }

    /// <summary>Called after a document is removed; with none left the proof is outstanding again.</summary>
    internal Result ProofRemoved(int remaining)
    {
        if (PodStatus == PodStatus.Verified)
        {
            return Error.Conflict("pod.verified", "A verified proof cannot be removed.");
        }

        if (remaining == 0)
        {
            PodStatus = PodStatus.Awaiting;
        }

        return Result.Success();
    }

    internal Result Verify(Guid? userId, DateTimeOffset now)
    {
        if (PodStatus != PodStatus.Uploaded)
        {
            return Error.Conflict("pod.not_uploaded", "There is no uploaded proof waiting to be checked.");
        }

        PodStatus = PodStatus.Verified;
        PodReviewedAt = now;
        PodReviewedBy = userId;
        PodRejectionReason = null;
        return Result.Success();
    }

    internal Result Reject(Guid? userId, string reason, DateTimeOffset now)
    {
        if (PodStatus != PodStatus.Uploaded)
        {
            return Error.Conflict("pod.not_uploaded", "There is no uploaded proof waiting to be checked.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("pod.reason_required", "Say what is wrong with the proof.");
        }

        PodStatus = PodStatus.Rejected;
        PodRejectionCount++;
        PodReviewedAt = now;
        PodReviewedBy = userId;
        PodRejectionReason = reason.Trim();
        return Result.Success();
    }

    internal static ShipmentOrder Create(Guid tenantId, Guid shipmentId, Guid orderId, int sequence, OrderDirection direction, bool isReturn) =>
        new() { TenantId = tenantId, ShipmentId = shipmentId, OrderId = orderId, DropSequence = sequence, Direction = direction, IsReturn = isReturn };
}

/// <summary>One vehicle movement: orders from a common pickup, delivered in a set sequence by one transporter.</summary>
public sealed class Shipment : AggregateRoot, ITenantScoped
{
    private readonly List<ShipmentOrder> _orders = [];

    private Shipment()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Tenant-unique reference such as <c>SH-00042</c>.</summary>
    public string Number { get; private set; } = null!;

    public ShipmentStatus Status { get; private set; }

    public FreightMode Mode { get; private set; }

    public Guid? VehicleTypeId { get; private set; }

    public DateOnly PlannedPickupDate { get; private set; }

    public decimal? DistanceKm { get; private set; }

    public string OriginState { get; private set; } = null!;

    public string OriginCity { get; private set; } = null!;

    /// <summary>Weight delivered on the outbound run (the load that leaves the origin).</summary>
    public decimal TotalWeightKg { get; private set; }

    /// <summary>The most the vehicle carries at any moment, once return pickups are counted. Zero on shipments made before returns existed.</summary>
    public decimal PeakOnboardKg { get; private set; }

    /// <summary>The plan this shipment was committed from (e.g. <c>PLN-20261004-001 v2</c>), for tracing back to why it was made this way.</summary>
    public string? PlanReference { get; private set; }

    /// <summary>What the plan estimated this trip would cost at the time, kept even if rates change.</summary>
    public decimal? PlannedCost { get; private set; }

    /// <summary>For a collection run (a milk run bringing goods in from several places to one): where everything is delivered.</summary>
    public string? CollectionState { get; private set; }

    public string? CollectionCity { get; private set; }

    /// <summary>True for a trip that collects at several places and delivers to one. Its <see cref="OriginCity"/> is the farthest pickup, which is the lane that is priced.</summary>
    public bool IsCollectionRun => CollectionCity is not null;

    public void RecordPlan(string reference, decimal cost)
    {
        PlanReference = reference.Length > 40 ? reference[..40] : reference;
        PlannedCost = cost;
    }

    /// <summary>What the vehicle's capacity must cover.</summary>
    public decimal LoadKg => Math.Max(TotalWeightKg, PeakOnboardKg);

    public decimal? TotalVolumeCbm { get; private set; }

    // Allocation (who will carry it, and at what expected cost).
    public Guid? TransporterId { get; private set; }

    public Guid? ContractId { get; private set; }

    public string? ContractReference { get; private set; }

    public decimal? FreightEstimate { get; private set; }

    public IReadOnlyList<FreightQuoteLine> EstimateLines { get; private set; } = [];

    /// <summary>Why a dearer-than-cheapest transporter was chosen. Required when the choice is not the lowest quote.</summary>
    public string? OverrideReason { get; private set; }

    public DateTimeOffset? TenderedAt { get; private set; }

    public int RejectionCount { get; private set; }

    public string? LastRejectionReason { get; private set; }

    // Assignment (the vehicle and driver the transporter committed).
    public Guid? VehicleId { get; private set; }

    /// <summary>The vehicle's capacity when it was assigned, so utilisation stays true if the vehicle type is edited later.</summary>
    public int? VehiclePayloadKg { get; private set; }

    public string? VehicleRegistration { get; private set; }

    public Guid? DriverId { get; private set; }

    public string? DriverName { get; private set; }

    public string? DriverPhone { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public string? CancelReason { get; private set; }

    public IReadOnlyCollection<ShipmentOrder> Orders => _orders;

    public IReadOnlyList<ShipmentOrder> OrdersInDropSequence => _orders.OrderBy(o => o.DropSequence).ToList();

    public static Result<Shipment> Create(
        Guid tenantId, string number, IReadOnlyList<Order> orders, FreightMode mode, Guid? vehicleTypeId, DateOnly plannedPickup, decimal? distanceKm)
    {
        if (orders.Count == 0)
        {
            return Error.Validation("shipments.no_orders", "Choose at least one order.");
        }

        var shipment = new Shipment { TenantId = tenantId, Number = number, Status = ShipmentStatus.Draft };
        var added = shipment.AddOrders(orders);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var set = shipment.SetPlan(mode, vehicleTypeId, plannedPickup, distanceKm);
        return set.IsFailure ? set.Error : shipment;
    }

    /// <summary>
    /// A shipment that collects return (reverse) orders from several places and delivers them all to one. The lane is priced from
    /// <paramref name="laneOriginState"/>/<paramref name="laneOriginCity"/>, the farthest pickup, with the other pickups as extra stops.
    /// </summary>
    public static Result<Shipment> CreateCollection(
        Guid tenantId, string number, IReadOnlyList<Order> orders, string laneOriginState, string laneOriginCity, FreightMode mode, Guid? vehicleTypeId,
        DateOnly plannedPickup, decimal? distanceKm)
    {
        if (orders.Count == 0)
        {
            return Error.Validation("shipments.no_orders", "Choose at least one order.");
        }

        var first = orders[0];
        if (orders.Any(o => o.Direction != OrderDirection.Reverse || o.DropState != first.DropState || o.DropCity != first.DropCity))
        {
            return Error.Validation("shipments.collection_mismatch", "A collection run takes return orders that are all delivered to the same place.");
        }

        if (orders.All(o => o.PickupState != laneOriginState || o.PickupCity != laneOriginCity))
        {
            return Error.Validation("shipments.collection_mismatch", "The lane origin must be one of the pickups.");
        }

        var shipment = new Shipment
        {
            TenantId = tenantId, Number = number, Status = ShipmentStatus.Draft, OriginState = laneOriginState, OriginCity = laneOriginCity,
            CollectionState = first.DropState, CollectionCity = first.DropCity,
        };
        var added = shipment.AddOrders(orders);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var set = shipment.SetPlan(mode, vehicleTypeId, plannedPickup, distanceKm);
        return set.IsFailure ? set.Error : shipment;
    }

    public Result UpdatePlan(FreightMode mode, Guid? vehicleTypeId, DateOnly plannedPickup, decimal? distanceKm) =>
        EnsureDraft() ?? SetPlan(mode, vehicleTypeId, plannedPickup, distanceKm);

    private Result SetPlan(FreightMode mode, Guid? vehicleTypeId, DateOnly plannedPickup, decimal? distanceKm)
    {
        if (!Enum.IsDefined(mode))
        {
            return Error.Validation("shipments.mode_invalid", "Choose full truck load or part load.");
        }

        if (mode == FreightMode.Ftl && vehicleTypeId is null)
        {
            return Error.Validation("shipments.vehicle_type_required", "Choose the vehicle type for a full-truck-load shipment.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["vehicleTypeId"] = ["Choose the vehicle type."] },
            };
        }

        if (distanceKm is <= 0 or > 10_000)
        {
            return Error.Validation("shipments.distance_invalid", "Distance must be between 0 and 10,000 km.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["distanceKm"] = ["Distance must be between 0 and 10,000 km."] },
            };
        }

        Mode = mode;
        VehicleTypeId = mode == FreightMode.Ftl ? vehicleTypeId : null;
        PlannedPickupDate = plannedPickup;
        DistanceKm = distanceKm;
        return Result.Success();
    }

    /// <summary>Adds open orders from the same pickup. Their drop sequence continues after the existing ones.</summary>
    public Result AddOrders(IReadOnlyList<Order> orders)
    {
        if (EnsureDraft() is { } locked)
        {
            return locked;
        }

        foreach (var order in orders)
        {
            if (order.Status != OrderStatus.Open)
            {
                return Error.Conflict("shipments.order_not_open", $"Order {order.Number} is not open, so it cannot be added.");
            }

            if (_orders.Any(o => o.OrderId == order.Id))
            {
                return Error.Conflict("shipments.order_duplicate", $"Order {order.Number} is already on this shipment.");
            }
        }

        if (orders.Count == 0)
        {
            return Result.Success();
        }

        if (IsCollectionRun)
        {
            var stray = orders.FirstOrDefault(o => o.Direction != OrderDirection.Reverse || o.DropState != CollectionState || o.DropCity != CollectionCity);
            if (stray is not null)
            {
                return Error.Validation("shipments.collection_mismatch", $"Order {stray.Number} is not a return order delivered to {CollectionCity}, so it cannot join this collection run.");
            }

            var after = _orders.Count == 0 ? 1 : _orders.Max(o => o.DropSequence) + 1;
            foreach (var order in orders)
            {
                _orders.Add(ShipmentOrder.Create(TenantId, Id, order.Id, after++, order.Direction, false));
                order.Plan(Id);
            }

            return Result.Success();
        }

        // The origin is where the forward orders are picked up (or, for a reverse-only run, where its orders are).
        var anchor = _orders.Count == 0 ? orders.FirstOrDefault(o => o.Direction == OrderDirection.Forward) ?? orders[0] : null;
        var originState = anchor?.PickupState ?? OriginState;
        var originCity = anchor?.PickupCity ?? OriginCity;

        var hasForward = _orders.Any(o => o.Direction == OrderDirection.Forward) || orders.Any(o => o.Direction == OrderDirection.Forward);
        var hasReverseRun = _orders.Any(o => o.Direction == OrderDirection.Reverse && !o.IsReturn) || (anchor is not null && anchor.Direction == OrderDirection.Reverse);
        var returns = new HashSet<Guid>();

        foreach (var order in orders)
        {
            var fromOrigin = order.PickupState == originState && order.PickupCity == originCity;
            var toOrigin = order.DropState == originState && order.DropCity == originCity;

            if (order.Direction == OrderDirection.Forward)
            {
                if (!fromOrigin)
                {
                    return Error.Validation("shipments.pickup_mismatch", "All orders on a shipment must be picked up from the same place.");
                }

                if (hasReverseRun)
                {
                    return Error.Validation("shipments.direction_mismatch", "Forward and reverse orders cannot share a shipment unless the reverse order is a return to the origin.");
                }
            }
            else if (fromOrigin)
            {
                if (hasForward)
                {
                    return Error.Validation("shipments.direction_mismatch", "Forward and reverse orders cannot share a shipment unless the reverse order is a return to the origin.");
                }
            }
            else if (hasForward && toOrigin)
            {
                returns.Add(order.Id); // collected elsewhere on the way back, delivered to the origin
            }
            else
            {
                return Error.Validation("shipments.pickup_mismatch", hasForward
                    ? "A reverse order can only ride along if it is delivered back to this shipment's origin."
                    : "All orders on a shipment must be picked up from the same place.");
            }
        }

        OriginState = originState;
        OriginCity = originCity;
        var next = _orders.Count == 0 ? 1 : _orders.Max(o => o.DropSequence) + 1;
        foreach (var order in orders)
        {
            _orders.Add(ShipmentOrder.Create(TenantId, Id, order.Id, next++, order.Direction, returns.Contains(order.Id)));
            order.Plan(Id);
        }

        return Result.Success();
    }

    public Result RemoveOrder(Order order)
    {
        if (EnsureDraft() is { } locked)
        {
            return locked;
        }

        var link = _orders.FirstOrDefault(o => o.OrderId == order.Id);
        if (link is null)
        {
            return Error.NotFound("shipments.order_not_found", "That order is not on this shipment.");
        }

        if (_orders.Count == 1)
        {
            return Error.Conflict("shipments.last_order", "A shipment needs at least one order. Cancel the shipment instead.");
        }

        _orders.Remove(link);
        order.Release();
        Resequence();
        return Result.Success();
    }

    /// <summary>Sets the delivery run to follow <paramref name="orderIds"/> in that order. Must list exactly the orders on the shipment.</summary>
    public Result Sequence(IReadOnlyList<Guid> orderIds)
    {
        if (EnsureDraft() is { } locked)
        {
            return locked;
        }

        if (orderIds.Count != _orders.Count || orderIds.Distinct().Count() != orderIds.Count || orderIds.Any(id => _orders.All(o => o.OrderId != id)))
        {
            return Error.Validation("shipments.sequence_invalid", "The drop sequence must list each order on the shipment exactly once.");
        }

        for (var i = 0; i < orderIds.Count; i++)
        {
            _orders.First(o => o.OrderId == orderIds[i]).DropSequence = i + 1;
        }

        return Result.Success();
    }

    /// <summary>Recomputes weight and volume from the orders. Called by the handler whenever the order set changes.</summary>
    public void RecalculateLoad(IReadOnlyCollection<Order> orders)
    {
        var byId = orders.ToDictionary(o => o.Id);
        var links = _orders.OrderBy(o => o.DropSequence).Where(l => byId.ContainsKey(l.OrderId)).ToList();
        var outbound = links.Where(l => !l.IsReturn).Select(l => byId[l.OrderId]).ToList();
        TotalWeightKg = outbound.Sum(o => o.WeightKg);
        // Volume of the orders that declare one; an order with no volume simply does not contribute.
        var withVolume = outbound.Where(o => o.VolumeCbm.HasValue).ToList();
        TotalVolumeCbm = withVolume.Count == 0 ? null : withVolume.Sum(o => o.VolumeCbm!.Value);

        // Walk the run: deliveries unload, return pickups load. The peak is what the vehicle must be able to carry.
        var onboard = TotalWeightKg;
        var peak = onboard;
        foreach (var link in links)
        {
            onboard += link.IsReturn ? byId[link.OrderId].WeightKg : -byId[link.OrderId].WeightKg;
            peak = Math.Max(peak, onboard);
        }

        PeakOnboardKg = peak;
    }

    /// <param name="cheapestTotal">The lowest quote at the time; when the chosen one is dearer a reason must be given.</param>
    public Result Tender(
        Guid transporterId, FreightQuoteResult chosen, decimal? cheapestTotal, string? overrideReason, DateTimeOffset now)
    {
        if (EnsureDraft() is { } locked)
        {
            return locked;
        }

        if (_orders.Count == 0)
        {
            return Error.Validation("shipments.no_orders", "The shipment has no orders.");
        }

        var dearer = cheapestTotal is { } cheapest && chosen.Total > cheapest;
        if (dearer && string.IsNullOrWhiteSpace(overrideReason))
        {
            return Error.Validation("shipments.override_reason_required",
                $"This is not the lowest quote (₹{cheapestTotal:0.00} is available). Say why you are choosing it.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["overrideReason"] = ["Give a reason for not choosing the lowest quote."] },
            };
        }

        TransporterId = transporterId;
        ContractId = chosen.ContractId;
        ContractReference = chosen.ContractReference;
        FreightEstimate = chosen.Total;
        EstimateLines = chosen.Lines;
        OverrideReason = dearer ? overrideReason!.Trim() : null;
        TenderedAt = now;
        Status = ShipmentStatus.Tendered;
        Raise(new ShipmentTendered(Id, TenantId, Number, transporterId, now));
        return Result.Success();
    }

    /// <summary>Opens the shipment to a broadcast tender: locked for editing, visible to every invited transporter.</summary>
    public Result StartBidding(DateTimeOffset now)
    {
        if (EnsureDraft() is { } locked)
        {
            return locked;
        }

        if (_orders.Count == 0)
        {
            return Error.Validation("shipments.no_orders", "The shipment has no orders.");
        }

        TenderedAt = now;
        Status = ShipmentStatus.Bidding;
        return Result.Success();
    }

    /// <summary>A broadcast tender ended without an award (cancelled, or nobody answered): back to Draft.</summary>
    public Result EndBidding()
    {
        if (Status != ShipmentStatus.Bidding)
        {
            return Error.Conflict("shipments.not_bidding", "This shipment is not out to tender.");
        }

        TenderedAt = null;
        Status = ShipmentStatus.Draft;
        return Result.Success();
    }

    /// <summary>
    /// A broadcast tender was awarded: the winning bid's contract, vehicle and driver become the allocation, as if it had been tendered and accepted.
    /// </summary>
    /// <param name="agreedRate">A counter-offer the planner agreed; it replaces the contract price as the estimate.</param>
    public Result Award(Guid transporterId, FreightQuoteResult chosen, decimal? agreedRate, FleetVehicle vehicle, FleetDriver driver, DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Bidding)
        {
            return Error.Conflict("shipments.not_bidding", "This shipment is not out to tender.");
        }

        TransporterId = transporterId;
        var assigned = AssignFleet(vehicle, driver);
        if (assigned.IsFailure)
        {
            TransporterId = null;
            return assigned;
        }

        ContractId = chosen.ContractId;
        ContractReference = chosen.ContractReference;
        FreightEstimate = agreedRate ?? chosen.Total;
        EstimateLines = chosen.Lines;
        OverrideReason = agreedRate is { } rate && rate != chosen.Total ? $"Awarded from a broadcast tender at the agreed rate ₹{rate:0.00} (contract price ₹{chosen.Total:0.00})." : "Awarded from a broadcast tender.";
        AcceptedAt = now;
        Status = ShipmentStatus.Accepted;
        Raise(new ShipmentAccepted(Id, TenantId, Number, transporterId, now));
        return Result.Success();
    }

    /// <summary>Records that the planner agreed the rate the transporter proposed instead of the contract's.</summary>
    public Result ApplyAgreedRate(decimal rate)
    {
        if (Status != ShipmentStatus.Tendered)
        {
            return Error.Conflict("shipments.not_tendered", "This shipment is not waiting for a transporter to respond.");
        }

        OverrideReason = $"Counter-offer agreed at ₹{rate:0.00} (contract price ₹{FreightEstimate:0.00}).";
        FreightEstimate = rate;
        return Result.Success();
    }

    /// <summary>The transporter did not answer before the deadline: the load comes back to Draft so the next one can be tried.</summary>
    public Result ExpireTender(DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Tendered)
        {
            return Error.Conflict("shipments.not_tendered", "This shipment is not waiting for a transporter to respond.");
        }

        RejectionCount++;
        LastRejectionReason = "No response before the deadline.";
        Raise(new ShipmentTenderExpired(Id, TenantId, Number, TransporterId!.Value, now));
        ClearAllocation();
        Status = ShipmentStatus.Draft;
        return Result.Success();
    }

    /// <summary>Takes a tendered shipment back to Draft, e.g. to choose someone else.</summary>
    public Result Withdraw()
    {
        if (Status != ShipmentStatus.Tendered)
        {
            return Error.Conflict("shipments.not_tendered", "Only a tendered shipment can be withdrawn.");
        }

        Raise(new ShipmentTenderWithdrawn(Id, TenantId, Number, TransporterId!.Value, "The offer was withdrawn."));
        ClearAllocation();
        Status = ShipmentStatus.Draft;
        return Result.Success();
    }

    public Result Accept(FleetVehicle vehicle, FleetDriver driver, DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Tendered)
        {
            return Error.Conflict("shipments.not_tendered", "This shipment is not waiting for a transporter to accept it.");
        }

        var assigned = AssignFleet(vehicle, driver);
        if (assigned.IsFailure)
        {
            return assigned;
        }

        AcceptedAt = now;
        Status = ShipmentStatus.Accepted;
        Raise(new ShipmentAccepted(Id, TenantId, Number, TransporterId!.Value, now));
        return Result.Success();
    }

    /// <summary>Swaps the vehicle or driver before the shipment leaves (breakdown, sickness).</summary>
    public Result Reassign(FleetVehicle vehicle, FleetDriver driver)
    {
        if (Status != ShipmentStatus.Accepted)
        {
            return Error.Conflict("shipments.not_accepted", "Only an accepted shipment that has not left can be reassigned.");
        }

        var changed = VehicleId != vehicle.Id;
        var assigned = AssignFleet(vehicle, driver);
        if (assigned.IsSuccess && changed)
        {
            Raise(new ShipmentVehicleReassigned(Id, TenantId, Number, TransporterId!.Value, vehicle.Id, vehicle.RegistrationNumber));
        }

        return assigned;
    }

    /// <summary>Whether a vehicle and driver may carry a load of this weight for the transporter: theirs, active, papers in order, big enough.</summary>
    internal static Result CheckFleet(FleetVehicle vehicle, FleetDriver driver, Guid? transporterId, decimal loadKg)
    {
        if (vehicle.TransporterId != transporterId || driver.TransporterId != transporterId)
        {
            return Error.Forbidden("shipments.fleet_not_theirs", "The vehicle and driver must belong to the transporter this load was tendered to.");
        }

        if (!vehicle.IsActive || !driver.IsActive)
        {
            return Error.Conflict("shipments.fleet_inactive", "An inactive vehicle or driver cannot be assigned.");
        }

        if (vehicle.Compliance == FleetCompliance.NonCompliant)
        {
            return Error.Conflict("shipments.vehicle_non_compliant", $"Vehicle {vehicle.RegistrationNumber} cannot be used: {string.Join("; ", vehicle.Issues)}.");
        }

        if (driver.Compliance == FleetCompliance.NonCompliant)
        {
            return Error.Conflict("shipments.driver_non_compliant", $"{driver.FullName} cannot drive this load: {string.Join("; ", driver.Issues)}.");
        }

        if (vehicle.PayloadKg > 0 && loadKg > vehicle.PayloadKg)
        {
            return Error.Conflict("shipments.overload", $"The load is {loadKg:0.##} kg but {vehicle.RegistrationNumber} can carry {vehicle.PayloadKg} kg.");
        }

        return Result.Success();
    }

    private Result AssignFleet(FleetVehicle vehicle, FleetDriver driver)
    {
        var usable = CheckFleet(vehicle, driver, TransporterId, LoadKg);
        if (usable.IsFailure)
        {
            return usable;
        }

        VehicleId = vehicle.Id;
        VehiclePayloadKg = vehicle.PayloadKg > 0 ? vehicle.PayloadKg : null;
        VehicleRegistration = vehicle.RegistrationNumber;
        DriverId = driver.Id;
        DriverName = driver.FullName;
        DriverPhone = driver.Phone;
        return Result.Success();
    }

    public Result Reject(string reason, DateTimeOffset now = default)
    {
        if (Status != ShipmentStatus.Tendered)
        {
            return Error.Conflict("shipments.not_tendered", "This shipment is not waiting for a transporter to respond.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("shipments.reason_required", "Say why the load is being declined.");
        }

        RejectionCount++;
        LastRejectionReason = reason.Trim();
        Raise(new ShipmentRejected(Id, TenantId, Number, TransporterId!.Value, now, reason.Trim()));
        ClearAllocation();
        Status = ShipmentStatus.Draft;
        return Result.Success();
    }

    /// <param name="lrNumbers">One lorry-receipt number per order, in drop sequence.</param>
    public Result Dispatch(IReadOnlyList<string> lrNumbers, IReadOnlyCollection<Order> orders, DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Accepted)
        {
            return Error.Conflict("shipments.not_accepted", "Only an accepted shipment can be dispatched.");
        }

        var ordered = OrdersInDropSequence;
        if (lrNumbers.Count != ordered.Count)
        {
            return Error.Validation("shipments.lr_count", "A lorry-receipt number is needed for each order.");
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].LrNumber = lrNumbers[i];
        }

        orders.Where(o => _orders.Any(l => l.OrderId == o.Id)).ToList().ForEach(o => o.MarkDispatched());
        DispatchedAt = now;
        Status = ShipmentStatus.Dispatched;
        Raise(new ShipmentDispatched(Id, TenantId, Number, TransporterId!.Value, now));
        return Result.Success();
    }

    /// <summary>Closes the whole run in one step with no per-order detail (a quick close). Orders already delivered keep their details.</summary>
    public Result Deliver(IReadOnlyCollection<Order> orders, DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Dispatched)
        {
            return Error.Conflict("shipments.not_dispatched", "Only a shipment that is on the road can be marked delivered.");
        }

        foreach (var link in _orders.Where(l => !l.IsDelivered))
        {
            var order = orders.FirstOrDefault(o => o.Id == link.OrderId);
            link.MarkDelivered(now, null, order?.Packages, null, null, null);
            order?.MarkDelivered();
        }

        return Complete(now);
    }

    /// <summary>Records one order's delivery with who received it and what arrived. The shipment is delivered when its last order is.</summary>
    public Result RecordDelivery(Order order, DeliveryDetails details, DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Dispatched)
        {
            return Error.Conflict("shipments.not_dispatched", "Deliveries can only be recorded for a shipment that is on the road.");
        }

        var link = _orders.FirstOrDefault(l => l.OrderId == order.Id);
        if (link is null)
        {
            return Error.NotFound("shipments.order_not_found", "That order is not on this shipment.");
        }

        if (link.IsDelivered)
        {
            return Error.Conflict("delivery.already_recorded", "This delivery has already been recorded.");
        }

        if (details.DeliveredAt > now.AddMinutes(5))
        {
            return Error.Validation("delivery.in_future", "A delivery cannot be dated in the future.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["deliveredAt"] = ["Choose a time that has already passed."] },
            };
        }

        if (DispatchedAt is { } left && details.DeliveredAt < left)
        {
            return Error.Validation("delivery.before_dispatch", "A delivery cannot be before the shipment left.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["deliveredAt"] = ["Delivery cannot be earlier than dispatch."] },
            };
        }

        var checkedFigures = DeliveryRules.Check(order.Packages, details);
        if (checkedFigures.IsFailure)
        {
            return checkedFigures.Error;
        }

        var (delivered, damaged, shortage) = checkedFigures.Value;
        link.MarkDelivered(details.DeliveredAt, details.ReceiverName, order.Packages, delivered, damaged, details.Remarks);
        order.MarkDelivered();

        if (link.HasException)
        {
            Raise(new DeliveryExceptionReported(Id, order.Id, TenantId, Number, order.Number, TransporterId!.Value, shortage ?? 0, damaged ?? 0, link.DeliveryRemarks, now));
        }

        return _orders.All(l => l.IsDelivered) ? Complete(now) : Result.Success();
    }

    private Result Complete(DateTimeOffset now)
    {
        DeliveredAt = _orders.Max(l => l.DeliveredAt) ?? now;
        Status = ShipmentStatus.Delivered;
        Raise(new ShipmentDelivered(Id, TenantId, Number, TransporterId!.Value, DeliveredAt.Value));
        return Result.Success();
    }

    public Result ProofAdded(Guid orderId) => Link(orderId, out var link) ?? link!.ProofAdded();

    public Result ProofRemoved(Guid orderId, int remaining) => Link(orderId, out var link) ?? link!.ProofRemoved(remaining);

    public Result VerifyProof(Order order, Guid? userId, DateTimeOffset now)
    {
        if (Link(order.Id, out var link) is { } missing)
        {
            return missing;
        }

        var verified = link!.Verify(userId, now);
        if (verified.IsSuccess)
        {
            Raise(new PodVerified(Id, order.Id, TenantId, Number, order.Number, TransporterId!.Value, now));
        }

        return verified;
    }

    public Result RejectProof(Guid orderId, Guid? userId, string reason, DateTimeOffset now) => Link(orderId, out var link) ?? link!.Reject(userId, reason, now);

    private Error? Link(Guid orderId, out ShipmentOrder? link)
    {
        link = _orders.FirstOrDefault(l => l.OrderId == orderId);
        return link is null ? Error.NotFound("shipments.order_not_found", "That order is not on this shipment.") : null;
    }

    /// <summary>Cancels a shipment that has not left. Its orders go back to Open so they can be planned again.</summary>
    public Result Cancel(string reason, IReadOnlyCollection<Order> orders)
    {
        if (Status is not (ShipmentStatus.Draft or ShipmentStatus.Tendered or ShipmentStatus.Accepted or ShipmentStatus.Bidding))
        {
            return Error.Conflict("shipments.not_cancellable", "A shipment that has left cannot be cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("shipments.reason_required", "Say why the shipment is being cancelled.");
        }

        orders.Where(o => _orders.Any(l => l.OrderId == o.Id)).ToList().ForEach(o => o.Release());
        CancelReason = reason.Trim();
        var offeredTo = Status is ShipmentStatus.Draft or ShipmentStatus.Bidding ? null : TransporterId;
        Status = ShipmentStatus.Cancelled; // the transporter reference is kept so they can see it was cancelled
        if (offeredTo is { } carrier)
        {
            Raise(new ShipmentCancelled(Id, TenantId, Number, carrier, CancelReason));
        }
        return Result.Success();
    }

    /// <summary>Share of the assigned vehicle's weight capacity this load uses, or null before a vehicle is assigned.</summary>
    public decimal? Utilization => VehiclePayloadKg is { } payload ? WeightUtilization(LoadKg, payload) : null;

    /// <summary>Share of a vehicle's weight capacity used, or null when no capacity is known.</summary>
    public static decimal? WeightUtilization(decimal loadKg, int payloadKg) =>
        payloadKg > 0 ? Math.Round(loadKg / payloadKg, 4) : null;

    private void ClearAllocation()
    {
        TransporterId = null;
        ContractId = null;
        ContractReference = null;
        FreightEstimate = null;
        EstimateLines = [];
        OverrideReason = null;
        TenderedAt = null;
        VehicleId = null;
        VehiclePayloadKg = null;
        VehicleRegistration = null;
        DriverId = null;
        DriverName = null;
        DriverPhone = null;
        AcceptedAt = null;
    }

    private void Resequence()
    {
        var i = 1;
        foreach (var link in _orders.OrderBy(o => o.DropSequence))
        {
            link.DropSequence = i++;
        }
    }

    private Error? EnsureDraft() =>
        Status == ShipmentStatus.Draft ? null : Error.Conflict("shipments.not_draft", "Only a draft shipment can be changed. Withdraw it from the transporter (or cancel its tender) first.");
}
