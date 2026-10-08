# KPI definitions

Every KPI is defined once, in `Tms.Modules.Reports/Domain/Kpi/KpiCatalogue.cs`, and calculated by one engine. Reports, dashboards and the KPI API all ask for a KPI by code; none re-derives it.
This page is generated from the definitions the API serves (`GET /api/v1/kpis`), so it cannot drift from the code. A test checks that every KPI in the catalogue is described here.

## How a KPI is calculated

- Each KPI returns its **numerator** and **denominator**, not only the result. The value is derived from them: `Percent` = numerator ÷ denominator × 100; `Average` = numerator ÷ denominator; `Sum` and `Count` = numerator.
- A KPI is **Not measurable** (no value) when its denominator is zero, never zero. Facts that cannot be judged (no planned delivery time, proof not required, proof not yet due) are left out and counted as *excluded*; they are never counted as failures.
- A ratio over a group of rows (a carrier, a lane, a month) is re-derived from the summed numerators and denominators, never averaged from the rows' percentages.
- Every result carries the **calculation version**. A change to a formula raises the version; the kept daily values record the version they were calculated with and are not read if it differs.
- Comparison: every card is calculated for the period, the previous period of equal length (or the previous month/quarter/year for those periods) and the same period last year. A comparison period with nothing to judge is shown as no comparison, not 0.
- Period dates, filters and the caller's limits (a transporter sees only its own company; a user limited to customers or regions sees only those) are applied to the facts before any KPI is calculated, so every KPI obeys them.
- Thresholds are organisation settings, not code: OTP/OTD grace minutes, the ETA tolerance, ageing buckets, working days and holidays (Report settings).

Source of truth: Reports shows what the owning module decided and recalculates none of its rules. Transporter score, a delivery's on-time judgement, a proof's SLA test, tracking health and ETA, and kept contract freight all come from the module that owns them.

## Deliveries

### Claims rate (`CLAIMS_RATE`)

Share of completed deliveries that led to a claim.

| | |
|---|---|
| **Formula** | Deliveries with a claim ÷ completed deliveries × 100 |
| **Numerator** | Distinct deliveries with a shortage or damage that has a claim reference |
| **Denominator** | Deliveries completed (fully or partly) |
| **Aggregation** | Percent (Percent) |
| **Better when** | lower |
| **Source of truth** | POD & Delivery (discrepancies handed to claims) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Damage rate (`DAMAGE_PCT`)

Share of completed deliveries with a damage recorded.

| | |
|---|---|
| **Formula** | Deliveries with a damage ÷ completed deliveries × 100 |
| **Numerator** | Distinct deliveries with a damage |
| **Denominator** | Deliveries completed (fully or partly) |
| **Aggregation** | Percent (Percent) |
| **Better when** | lower |
| **Source of truth** | POD & Delivery |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### On-time delivery (OTD) (`OTD`)

Share of completed deliveries made by their promised time.

| | |
|---|---|
| **Formula** | On-time deliveries ÷ deliveries that could be judged × 100 |
| **Numerator** | Eligible deliveries completed on or before the promised time |
| **Denominator** | Eligible deliveries with a valid planned delivery time |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | POD & Delivery (its own on-time judgement) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### POD compliance (`POD_COMPLIANCE`)

Share of required proofs of delivery submitted within the SLA. Deliveries that need no proof are left out (not applicable), never counted as zero.

| | |
|---|---|
| **Formula** | Submitted within SLA ÷ proofs required (and judgeable) × 100 |
| **Numerator** | Required proofs submitted within SLA |
| **Denominator** | Required proofs whose timeliness can be judged |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | POD & Delivery (submission within its SLA) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Shortage rate (`SHORTAGE_PCT`)

Share of completed deliveries with a shortage recorded.

| | |
|---|---|
| **Formula** | Deliveries with a shortage ÷ completed deliveries × 100 |
| **Numerator** | Distinct deliveries with a shortage |
| **Denominator** | Deliveries completed (fully or partly) |
| **Aggregation** | Percent (Percent) |
| **Better when** | lower |
| **Source of truth** | POD & Delivery |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

## Freight Contracts

### Average cost per shipment (`COST_PER_SHIPMENT`)

Contract freight divided by the shipments that have one.

| | |
|---|---|
| **Formula** | Total freight ÷ shipments with a freight |
| **Numerator** | Sum of contract freight |
| **Denominator** | Shipments with a contract freight |
| **Aggregation** | Average (Currency) |
| **Better when** | lower |
| **Source of truth** | Freight Contracts |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Cost per ton (`COST_PER_TON`)

Contract freight divided by the tons moved on the shipments that have a freight.

