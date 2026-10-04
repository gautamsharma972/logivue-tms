# Shipments & planning (module 1)

Orders are goods waiting to move; a **shipment** is one truck run that carries one or more orders from a single pickup to
one or more drops. The module plans loads, asks a transporter to take them, and records dispatch and delivery.

## Lifecycle

```
Order:     Open ──► Planned ──► Dispatched ──► Delivered        (Cancelled from Open)
Shipment:  Draft ──tender──► Tendered ──accept──► Accepted ──dispatch──► Dispatched ──► Delivered
              ▲                  │ reject/withdraw      │
              └──────────────────┘      cancel (Draft/Tendered/Accepted) releases the orders to Open
```

- **Draft**: planner edits plan (mode, vehicle type, date, distance), removes orders, sets drop sequence.
- **Tender**: the screen shows ranked quotes from `IFreightQuoteService`; the server **re-prices at tender time**. Choosing a
  contract that is not the cheapest requires an override reason (kept on the shipment).
- **Accept** (vendor, or a planner on their behalf): needs a vehicle and driver of that transporter. Blocked when either is
  inactive, has lapsed/missing papers (`IFleetDirectory` → `ComplianceEvaluator`), or the vehicle cannot carry the load.
  Papers expiring soon are allowed (flagged). The vehicle's payload is snapshotted for utilisation.
- **Dispatch**: issues one LR number per order (`LR-000001…`, per tenant, via `ISequenceGenerator`) and raises
  `ShipmentDispatched` (outbox). **Deliver** raises `ShipmentDelivered` — tracking, POD and billing build on these.

## Delivery and proof of delivery (module 6)

Delivery is recorded **per order** (per LR), not only per shipment.

- **Record a delivery** (`POST /shipments/{id}/orders/{orderId}/delivery`): who received it, when, packages received and how many of those
  were damaged. The shipment becomes *Delivered* when its last order is. Rules: only while the shipment is on the road; once per order;
  not in the future and not before dispatch; received ≤ shipped, damaged ≤ received; **shortage or damage needs an explanation**. An
  order with no package count records no quantities. The planner's old "Mark delivered" still closes the whole run in one step (no
  detail) and leaves proof to be collected.
- **Proof** (`…/pod`): PDF, JPG or PNG, checked by content not name, 10 MB, at most 5 per order, stored through `IFileStore`. Allowed only
  after delivery. States: *Awaiting → Uploaded → Verified* or *Rejected* (with a reason the transporter sees; a new upload clears it).
  Removing the last file makes proof outstanding again; **verified proof is final** (no upload, no removal).
- **Who**: the transporter (`shipments.respond`) records deliveries and uploads proof for its own shipments (another company's ids return
  404); planners may do it on their behalf; **only staff with `shipments.pod.verify` verify or reject**, so a vendor never approves its own
  paperwork.
- **Events**: `PodVerified` (billing will release payment on it) and `DeliveryExceptionReported` with shortage, damage and remarks
  (claims will start from it), both through the outbox. `ShipmentDelivered` is raised once, when the last order is delivered.
- **Worklist and ageing** (`GET /pod`, `GET /pod/ageing`; UI: POD & deliveries / Deliveries): every line with its stage (on the road,
  proof awaited, to check, rejected, verified), age in days, shortage and damage; staff also get buckets (0–3, 4–7, 8–15, 16–30, 30+),
  overdue (7+ days by default) and the slowest transporters. Ageing is a staff report; a vendor sees only its own lines.
- **Not built yet**: OCR of proofs, e-POD by SMS/OTP or driver app, correcting a delivery once recorded, partial re-delivery of a shortage,
  and automatic reminders. The claim itself is module 8.

## Planning rules (pure domain code, unit tested)

| Rule | Behaviour |
|---|---|
| `VehicleSizer` | Smallest active vehicle type that fits weight and volume; otherwise says how many of the largest are needed. |
| `ModeAdvisor` | Cheaper of best FTL and best PTL quote; on a tie prefers FTL only if the truck would be ≥ 50 % full. The advisor prices every vehicle that fits, since a rate may exist only for a bigger truck. |
| `ConsolidationPlanner` | Open forward orders → loads: same pickup, ready within 2 days, same drop state, first-fit-decreasing into the largest vehicle. A load under 60 % of the best-fitting truck is suggested as PTL. Reverse orders going the opposite way are offered as backhaul. Suggests only; a planner decides. |

## Access

- Staff: `shipments.read` (view), `shipments.plan` (everything else).
- Vendor portal: `shipments.respond` (the only external-allowed permission). A vendor sees **only** shipments tendered to
  their transporter (never Draft), others return 404, and the freight estimate, contract reference, override reason and
  rejection reason are omitted from every response and screen.

## API (all under `/api/v1`)

`orders` (list/create/get/update/cancel) · `planning/advice` · `planning/suggestions` · `planning/utilization` ·
`planning/vehicle-types` · `shipments` (list/create/get) · `shipments/{id}/plan|orders|sequence|quotes|tender|withdraw|
fleet-options|accept|reject|reassign|dispatch|deliver|cancel`.

## Known limits / next

- One pickup per shipment, except a **collection run** (made by committing a milk run): several pickups of return orders delivered to one place, priced from the farthest pickup.
- The quote lane runs to the last drop; extra drops are charged per the contract's multi-drop term.
- No UI yet to add orders to an existing draft (create a new shipment, or remove/recreate).
- Detention and POD-based bill checks arrive with delivery (6) and billing (7).
