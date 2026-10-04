# Contracts and the freight price engine

`Tms.Modules.Contracts` owns freight agreements and is the **single source of freight prices**. Shipment planning,
procurement and bill audit must price through it, never re-implement rates.

## Model

- **Contract**: header (transporter, type, validity, payment terms, estimated annual spend), commercial **terms**
  (volumetric factor, loading/unloading, multi-drop, minimum per consignment, detention), an optional **diesel clause**,
  and **rate cards**. Types: `Ftl` (flat per trip or per km), `Ptl` (weight slabs), `Dedicated` (monthly rental + overage).
- **Rate card**: a lane (`from` → `to`, optionally both ways), an optional vehicle type and distance band, and a **pricing
  shape** (`flatTrip`, `perKm`, `weightSlabs`, `dedicated`; JSON with a `kind` discriminator).
- **Place** = `City` (3) | `Zone` (2) | `State` (1) | `Any` (0). Zones group states/cities per tenant.
- **Diesel prices** per region and date; the clause uses the price in force on the shipment date.

## Which rate wins
Among a contract's rate cards that fit the shipment, the highest score wins:
`(origin specificity + destination specificity) × 100 + 10 if vehicle-specific + 1 if distance-banded`. One rate per
contract is returned, so transporters can be compared. Ties break deterministically by rate id.

## Calculation (all in `FreightCalculator`, pure and unit-tested)
- Per line rounding to paise, half away from zero; **total = exact sum of the lines shown**.
- PTL: chargeable weight = `max(actual, volume × factor, minimum chargeable)`. Slab convention: a weight belongs to a slab
  if it is **above** the lower limit and **up to and including** the upper limit. `Whole` charges all weight at the slab
  rate; `Incremental` charges slice by slice. Then `max(base, card minimum, contract minimum)`.
- FTL per km: `max(distance, minimum km) × rate`, floored by the minimum charge.
- Diesel clause: whole steps only; optional tolerance (dead band), cap, and escalation-only. Applies to base freight only,
  as its own `FUEL` line. No diesel price on file ⇒ no fuel line and an explanatory note (never a failure).
- Dedicated contracts are billed monthly (`CalculateDedicatedMonth`), not per shipment.

## Lifecycle
`Draft → PendingApproval → Active` through the approval engine (`freight_contract`, amount = estimated annual spend).
Approved contracts are **immutable**: change them by **revising** (a draft copy; when approved it takes over on its start
date and the old contract ends the day before, status `Superseded`).

**"In force" is historical.** A shipment dated D is priced by the approved contract whose dates cover D, regardless of what
happened to the contract afterwards (expired, terminated, superseded). Bill audit relies on this.

A nightly job (`ContractLifecycleService`) marks lapsed contracts `Expired` and emails the contract owner at 60/30/15/7 days
(once per threshold).

## For other modules
Call the quote endpoint for now (`POST /api/v1/freight/quote`); when planning needs an in-process call, expose an
`IFreightQuoteService` contract in `Tms.SharedKernel.Contracts` backed by `QuoteHandler` rather than referencing this module.
Contracts are staff-only: vendor-portal users get 403 on every contracts endpoint.

## Known limits / next
- Rates are edited in the UI one at a time and saved as a set. **CSV import/export of rate cards** is the next
  usability step for large matrices (limit: 5,000 rates per contract).
- Detention is stored as terms but only *validated* once bill audit (module 7) exists.
- Distance must be supplied by the caller; route/distance lookup arrives with planning.