| | |
|---|---|
| **Formula** | Total freight ÷ total tons |
| **Numerator** | Sum of contract freight |
| **Denominator** | Sum of tons (weight ÷ 1000) of the same shipments |
| **Aggregation** | Average (Currency) |
| **Better when** | lower |
| **Source of truth** | Freight Contracts and Shipments (weight) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Cost per ton-km (`COST_PER_TON_KM`)

Contract freight divided by the ton-kilometres of the shipments that have a freight and a distance.

| | |
|---|---|
| **Formula** | Total freight ÷ Σ(tons × km) |
| **Numerator** | Sum of contract freight |
| **Denominator** | Σ (tons × distance km) of the same shipments |
| **Aggregation** | Average (Currency) |
| **Better when** | lower |
| **Source of truth** | Freight Contracts, Shipments (weight and distance) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Loads without an applicable rate (`LOADS_WITHOUT_RATE`)

Loads on lanes where no contract rate applies.

| | |
|---|---|
| **Formula** | Σ loads without a rate |
| **Numerator** | Loads with no applicable rate |
| **Denominator** | Loads on covered or uncovered lanes |
| **Aggregation** | Count (Count) |
| **Better when** | lower |
| **Source of truth** | Freight Contracts (rate coverage) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Total freight spend (`FREIGHT_SPEND`)

The contractual freight kept against shipments in the period.

| | |
|---|---|
| **Formula** | Sum of contract freight |
| **Numerator** | Sum of the contract freight of shipments that have one |
| **Denominator** | Number of shipments that have a contract freight |
| **Aggregation** | Sum (Currency) |
| **Better when** | lower |
| **Source of truth** | Freight Contracts (the contract freight kept against each shipment) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

## Planning

### Consolidation savings (`CONSOLIDATION_SAVINGS`)

What putting orders on shared trips saved compared with sending them separately.

| | |
|---|---|
| **Formula** | Σ (cost if separate − cost of the consolidated trip) |
| **Numerator** | Σ savings of consolidated trips |
| **Denominator** | Consolidated trips with a separate-cost estimate |
| **Aggregation** | Sum (Currency) |
| **Better when** | higher |
| **Source of truth** | Planning (cost if shipped separately) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Dedicated share (`DEDICATED_PCT`)

Dedicated shipments as a share of all shipments.

| | |
|---|---|
| **Formula** | Dedicated shipments ÷ shipments × 100 |
| **Numerator** | Dedicated shipments |
| **Denominator** | All shipments in the period (cancelled excluded) |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Shipments |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### FTL share (`FTL_PCT`)

FTL shipments as a share of all shipments.

| | |
|---|---|
| **Formula** | FTL shipments ÷ shipments × 100 |
| **Numerator** | FTL shipments |
| **Denominator** | All shipments in the period (cancelled excluded) |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Shipments |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Planning savings (`PLANNING_SAVINGS`)

What the planner's optimisation saved against its own baseline, as calculated by each planning run.

| | |
|---|---|
| **Formula** | Σ planning run savings |
| **Numerator** | Σ savings of runs that report one |
| **Denominator** | Runs that report a saving |
| **Aggregation** | Sum (Currency) |
| **Better when** | higher |
| **Source of truth** | Planning (run savings) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### PTL share (`PTL_PCT`)

PTL shipments as a share of all shipments.

| | |
|---|---|
| **Formula** | PTL shipments ÷ shipments × 100 |
| **Numerator** | PTL shipments |
| **Denominator** | All shipments in the period (cancelled excluded) |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Shipments |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Total shipments (`SHIPMENTS`)

Shipments planned to be picked up in the period, cancelled ones excluded.

| | |
|---|---|
| **Formula** | Count of shipments |
| **Numerator** | Shipments in the period that are not cancelled |
| **Denominator** | The same shipments (a count has no separate denominator; zero means there are none to judge) |
| **Aggregation** | Count (Count) |
| **Better when** | higher |
| **Source of truth** | Shipments |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Volume utilisation (`VOLUME_UTIL`)

Volume carried as a share of the cubic capacity of the vehicles used.

| | |
|---|---|
| **Formula** | Σ volume ÷ Σ capacity × 100 |
| **Numerator** | Σ planned volume (CBM) |
| **Denominator** | Σ cubic capacity of the same vehicles (CBM) |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Planning (vehicle load and capacity) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Weight utilisation (`WEIGHT_UTIL`)

Weight carried as a share of the payload of the vehicles used.

| | |
|---|---|
| **Formula** | Σ weight ÷ Σ payload × 100 |
| **Numerator** | Σ planned weight (kg) |
| **Denominator** | Σ payload of the same vehicles (kg) |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Planning (vehicle load and capacity) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

## Tracking

### Average ETA error (`ETA_ERROR_MIN`)

Average distance in minutes between the last ETA and the actual arrival.

