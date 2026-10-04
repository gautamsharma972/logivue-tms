# Planning & load optimisation — analysis and status

Source: the "Planning & Load Optimisation master prompt". This page records how it maps onto the existing codebase, what
was built (Phase 1), where we deliberately differ, and what is deferred. The decision engine lives in
`Tms.Modules.Shipments` (see also `docs/shipments.md`).

## Decisions that differ from the prompt (and why)

| Prompt asks | We did | Reason |
|---|---|---|
| New `TransportOrders`, `Vehicles`, `VehicleTypes`, `Transporters`, `FreightRates`, `Locations` tables | **Reuse** existing Orders, Vehicle types / fleet (Transporters module), Contracts rate cards + price engine | Duplicating masters would fork the source of truth. Planning prices **only** through `IFreightQuoteService` and reads fleet through `IFleetDirectory`. |
| Controllers, long/int ids, `PLN-…` in request examples | Minimal APIs, Guid v7 ids, Result→RFC 9457 errors (existing standard) | Consistency; `traceId` and a stable `code` are already in every error. |
| Separate `PlanVehicles/PlanStops/PlanAllocations/PlanningResults/PlanningConstraints` tables | One `shipments_planning_runs` row per **version**, with options, order ids and the **plan snapshot** as JSON | The plan is an immutable snapshot (prices, capacities, reasons as they were). Fewer joins, history cannot drift. Queryable fields (number, version, status, date) are real columns. |
| Planning in a new module | Inside Shipments | It needs orders, shipments and the consolidation/sizing rules already there; commit creates shipments in one transaction without cross-module calls. |
| "Optimal" solver wording | Status `Feasible`; `Optimized` is reserved for a solver that can prove it | A rule-based constructive heuristic must not be labelled optimal. |

## Built (Phase 1)

- **Planning runs**: create, list, get, versions, re-optimise (new version + reason), lock/unlock vehicle, approve (separate
  `shipments.approve` permission), commit, cancel. Statuses: Completed, PartiallyPlanned, Infeasible, Approved, Committed, Cancelled.
- **Never overwrites history**: every re-plan is a new version under the same `PLN-yyyyMMdd-nnn`; older versions are read-only.
- **Hard constraints** enforced server-side: payload and volume checked independently (`VehicleEvaluator`), max stops (groups are
  split), locked vehicles kept as-is, only *Open* orders can be planned, committed orders cannot be planned again.
- **Vehicle recommendation** (`GET /planning/vehicles/recommendations`): every vehicle type with accepted/rejected and the exact
  overflow ("Payload exceeded by 1,000 kg"), weight and volume utilisation reported separately (never merged into one score).
- **FTL vs PTL** (`POST /planning/ftl-ptl/compare`, and inside every run): prices each fitting vehicle type as FTL and the load as PTL
  from real contracts; explains the choice; lists rejected alternatives with a reason. Not a weight threshold.
- **Objectives**: MinimizeTotalCost, MinimizeVehicles, MaximizeUtilisation, BalanceCostAndUtilisation (normalised), MinimizeDistance
  (currently = cost until routing exists; labelled so in the UI).
- **Consolidation** with measured saving (cost of the orders moving separately minus the consolidated cost).
- **Unplanned orders** always carry a code, a reason and suggested actions; nothing is dropped silently; a time limit returns the
  best plan so far with `TimeLimitReached`.
- **Audit**: all writes (run create, version, lock, approve, commit, cancel) go through the automatic audit trail.
- **UI**: Planning → Workbench (select orders, rules, run, compare), Plans (history), plan review page (explanations, utilisation,
  alternatives, unplanned reasons, lock, re-plan, approve, commit, version list).
- **Tests**: evaluator + optimizer unit tests (payload/volume overflow, FTL vs PTL, consolidation, max stops, locks, no rate, no
  vehicle, timeout, objectives), integration tests (lifecycle, permissions, tenant isolation, versioning, commit→shipments, snapshot
  immutability when contracts change).

