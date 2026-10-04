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

## Tendering to several transporters
Lives in Shipments (`Domain/Tendering.cs`, `Application/Tendering/`), because a tender is about a shipment. A `TenderRound` has `TenderInvitee`s (one
per transporter: order, contract, price at the time, answer, counter-offer, bid) and a timeline of `TenderEvent`s. See `docs/shipments.md`.
- The old branch's modes map as: Direct = the existing single tender; Sequential and Broadcast = `TenderRound`. Counter-offers are kept; so is expiry
  (settled lazily on read) and the response window (per tender, 15 min – 3 days, default 4 h).
- Differences, on purpose: a vendor never sees the contract price (so there is no "offered rate" to match; it proposes its own); accepting in a
  sequential tender is the same step as committing a vehicle and driver; a broadcast tender is awarded by a planner among bids.
- Planning rules apply: barred transporters (suspended, expired papers, restricted) cannot be invited, and are skipped if they become barred mid-tender.
- Performance: an offered load counts as an invitation for each invitee; a refusal or a missed deadline counts against tender acceptance; a load
  closed by the buyer (another carrier awarded, tender cancelled) is not counted either way. The transporter's primary contact is emailed when a load
  is offered, expires or is closed (best-effort; a mail failure never fails the flow).

## Master data and document rules
- **Document rules** (`DocumentRule`, per tenant per kind of paper): mandatory, needs an expiry date, renewal reminder days, expired blocks work,
  checked or not. The defaults reproduce the previous behaviour; the policy is applied to fleet compliance, onboarding readiness and expiry status.
  Replaces the old branch's `tm_document_types`. The kinds themselves are fixed (12 kinds for transporter, vehicle and driver), not user-defined.
- **Transporter types** and **capabilities** (`MasterItem`): built-in lists (the old branch's types and capabilities) plus a tenant's own entries;
  an entry can be renamed or switched off (nobody new gets it; holders keep it). A transporter has an optional `TypeCode`.
- Where the old branch had a table, ours has: `tm_service_types` → the lane/mode (`FreightMode`) on lanes; `tm_onboarding_steps` and
  `tm_transporter_approval_actions` → the approval engine's policies; `tm_transporter_users` → Platform users with a `trn` claim;
  `tm_transporter_audit_logs` → the automatic audit trail; `tm_transporter_rates` → Contracts; `tm_pod_records` → Shipments POD.

## Table mapping (old branch → this system)
| Old table | Here |
|---|---|
| tm_transporters, tm_transporter_types | `transporters_transporters` (+ `type_code`), `transporters_master_items` |
| tm_transporter_vehicles / drivers / documents | `transporters_vehicles` / `drivers` / `documents` (existing) |
| tm_document_types | `transporters_document_rules` |
| tm_capability_types, tm_transporter_capabilities | `transporters_master_items`, `transporters_capabilities` |
| tm_tenders, tm_tender_responses, tm_tender_events | `shipments_tender_rounds`, `shipments_tender_invitees`, `shipments_tender_events` |
| tm_load_executions / events, tm_vehicle_placement_*, tm_claims, tm_load_costs, tm_capacity_days, tm_transporter_alerts | `transporters_load_executions`, `execution_events`, `placements`, `placement_events`, `claims`, `load_costs`, `capacity_days`, `alerts` |
| tm_transporter_performance_kpis / scorecards / rankings | `transporters_performance_kpis`, `scorecards` (rankings computed on demand) |
| tm_transporter_lanes, planning_rules, planning_feedback | `transporters_lanes`, `planning_rules`, `planning_feedback` |
| tm_transporter_contacts / branches | `transporters_contacts` / `branches` |
| (new, no old table) tender invitations seen by performance | `transporters_tender_invitations` |
| tm_configuration_settings | `transporters_settings` |
| tm_pod_records | Shipments POD (kept) |

## Still different from the old branch
- Custom document kinds: not supported (rules configure the fixed kinds).
- Its `frontend/` scaffold and standalone auth are not part of this system; `web/` and Platform are used.
- Notifications are email to the primary contact; there is no in-app inbox, SMS or WhatsApp.
