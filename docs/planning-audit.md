# Planning & load optimisation — audit against the master prompt

First audited on 2026-10-04 against 510 backend and 78 web tests; **re-audited the same day after the fixes and the pending items below were built**
(backend 550 tests, web 86 tests). ✅ done · ◐ partly · ❌ not built · ⇄ built differently on purpose.
Design and behaviour: `docs/planning-module.md`. Performance figures are measured, not assumed.

## Summary

| Area | Result |
|---|---|
| Core decision engine (payload + volume + length, FTL/PTL, consolidation, sequencing, returns, explainability, versioning, commit) | ✅ |
| Manual control, locking, lifecycle | ✅ locks at vehicle / stop-order / vehicle-and-driver / order level; Running status with progress; mandatory override reasons |
| Master data the prompt assumes (priority, handling/hazard, product compatibility, vehicle dimensions, vehicle availability) | ✅ |
| Specific vehicle chosen at planning time | ✅ a real vehicle and driver from the carrier's fleet, never double-booked |
| Endpoints, UI components and KPIs named in the prompt | ✅ except the items under "Still open" |
| Performance target (500 orders) | ✅ measured |

## The four defects found by the first audit — all fixed

1. **Consolidation gave up too early** (500 demo orders → 381 vehicles). Groups are now packed to the largest vehicle that has a rate and, when a group does not pay, it is halved and retried before falling back to single orders; prices are cached per run. Measured on the same data: **~259 vehicles for 385 planned orders** (the rest are held back only because the demo fleet of 36 vehicles runs out — see below).
2. **Part load had no upper bound.** `MaxPtlWeightKg` (default 5,000 kg, configurable per run) is a feasibility cap, not a decision threshold.
3. **Overrides needed no reason.** Every manual change except a plain re-order of stops now requires a comment (API and UI); the FTL/PTL override requires one too, and it is kept with the plan.
4. **Commit lost the estimate.** A committed shipment keeps `PlanReference` and `PlannedCost`; its distance is the plan's.

## Section by section