## Deferred (needs inputs we do not have, or is a later phase)

| Prompt section | Status | What it needs |
|---|---|---|
| §6 Road distance/time, stop ETAs | ✅ Stage 1 | Locations master + `IRoutingProvider` (OSRM or labelled estimate). |
| §6 Stop sequencing, delivery windows, capacity at every moment | ✅ Stage 2 | `StopSequencer`; exact ≤ 8 stops, local search above. Multi-pickup routes are Stage 3. |
| §4 SLA / transit-time feasibility | ✅ Stage 1 | Derived from route duration (+ configurable part-load extra); deliver-by dates are hard constraints. Lane-specific transit tables could refine it later. |
| §7 Multi-drop consolidation economics | ✅ Stage 2 | Compared with separate trips; saving %, extra km and minutes; split when it does not pay. |
| §8 Milk-run templates | ✅ Stage 3 · committed to shipments in Stage 4 | Locations master + routing. |
| §9 Reverse-logistics pickups added to a forward route | ✅ Stage 2 | Capacity at every moment, deadlines, detour limit; return charge % is an assumption you set. |
| §13 Manual moves with instant re-validation | ✅ Stage 2 | Move/remove/add/reorder/change type via dropdowns and buttons (no drag-and-drop). |
| §10 Loaded km, empty km, cost per tonne-km / tonne / shipment | ✅ Stage 1 and 4 | Empty km is measured leg by leg (goods on board or not). |
| §28 Dashboard KPIs, Excel/CSV/PDF export | ✅ Stage 3 and 4 | Date-filtered dashboard; CSV, Excel and PDF for a plan and the dashboard. |
| §11 Map view | ◐ Stage 1 | Per-vehicle route map done; whole-plan map and road geometry later. |
| Specific vehicle allocation at plan time | ✅ Stage 4 | The plan names a real vehicle and driver from the carrier's fleet (never double-booked); the transporter still confirms or changes them at accept (compliance-gated). |
| Background worker for large runs | ✅ Stage 4 | `background: true` runs are queued, show progress and can be cancelled; synchronous runs remain for API callers. The queue is in memory. |
| §30 Planner / Supervisor / Admin | ✅ | `shipments.plan` (plan), `shipments.approve` (approve/commit), admin via roles. |

## Stage 1 additions: locations, road distance, transit and deadlines

- **Locations master** (`/locations`, Logistics → Locations): code, name, type, address and coordinates, maintained by planners.
  Coordinates must fall inside India (catches swapped lat/long). Orders can link a pickup and delivery location; the server then
  fills the order's address from it, so address and coordinates never disagree. Orders without locations still plan (no distance).
- **Routing** (`IRoutingProvider`): `ResilientRoutingProvider` uses your OSRM server when `Routing:OsrmBaseUrl` is set and reachable
  (results cached 24 h), otherwise a labelled **estimate** (straight line × `CircuityFactor` at `AverageSpeedKmh`). Every plan
  vehicle shows its source; an estimate is never presented as road distance. The distance now feeds the price engine, so per-km
  contracts price correctly.
- **Stops and ETAs**: pickup then drops in order, each with arrival/departure from `DepartureHour`, driving time and
  `StopServiceMinutes`. Map on the plan page (Leaflet + OpenStreetMap tiles; straight lines between stops, not road geometry).
- **Deadlines (hard constraint, switchable)**: an option whose arrival is after the order's deliver-by date is rejected with the
  arrival time in the reason. Part load adds `PtlExtraTransitHours` (default 24, an assumption you can change) for terminal handling.
  If nothing can make it, the order is unplanned as `DEADLINE_IMPOSSIBLE` with the earliest possible arrival.
- **KPIs**: total distance and cost per tonne-km (loaded km only; empty return km needs return-trip planning, Stage 2).

### Running OSRM (open source, self-hosted) — not tested here (no Docker on the dev machine)

