# Transporter management (performance, selection, operations)

Ported from the separate `origin/transporter-management` branch (the `LogiVue.Tms` codebase) into this modular
monolith. That branch shares no history with `main` and used a different model (controllers, long ids, no tenancy),
so it was ported capability by capability, not merged textually. The branch is recorded as merged
(`-s ours`) so its history is reachable; its code is not part of the tree.

## How the data flows
Transporters never references Shipments. Facts arrive two ways, both through `Tms.SharedKernel.Contracts`:
- **Events** (`ShipmentTendered/Accepted/Rejected/Dispatched/Delivered/Cancelled/VehicleReassigned`,
  `DeliveryExceptionReported`) handled by `Integration/OperationsSubscribers.cs`. Subscribers are idempotent.
  Accepting a load creates its execution record and placement.
- **Pull feed** `IShipmentOperationsFeed` (read-only shipment facts) for recalculation and benchmarks.
- **Policy** `ITransporterPlanningPolicy` goes the other way: Shipments planning asks which carriers are barred or
  preferred. Barred carriers are excluded (`UnplannedCodes.TransporterRestricted`); preferred wins ties.

## Capabilities
| Area | What it does |
|---|---|
| Performance | Eight KPIs (on-time pickup/delivery, vehicle placement, tender acceptance, POD on time, claims rate, cost performance, availability), stored by month with numerator/denominator. |
| Scorecards | Generated per period, immutable, keep the weights used. Rankings and benchmark (lane, region, same services, best). |
| Selection | Eligibility (capability, lane, status, rules) and a scored recommendation with reasons. |
| Operations | Executions (arrival/loading times, delay reasons), placements, claims, invoiced costs, daily vehicle capacity, alerts. |
| Coverage | Lanes served with committed transit, capabilities (e.g. hazardous), planning rules (prefer / bar, always with a reason). |
| Contacts, branches | Per-transporter contacts and branches. |

## KPI rules (deliberate)
- A delay counts against the carrier only when attributed to the carrier; "unattributed" late events are excluded until
  a person gives a reason.
- No planned time means not measurable (never scored as good or bad).
- Months are pooled by numerator/denominator. Fewer than the minimum sample (20) leaves a KPI out and renormalises the
  remaining weights.
- Claims rate is lower-is-better. Planned times derive from planned pickup date / deliver-by plus a configurable time of
  day (default 20:00 IST).
- Alerts are evaluated lazily when the alert list is read (there is no cross-tenant worker).
- Per-tenant settings (JSON rows) override code defaults (`SettingDefaults`).

## Permissions
`transporters.performance.read`, `.manage`, `.select` (staff) and `transporters.performance.self`
(`ExternalAllowed`: a vendor sees only its own figures; other companies' ids answer 404).

## Not ported (still only on the old branch)
- Multi-carrier tendering modes (direct / sequential / broadcast), counter-offers, tender expiry worker, vendor-response SLA.
- The branch's POD workflow (main's Shipments POD is kept).
- Document-type configuration (mandatory / renewal / block-allocation), notifications, transporter types/users.
- The branch's `frontend/` scaffold (main's `web/` is kept) and its compliance evaluation worker.