| § | Requirement | Status | Notes |
|---|---|---|---|
| 1 | Stack, optimizer behind an abstraction | ✅ | Ant Design tables, Recharts, Leaflet/OSM; OpenAPI + Scalar; Serilog; `IPlanningOptimizer` |
| 2 | The ten planning questions | ✅ | #4 "which specific vehicle" is answered by the fleet allocator |
| 2 | Planner in control; every override auditable | ✅ | Reason mandatory; every edit is a new version with its description and comment |
| 3A | Planning date, orders, objective, max stops, consolidation, reverse | ✅ | |
| 3A | Origin/destination filter, optional transporter, optional route in the workbench | ❌ | Orders are chosen by hand; no transporter/route constraint |
| 3A | Per-order priority, product category, handling, hazardous, stackable, longest item | ✅ | Order form and API; urgent orders are planned first |
| 3A | Feasibility on payload, volume | ✅ | Independent hard limits with the exact overflow |
| 3A | Feasibility on length, handling (temperature, hazardous), availability, compatibility | ✅ | `NOT_COMPATIBLE`, `TOO_LONG`, `NO_AVAILABLE_VEHICLE`, `INCOMPATIBLE`. **Route restrictions** ❌ |
| 3A | Output: recommended type, actual vehicle and driver, utilisation (weight, volume, length), distance, duration, cost, why others were rejected | ✅ | Remaining capacity is implied by the fill bars |
| 4 | FTL vs PTL from real prices | ✅ | Not a weight threshold |
| 4 | Accept / Override with mandatory reason, cost-component table | ✅ | Comparison dialog |
| 5 | Vehicle types from the database; length utilisation; availability in ranking | ✅ | Vehicle type dimensions and flags are maintained under Freight network → Vehicle types |
| 6 | Multi-stop routes, road distance, deadlines, windows, pickup-before-delivery, max stops | ✅ | Exact ≤ 8 stops, heuristic above |
| 6 | Multiple pickups and max route duration in a *normal* plan | ❌ | Both exist only in milk runs |
| 6 | Solver statuses | ◐ | `Feasible`, `TimeLimitReached`, `Infeasible`, `Failed` (a background run that crashed) are produced; `Optimized` never is — by design the plan is never called optimal |
| 6 | OR-Tools | ⇄ | Own sequencer |
| 7 | Consolidation | ✅ | Must pay; kept apart by compatibility rules and hazardous goods |
| 8 | Milk run templates, daily recalculation, vehicle not assumed | ✅ | The day's plan names the transporter, vehicle and driver for every trip and every vehicle considered |
| 8 | Milk run committed into trips | ✅ | `POST planning/milk-runs/commit`: one draft shipment per trip; pure collection trips become collection runs (many pickups, one drop) |
| 9 | Reverse pickups with capacity checks at every moment | ✅ | |
| 9 | Return type, reason, priority, pickup window | ✅ | Attributes of a reverse order (mandatory type and reason); pickup window respected when fitting a return. No separate `ReturnRequests` table (⇄) |
| 10 | Weight/volume utilisation, loaded km, **empty km**, cost per trip, per tonne-km, **per tonne, per shipment** | ✅ | Plan page, KPI tab, CSV/Excel/PDF |
| 11 | One Planning Workbench page | ◐ | Same capabilities across tabs and a plan page; not one four-pane screen |
| 11 | Queue search/filter/sort, customer and priority columns | ◐ | Multi-select and paging only |
| 11 | Allocation board shows the vehicle registration | ✅ | On each plan vehicle: registration, driver, phone |
| 11 | Map of the whole plan | ◐ | Map per vehicle |
| 12 | FTL/PTL table by cost component, Accept/Override | ✅ | |
| 13 | Manual edits with instant revalidation | ✅ | Dropdowns/buttons, not drag-and-drop |
| 14 | Locks: allocation, vehicle, stop sequence, route; indicator; optimizer respects locks | ✅ | Four kinds; a re-plan carries over anything locked at any level; edits honour each kind |
| 15 | Versioning | ✅ | |
| 16 | Statuses incl. **Running** | ✅ | `Draft` is not used (a plan is created when planned) |
| 16 | Commit snapshot incl. cost and distance | ✅ | |
| 17 | Unplanned reason + suggestions | ✅ | Added: not compatible, too long, no available vehicle, incompatible products, lock conflict. "No route" and "required type unavailable" ❌ |
| 18 | The 18 tables | ⇄ | Plan vehicles/stops/results live inside the immutable plan snapshot. New tables: compatibility rules. Order and vehicle-type attributes are columns |
| 19 | All named endpoints | ✅ | Added `runs/{id}/vehicles`, `runs/{id}/stops`, `consolidation/preview`, `returns/plan`, `compatibility-rules`, `milk-runs/commit`, PDF export |
| 19 | Log planning execution | ✅ | Structured logs, plus a per-run log (start, steps, duration) saved with the plan and shown on the plan page |
| 20–21 | Optimizer steps; hard constraints | ✅ | Availability and compatibility are hard. Max trip duration (normal plans) and route restrictions ❌ |
| 22–23 | Request fields `allowMultiDrop`, `allowMilkRun`, `maxTripDurationMinutes`, `respectTimeWindows` | ❌ | Equivalent options differ |
| 24 | Business rules 1–15 | ◐ | #15 "no hard-coded values": the ready window (2 days), FTL threshold (0.6), under-utilised (0.5), overdue days (7) and balance weights remain constants |
| 25 | Consistent error format | ◐ | RFC 9457; "no route" not modelled |
| 26 | Frontend components | ◐ | Added OptimizationProgress, manual-override reason dialog, compatibility rules, vehicle-type admin. Absent: ShipmentFilters, VehicleRecommendationList |
| 27 | Optimization progress messages | ✅ | Background runs report each step; the plan page follows them |
| 28 | Dashboard KPIs, date filter, CSV, Excel, **PDF** | ✅ | PDF for a plan and for the dashboard |
| 29 | Tests 1–14 | ✅ | #9 vehicle unavailable now has tests (unit and API) |
| 30 | Permissions, audit | ✅ | |
| 31 | 500 orders | ✅ | **Measured after the changes:** 50 orders 0.6–1.5 s, 150 orders 1.2–3.3 s, 500 orders 3.8 s (budget 20 s). The demo fleet is only 36 vehicles, so for 500 orders ~110 are reported `NO_AVAILABLE_VEHICLE` — that is the allocator refusing to double-book, not a slowdown. 20+ vehicle types not tested |
| 32 | Indexes | ✅ | |
| 35 | Configurable | ◐ | Toll rules and cost weights remain fixed |
| 36–38 | Deliverables, demo data, acceptance | ✅ | The demo script now also seeds priorities, categories, a FOOD/CHEMICALS rule, return types and a truck in maintenance |

## Still open

- Origin/destination/transporter/route filters in the workbench; queue search and filter; one-screen workbench; whole-plan map.
- Multiple pickups and a maximum route duration in a normal plan; route restrictions; "no route" as a reason.
- The remaining hard-coded business constants (§24 #15); toll rules.
- A background run is queued in memory: if the API restarts while a plan is Running, that run stays Running (it can be cancelled). A durable job queue is the next step if planning moves to a separate worker.
- Delivery window and vehicle availability are by day, not by hour.