```bash
# one-off: download an India extract and prepare it (needs several GB of RAM/disk)
wget https://download.geofabrik.de/asia/india-latest.osm.pbf -P osrm-data
docker run -t -v "$PWD/osrm-data:/data" osrm/osrm-backend osrm-extract -p /opt/car.lua /data/india-latest.osm.pbf
docker run -t -v "$PWD/osrm-data:/data" osrm/osrm-backend osrm-partition /data/india-latest.osrm
docker run -t -v "$PWD/osrm-data:/data" osrm/osrm-backend osrm-customize /data/india-latest.osrm
# serve
docker run -d -p 5001:5000 -v "$PWD/osrm-data:/data" osrm/osrm-backend osrm-routed --algorithm mld /data/india-latest.osrm
```

Then set `Routing__OsrmBaseUrl=http://localhost:5001`. The car profile is a stand-in for trucks; a custom truck profile
(speeds, restrictions) can be supplied to `osrm-extract` later. Do not point production at the public demo server.

## Stage 2 additions: sequencing, windows, consolidation economics, returns, manual edits

- **Stop sequencing** (`StopSequencer`, pure domain code): chooses the visiting order of a truck's stops for the shortest total
  distance from a pairwise distance matrix (`IRoutingProvider.GetMatrixAsync`; OSRM `table` service, or the labelled estimate).
  Exact (branch and bound over every order) up to 8 stops; nearest neighbour + 2-opt + relocate beyond that. The plan says which
  it used ("best of all orders" vs "improved heuristic"); a heuristic result is never called optimal.
- **Delivery windows** (per order, optional, India time): the truck waits if it arrives early, and if it arrives after closing the
  delivery moves to the next morning. Waits show on the stop and push every later ETA. A deliver-by date makes the window's closing
  time (or the end of that day) a hard deadline.
- **Consolidation must pay**: a consolidated group is compared with each order moving alone. If separate trips are cheaper, or the
  orders cannot move together (no rate, a deadline), they are planned separately and the reason is shown. A consolidated trip reports
  the separate-trip cost, saving %, and the extra km/minutes against its longest single trip.
- **Return pickups** (reverse orders) are fitted onto forward full trucks that start where the return is delivered. A fit must keep
  the truck within payload and volume **at every moment** (onboard weight goes up at a return pickup), meet deadlines, and stay within
  the allowed detour (default 100 km). By default every delivery finishes before any return is collected.
  Pricing is an **assumption you set**: the return load is charged at `BackhaulChargePercent` (default 50 %) of a standalone return
  trip (priced from your contracts); the saving against a separate vehicle is shown. A return with no rate for its own lane is left
  to the standalone path, which says `NO_VALID_RATE`.
- **Committing a plan with returns** creates one shipment: the return order rides as a *return leg* (`ShipmentOrder.IsReturn`);
  the shipment's weight is the outbound load and `PeakOnboardKg` is what the vehicle must carry at any moment. The vendor's vehicle
  is checked against the peak. The price is quoted on the outbound lane only.
- **Manual edits** (`POST /planning/runs/{id}/edit`): move an order to another truck or a new one, remove it, add an unplanned
  order, reorder stops, change vehicle type. Only the touched vehicles are rebuilt and **re-validated by the same optimizer**
  (capacity, windows, deadlines, return fit). A change that breaks a rule is refused with the exact reason and nothing is saved;
  a valid one becomes a new version (the previous version is kept). Locked vehicles cannot be edited; approved/committed plans are
  frozen; return pickups stay with their truck (remove one to place it elsewhere).

### Still open (after Stage 3; see the next section for what has since been built)

- ~~Committing a **milk run** into operational shipments~~ — built, see below.
- Milk runs are previews until committed: approval of a day's trips is not built.
- Stop-order search is per truck; it does not move orders between trucks to shorten total distance (the "shortest distance"
  objective therefore ranks by cost for now).
- Empty (return) kilometres for plain forward trips without a return pickup are not counted.
- OSRM's table service is capped by the server (`--max-table-size`, default 100 points).

## Stage 3 additions: milk runs, KPI dashboard, exports, demo data