| | |
|---|---|
| **Formula** | Σ |actual − last ETA| ÷ completed trips with an ETA |
| **Numerator** | Sum of absolute ETA errors (minutes) |
| **Denominator** | Completed trips with an ETA |
| **Aggregation** | Average (Minutes) |
| **Better when** | lower |
| **Source of truth** | Tracking (ETA predictions) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### ETA accuracy (`ETA_ACCURACY`)

Share of completed trips whose last ETA was within the tolerance of the actual arrival.

| | |
|---|---|
| **Formula** | Trips with |actual − last ETA| ≤ tolerance ÷ completed trips with an ETA × 100 |
| **Numerator** | Completed trips with an ETA error within tolerance |
| **Denominator** | Completed trips that had an ETA and an actual arrival |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Tracking (ETA predictions) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Open critical exceptions (`OPEN_CRITICAL_EXCEPTIONS`)

Exceptions of Critical severity that nobody has resolved yet, from every module.

| | |
|---|---|
| **Formula** | Count of unresolved Critical exceptions |
| **Numerator** | Critical exceptions not resolved |
| **Denominator** | Exceptions in the selection |
| **Aggregation** | Count (Count) |
| **Better when** | lower |
| **Source of truth** | Deliveries, Tracking and Transporters (their own exceptions) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Route deviations (`ROUTE_DEVIATIONS`)

Times a vehicle left its planned route beyond the allowed distance.

| | |
|---|---|
| **Formula** | Count of deviations |
| **Numerator** | Deviations detected in the period |
| **Denominator** | Trips in the selection |
| **Aggregation** | Count (Count) |
| **Better when** | lower |
| **Source of truth** | Tracking |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Tracking coverage (`TRACKING_COVERAGE`)

Share of trips that have been on the road (or are) for which tracking ever started.

| | |
|---|---|
| **Formula** | Trips with tracking started ÷ trips that departed × 100 |
| **Numerator** | Departed trips whose tracking health is not 'Not started' |
| **Denominator** | Trips in transit or completed |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Tracking |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Tracking lost (`TRACKING_LOST`)

Trips whose phone has stopped reporting for longer than the lost threshold.

| | |
|---|---|
| **Formula** | Count of trips with health 'Lost' |
| **Numerator** | Trips with tracking Lost |
| **Denominator** | Trips in the selection |
| **Aggregation** | Count (Count) |
| **Better when** | lower |
| **Source of truth** | Tracking |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

## Transporters

### On-time pickup (OTP) (`OTP`)

Share of pickups made at or before the planned time (plus any grace the organisation allows).

| | |
|---|---|
| **Formula** | On-time pickups ÷ pickups with a planned and an actual time × 100 |
| **Numerator** | Pickups where actual ≤ planned + grace |
| **Denominator** | Shipments that have both a planned and an actual pickup time |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Shipments (planned and actual pickup) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Placement compliance (`PLACEMENT_COMPLIANCE`)

Share of vehicle placements that arrived on time.

| | |
|---|---|
| **Formula** | On-time placements ÷ placements × 100 |
| **Numerator** | Placements marked on time |
| **Denominator** | Placements (late, no-show and replaced ones count against) |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Transporter Management (vehicle placements) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Tender acceptance (`TENDER_ACCEPTANCE`)

Share of answered or expired tenders that the transporter accepted. Withdrawn offers and ones still open are left out.

| | |
|---|---|
| **Formula** | Accepted ÷ (accepted + rejected + expired) × 100 |
| **Numerator** | Tenders accepted |
| **Denominator** | Tenders accepted, rejected or expired |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Shipments (tenders) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Tender response rate (`TENDER_RESPONSE_RATE`)

Share of offered tenders that were answered (accepted or rejected) before they expired.

| | |
|---|---|
| **Formula** | Answered ÷ offered × 100 |
| **Numerator** | Tenders accepted or rejected |
| **Denominator** | Tenders offered and no longer open |
| **Aggregation** | Percent (Percent) |
| **Better when** | higher |
| **Source of truth** | Shipments (tenders) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |

### Transporter score (`TRANSPORTER_SCORE`)

Average overall score the Transporters module gave carriers for the period. Shown, never recalculated here.

| | |
|---|---|
| **Formula** | Average of overall scores |
| **Numerator** | Sum of overall scores |
| **Denominator** | Scorecards with a score |
| **Aggregation** | Average (Number) |
| **Better when** | higher |
| **Source of truth** | Transporter Management (scorecards) |
| **Filters** | Period, transporter, lane, region, customer, vehicle type and service where the underlying facts carry them; the caller's data limits always |
| **Exclusions** | Facts that cannot be judged are excluded and counted, never failed (see the KPI's note on a result) |
| **Period** | Any date range; compared with the previous period and the same period last year |
| **Calculation version** | 1.0 |