- **Milk-run templates** (`/planning/milk-runs`, Planning → Milk runs): a depot, an ordered list of pickup and delivery stops (each with
  service minutes and an optional opening window), departure time, weekdays, a usual vehicle, a stop limit and a duration limit.
  A *pickup* stop collects orders from that location to the depot; a *delivery* stop takes orders from the depot to that location.
  Orders are found for a stop when they are **linked to its location**.
- **Planning a day** (`POST /planning/milk-runs/preview`, nothing is saved): takes the open orders ready by that date that touch the
  stops and recalculates everything from them:
  - stops with nothing to do are skipped; weight, volume, quantity, route and ETAs come from the orders that exist;
  - the **vehicle follows the load and price**, not yesterday's choice (the usual vehicle only breaks a tie, and the plan says why it
    was not used); a vehicle's capacity is checked against the peak load on board at any moment (deliveries first, then pickups);
  - a day too big for the largest vehicle, the stop limit or the duration limit is split into trips;
  - the planned order is kept by default (suppliers often have fixed slots); the plan shows how many km the shortest order would save;
  - pricing: an FTL trip priced on the lane to the farthest stop (stop → depot for a collection run), with the route's distance and stop
    count; no rate means the trip is still planned and its cost is shown as unknown with a warning.
  A milk run is **not committed into shipments** yet: a shipment has one origin, so multi-pickup shipments are not supported.
- **KPI dashboard** (`GET /planning/dashboard`, Planning → KPIs): the newest version of each plan in a date range (cancelled plans
  excluded, at most 366 days): orders total/planned/unplanned, vehicles, freight, consolidation and return-load savings, weight and
  volume fill (separate averages), distance, cost per tonne-km, stops per vehicle, FTL/PTL %, consolidated trips %, return pickups %, a
  daily series, unplanned reasons, spend by transporter and vehicle mix. All figures come from the stored plan snapshots.
  *Empty (deadhead) kilometres are not reported:* they need the return leg of every trip.
- **Exports**: a plan (`/planning/runs/{id}/export?format=csv|xlsx`) and the dashboard (`/planning/dashboard/export`). The Excel file has
  Summary, Vehicles, Orders, Stops and Unplanned sheets. Text that a spreadsheet could read as a formula is neutralised. **PDF is not
  offered**; Excel and CSV cover reporting, and a PDF would need a layout engine we have not justified yet.
- **Demo data**: `node tools/seed-planning-demo.mjs` (dev tenants only, uses the public API, safe to re-run). It creates 16 locations with
  real coordinates, two carriers with compliant fleets (36 vehicles, 12 drivers; one 32 ft truck is in maintenance for three days), FTL contracts from both carriers (one cheaper on the big
  truck) and a PTL contract with weight slabs, 37 orders and a milk run. The orders include small loads on the same corridor (they
  consolidate), a 12.5-tonne load (needs the big truck), part-load sized orders, returns from customers the trucks already visit,
  delivery windows, deadlines, and three that cannot be planned on purpose: no rate to Madhya Pradesh, no rate to Delhi (and due today),
  and 32 tonnes (heavier than any vehicle).

### Fixed along the way

- A shipment larger than any vehicle was planned as part load (priced at ₹2.24 lakh) because a part-load rate existed. Part load is now
  offered only when some vehicle could carry the shipment.

## Stage 4 additions: master data, real vehicles, locks, background runs, reporting

**Master data**
- **Orders** carry a priority (Low … Urgent: urgent orders are planned first), a free-text product category, handling (standard /
  fragile / temperature controlled), hazardous and stackable flags and the longest item. **Return (reverse) orders** must give a
  return type (customer return, damaged, rejected, empties, supplier return, replacement pickup) and a reason, and may give a pickup window.
- **Vehicle types** carry length, width, height, "may carry hazardous goods" and "temperature controlled" (maintained under Freight
  network → Vehicle types). A vehicle that cannot carry the load is rejected with the reason (`NOT_COMPATIBLE`, `TOO_LONG`) and the
  alternatives list shows length utilisation next to weight and volume.
- **Vehicles** have an availability: Available, In maintenance or Off the road, with an optional date window and note. An unavailable
  vehicle is usable again from its "available from" date. This is checked by day.
- **Compatibility rules** (Planning → Rules): pairs of product categories that never share a vehicle. Hazardous goods are also kept
  apart from other goods unless that is switched off for the run.

**A real vehicle and driver** (`FleetAllocator`): for a full-truck trip the planner picks an actual registered vehicle of the chosen type
and a driver from the carrier's fleet — in service that day, papers not invalid, not already on another trip of the same plan. Two
trips never share a vehicle or driver. If the cheapest option's vehicles are all taken, the next-best free option is used; if none is free
the order is unplanned as `NO_AVAILABLE_VEHICLE` with the reason for each vehicle ("MH12AB1234 is in maintenance until 07 Oct"). The
run option `RequireAvailableVehicle` (default on) can be switched off when the fleet list is incomplete; the trip is then planned without
a vehicle. The plan, the alternatives and the milk-run day all show the **transporter** (contact, phone, email), **vehicle**
(registration, payload, paper status) and **driver** (phone, licence, paper status). The price of a return trip used to measure a
backhaul saving is only a reference and does not need a free vehicle.

**Locks** (`POST planning/runs/{id}/lock` with `kind`): *Vehicle* freezes the whole trip; *Sequence* keeps the stop order while orders
may still be added or removed; *Assignment* keeps the vehicle and driver; *Order* keeps one order on its vehicle. A re-plan carries over
anything locked at any level unchanged; manual edits honour each kind and say which lock refused them.

**Manual changes need a reason** (all except a plain re-order of stops). The comparison dialog's **Accept** follows the recommendation;
**Override** picks another mode and requires a reason. Either is stored on the plan (`Reason`).

**Background runs and the planning log**: `POST planning/runs` with `background: true` returns the run at once as **Running**; a
background worker calculates it as the requesting tenant and user, saving each step to the run's log, and the plan page follows it
(polling) and can cancel it. Every run — background or not — stores a log (start, steps, duration) and writes structured log lines.
The queue is in memory: if the API restarts while a plan is Running, that run stays Running and can be cancelled.

**Reporting**: loaded and empty kilometres (leg by leg, goods on board or not), empty-km %, cost per tonne and cost per shipment join cost
per tonne-km, on the plan page, the KPI tab and every export. **PDF** export for a plan and for the dashboard (a small built-in writer,
A4 landscape, standard fonts; the ₹ sign is printed as "Rs"). New read endpoints: `runs/{id}/vehicles`, `runs/{id}/stops`,
`consolidation/preview` (what would share a vehicle and what that saves, nothing saved) and `returns/plan` (return trips versus returns
riding back with a forward truck).

**Milk runs become shipments** (`POST planning/milk-runs/commit`): the day is re-planned from the orders that are open now and each trip
becomes a draft shipment carrying the plan reference and the planned cost. A delivery trip is an ordinary shipment; a trip that also
collects carries the collections as returns; a trip that only collects becomes a **collection run** — many pickups, one drop point — priced from
the farthest pickup, with the other pickups charged as extra stops, exactly as the plan priced it. A second commit finds nothing left.

## How a run works

1. Load the chosen **open** orders; reject unknown or non-open ones.
2. Group: consolidate (same pickup, ready within 2 days, same drop state, first-fit-decreasing) or one group per order; reverse
   orders are their own groups; split groups above *max stops*.
3. For each group: evaluate every vehicle type (weight and volume), price each feasible type as FTL and the group as PTL via the
   contract engine, choose by objective, record every alternative with its verdict, compute consolidation saving.
4. Orders that cannot be planned get a coded reason and suggestions. Summary and solver status are computed; the snapshot is stored.
5. Planner reviews → optional lock/re-plan (new version) → approve (needs `shipments.approve`) → commit creates **Draft shipments**
   (one per planned vehicle) atomically; the planner then tenders them as usual.
