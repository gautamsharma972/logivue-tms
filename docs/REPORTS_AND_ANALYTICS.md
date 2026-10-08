# Reports & Analytics (Module 6)

A reusable reporting platform, not a set of report pages. Every number a manager sees can be traced to the transactions behind it.

```text
 Planning · Transporters · Deliveries · Tracking · Freight Contracts      (each owns its own rules)
                         │  reporting providers (flat, read-only facts keyed by reference)
                         ▼
        ReportFacts  ── period, filters and the caller's limits applied once
                         ▼
        KPI engine (one definition per KPI, numerator + denominator + version)
                         ▼
        Report definitions (39) → grouping · sorting · paging · drill-down
                         ▼
        Dashboard / table / chart / map  →  export (CSV · Excel · PDF) · schedule · audit
```

Reports never recreate a rule another module owns: Planning calculates utilisation and savings, Transporters the scorecard, Deliveries the on-time and SLA judgements, Tracking health and ETA, Freight Contracts the kept freight. Reports counts, lists and compares them. `docs/KPI_DEFINITIONS.md` names the source of truth of each KPI.

## Where things are

| | |
|---|---|
| Backend | `backend/src/Modules/Tms.Modules.Reports` (`Domain/Engine`, `Domain/Kpi`, `Domain/Catalogue`, `Application`, `Infrastructure`, `Endpoints`) |
| Provider contracts | `backend/src/BuildingBlocks/Tms.SharedKernel/Contracts/Reporting.cs` |
| Providers in the modules | `Integration/*ReportingProvider.cs` in Shipments, Transporters, Deliveries, Tracking and Contracts |
| Demonstration data | `Reports/Infrastructure/Providers/DemoDataGenerator.cs` |
| Tables | `rpt_*` (migration `ReportsInitial`) |
| Web | `web/src/features/reports` |
| Tests | `Tms.UnitTests/Reports`, `Tms.IntegrationTests/ReportsApiTests.cs`, `web/src/features/reports/Reports.test.tsx` |

## The 39 reports

Report logic is code; everything a person may want to change without a release (name, permission, columns shown, status) is data in `rpt_report_definitions`, `rpt_report_columns`, `rpt_report_filters`, `rpt_report_groupings` and `rpt_report_sorts`, seeded per organisation from the code on first use and reconciled (new columns added, edits kept) after a release.

| Code | Report | Type | Source | Needs | Refresh | Export |
|---|---|---|---|---|---|---|
| `R01_EXECUTIVE_DASHBOARD` | Executive TMS dashboard | Dashboard | All modules | `reports.executive` | NearRealTime | xlsx, pdf |
| `R02_LOAD_PLANNING_SUMMARY` | Load planning summary | Analytical | Planning | `reports.planning` | NearRealTime | csv, xlsx, pdf |
| `R03_VEHICLE_ALLOCATION` | Vehicle allocation | Operational | Planning | `reports.planning` | NearRealTime | csv, xlsx |
| `R04_VEHICLE_UTILISATION` | Vehicle utilisation | Analytical | Planning | `reports.planning` | NearRealTime | csv, xlsx, pdf |
| `R05_UNPLANNED` | Unplanned shipment report | Operational | Planning | `reports.planning` | NearRealTime | csv, xlsx |
| `R06_FTL_VS_PTL` | FTL vs PTL analysis | Analytical | Planning | `reports.planning` | NearRealTime | csv, xlsx, pdf |
| `R07_CONSOLIDATION_SAVINGS` | Consolidation & savings | Analytical | Planning | `reports.planning` | NearRealTime | csv, xlsx, pdf |
| `R08_PLANNED_VS_ACTUAL_TRIP` | Planned vs actual trip | Operational | Planning + Tracking + Freight Contracts | `reports.planning` | NearRealTime | csv, xlsx, pdf |
| `R09_TRANSPORTER_SCORECARD` | Transporter performance scorecard | Analytical | Transporters | `reports.transporters` (transporters too) | Scheduled | csv, xlsx, pdf |
| `R10_TRANSPORTER_RANKING` | Transporter ranking | Analytical | Transporters + Planning + Deliveries | `reports.transporters` | Scheduled | csv, xlsx, pdf |
| `R11_TRANSPORTER_BENCHMARK` | Transporter benchmark | Analytical | Transporters + Planning + Deliveries | `reports.transporters` | Scheduled | csv, xlsx, pdf |
| `R12_TENDER_PERFORMANCE` | Tender performance | Analytical | Planning (tenders) | `reports.transporters` (transporters too) | NearRealTime | csv, xlsx, pdf |
| `R13_PLACEMENT_COMPLIANCE` | Vehicle placement compliance | Operational | Transporters (placements) | `reports.transporters` (transporters too) | NearRealTime | csv, xlsx, pdf |
| `R14_OTP_OTD` | OTP / OTD performance | Analytical | Planning + Deliveries | `reports.transporters` (transporters too) | NearRealTime | csv, xlsx, pdf |
| `R15_POD_COMPLIANCE_BY_TRANSPORTER` | POD compliance by transporter | Analytical | Deliveries (POD) | `reports.transporters` (transporters too) | NearRealTime | csv, xlsx, pdf |
| `R16_TRANSPORTER_EXCEPTIONS` | Transporter exceptions | Operational | Deliveries + Tracking + Transporters | `reports.transporters` (transporters too) | NearRealTime | csv, xlsx |
| `R17_DELIVERY_PERFORMANCE` | Delivery performance | Analytical | Deliveries | `reports.delivery` (transporters too) | NearRealTime | csv, xlsx, pdf |
| `R18_POD_COMPLIANCE` | POD compliance | Analytical | Deliveries (POD) | `reports.delivery` (transporters too) | NearRealTime | csv, xlsx, pdf |
| `R19_POD_AGEING` | POD ageing | Analytical | Deliveries (POD) | `reports.delivery` (transporters too) | NearRealTime | csv, xlsx, pdf |
| `R20_POD_REJECTIONS` | POD rejection report | Operational | Deliveries (POD) | `reports.delivery` (transporters too) | NearRealTime | csv, xlsx |
| `R21_SHORTAGE` | Shortage report | Operational | Deliveries (discrepancies) | `reports.delivery` | NearRealTime | csv, xlsx, pdf |
| `R22_DAMAGE` | Damage report | Operational | Deliveries (discrepancies) | `reports.delivery` | NearRealTime | csv, xlsx, pdf |
| `R23_FAILED_REFUSED` | Failed / refused deliveries | Operational | Deliveries | `reports.delivery` (transporters too) | NearRealTime | csv, xlsx |
| `R24_CONTROL_TOWER` | Live control tower | Dashboard | Tracking | `reports.tracking` | NearRealTime | csv, xlsx |
| `R25_ETA_DELAY` | ETA / delay report | Analytical | Tracking | `reports.tracking` | NearRealTime | csv, xlsx, pdf |
| `R26_ROUTE_DEVIATION` | Route deviation report | Operational | Tracking | `reports.tracking` | NearRealTime | csv, xlsx, pdf |
| `R27_DWELL_TIME` | Dwell time report | Analytical | Tracking | `reports.tracking` | NearRealTime | csv, xlsx, pdf |
| `R28_TRACKING_HEALTH` | Tracking health | Analytical | Tracking | `reports.tracking` | NearRealTime | csv, xlsx, pdf |
| `R29_TRACKING_GAPS` | Tracking gap report | Operational | Tracking | `reports.tracking` | NearRealTime | csv, xlsx |
| `R30_PLANNED_VS_ACTUAL_ROUTE` | Planned vs actual route | Operational | Tracking | `reports.tracking` | NearRealTime | csv, xlsx, pdf |
| `R31_CONTRACT_EXPIRY` | Contract status & expiry | Operational | Freight Contracts | `reports.contracts` | Scheduled | csv, xlsx, pdf |
| `R32_RATE_COVERAGE` | Rate coverage | Analytical | Freight Contracts + Shipments | `reports.contracts` | Scheduled | csv, xlsx, pdf |
| `R33_RATE_SLAB` | Rate / slab report | Operational | Freight Contracts | `reports.contracts` | Scheduled | csv, xlsx, pdf |
| `R34_DPH_SURCHARGE` | DPH & surcharge report | Operational | Freight Contracts | `reports.contracts` | Scheduled | csv, xlsx, pdf |
| `R35_FREIGHT_RATING_AUDIT` | Freight rating audit | Operational | Freight Contracts (kept ratings) | `reports.contracts` | NearRealTime | csv, xlsx, pdf |
| `R36_SHIPMENT_360` | Shipment 360 | Drilldown | All modules | `reports.cross` | NearRealTime | xlsx, pdf |
| `R37_LANE_PERFORMANCE` | Lane performance | Dashboard | All modules | `reports.cross` | Scheduled | csv, xlsx, pdf |
| `R38_COST_VS_PERFORMANCE` | Transporter cost vs performance | Analytical | All modules | `reports.cross` | Scheduled | csv, xlsx, pdf |
| `R39_COST_VS_SERVICE` | Cost vs service analysis | Analytical | All modules | `reports.cross` | Scheduled | csv, xlsx, pdf |

Four report types are supported by one result shape: **Dashboard** (KPI cards, charts, map), **Operational** (transaction rows), **Analytical** (grouping, aggregation, trends) and **Drill-down** (Shipment 360: sections across modules).

## Filters, dimensions and grouping

Common filters: dates and period (daily, weekly, monthly, quarterly, YTD, rolling 30 / 90), business unit, origin, destination, zone, lane, region, transporter, vehicle, vehicle type, driver, customer, service type (FTL / PTL / Dedicated), shipment, load, trip, contract, status, exception. A report offers the filters that make sense for its rows, and each filter narrows only the facts that carry that dimension.

Dimensions have **one** definition (`FactDims`, `IndiaRegions`): a lane is "Origin → Destination" everywhere and a region comes from the same state map everywhere (overridable per organisation). Grouping is by any dimension a report lists; ratios are re-derived from summed numerators and denominators.

## Drill-down

Every KPI card, total, chart and linked value leads to the report that holds the detail, **keeping the filters** (period, carrier, lane, customer …). The target report applies them; a person can remove any on the next page. Example: Executive OTD → Late deliveries (`onTime=No`) → carrier → Shipment 360 (order, plan, vehicle, carrier, contract, tracking, delivery, proof, discrepancies, exceptions, freight, timeline with the delay reason).

## Security model

Enforced on the server; the web hides only what a person cannot use.

| Rule | Where |
|---|---|
| Report permission (`reports.executive`, `.planning`, `.transporters`, `.delivery`, `.tracking`, `.contracts`, `.cross`) plus `reports.read` | `ReportAccess.CanRun` |
| Export needs `reports.export`; scheduling needs `reports.schedule`; settings, definitions and data limits need `reports.manage`; the audit trail needs `reports.audit` | handlers |
| Commercial data (freight, contract, rating) only with `reports.contracts`; Shipment 360 drops its freight and contract sections for others; money KPIs are refused to others | report code, `KpiCalculationService.CanSee` |
| A transporter user sees only vendor-safe reports, and in every one only its own company: the limit is applied to **every kind of fact**; facts that carry no company are never shown. Another company's id in a filter is answered 404 | `ReportFacts.Allowed`, `ReportExecutor.Restrict` |
| Rankings, benchmarks, cost and contract rates are never shown to a transporter | not vendor-safe |
| A staff user can be limited to customers, regions or business units (`rpt_data_scopes`). Rows outside are removed; facts and reports that do not carry the dimension are refused to them (fail closed) | `ReportFacts.Allowed`, `reports.scope_unsupported` |
| Query-string tampering gives nothing: limits are applied after the filters, never from them | tests |
| Export files are stored under an opaque key, served only through an authenticated endpoint to the person who asked, with `Cache-Control: no-store`; there is no public URL | `ExportHandler.DownloadAsync` |
| A job and a subscription freeze the requester's authority; a subscription re-reads the owner's permissions at every run and stops when they are gone | `ReportScheduler` |

## Audit

`rpt_report_audit` records Viewed, Executed, ExportRequested, Exported, Downloaded, ExportDenied, ExportCancelled, SubscriptionCreated / Changed / Deleted, ScheduledRunQueued, DefinitionChanged, SettingsChanged, ScopeChanged and SummaryRebuilt with the user, report, filters, time, format, row count and duration. Changes to the report tables are also in the platform audit trail like every write.

## Data layer, freshness and performance

- Each report reads its facts through the providers for the period (plus the comparison periods and trend history it needs, fetched **once** per run and shared). The module-side providers use projections, never `SELECT *`, and filter by date and, for company users, by company in the database.
- **Summary table** `rpt_daily_transport_kpi`: each day's numerator and denominator for 22 additive KPIs, written hourly for the last 14 days (rebuild any span with `POST /reports/summary/rebuild?days=n`). Executive trend charts read it for wholly past periods when no filter or limit applies, and reproduce exactly what the transactions give (tested); the running period is always calculated live. Changing the data source clears it, and a value is not read if its calculation version differs.
- Refresh types per report: real time, near-real-time (control tower 15 s cache, dashboards 120 s), scheduled, on demand. Every result shows when it was built, how old it is, how long it took and which data it came from. "Refresh" bypasses the cache; the page can also refresh itself every 15 s to 5 min.
- Slow reports are logged (`ReportCode`, user, duration, rows) against a configurable threshold. Report metadata, KPI definitions, settings and lookups are cached briefly.
- **Honest limits.** Reports aggregate in memory over the requested window after the providers return projections; paging, sorting and grouping are server-side so the browser never holds more than a page. This is comfortable for the demonstration volume (570 shipments, 900 deliveries, 540 trips, 22 carriers; every report under a few hundred milliseconds) and for tens of thousands of rows per window. It has **not** been load-tested at 1,000,000 shipments or 10,000,000 tracking events. See *Performance recommendations*.

## Export

CSV (UTF-8 with BOM, quoted, formulas neutralised), Excel (a Report sheet, a Summary sheet with filters, KPI parts and notes, and a sheet per table section) and PDF (landscape A4, key figures, totals, sections, detail table with repeated header and page numbers, Latin text only; first 3,000 rows, the file says so). Operational reports offer CSV and Excel; management reports Excel and PDF; analytical reports all three.

An export up to `SyncExportRows` (5,000) is built while the person waits. A larger one becomes a **report job** (`Queued → Running → Completed`, or `Failed`, `Expired`, `Cancelled`): the web shows progress under *My exports*, an email says when it is ready, the file is kept `JobKeepHours` (72) and then deleted. A failed or cancelled job can be tried again; the worker retries once on an unexpected error. Only the requester can see or download a job. Limits are settings (`SyncExportRows`, `ExportMaxRows`, `JobKeepHours`).

## Scheduling and subscriptions

Daily, Weekly (weekday), Monthly (day 1–31, or 0 for the last day) and Custom (every 15 minutes to 7 days), at a time of day in the **subscription's time zone**. Filters are stored with it; relative periods (`period=Rolling30`) are recalculated at each run. A due subscription becomes a job for its owner (their file appears under *My exports*), and each listed recipient gets an email with a link that opens the report **with their own access**: an email never contains report data. *Run now* produces it immediately. A subscription stops by itself when its owner is inactive or may no longer export the report, or after `ScheduleStopAfterFailures` failures. Subscriptions use `rpt_report_subscriptions`; the scheduler is a background service (no new infrastructure).

Examples that work as configured: POD ageing over 7 days (`R19_POD_AGEING`, `minAgeDays=7`) daily 08:00; Transporter scorecard (`R09`) Mondays 09:00; Lane performance (`R37`) on the 1st; critical exceptions (`R16`, `severity=Critical`) daily 07:00.

## Report settings (nothing hard-coded)

Data source (Live / Demo), ageing buckets, working-day ageing, working days and holidays, OTP / OTD grace, ETA tolerance, cost / performance dividing line (median or average), default period, top-N, export limits and retention, cache durations, slow-report threshold, expiry bands, region overrides, schedule limits. KPI weights, SLA targets and scoring rules belong to the module that owns them and are consumed, not copied.

## Demonstration data

When an organisation chooses **Demo** (or a module is not installed) the same reports run over a deterministic generated dataset: 22 carriers of deliberately different quality and price, 64 vehicles, 16 lanes, 12 customers, ~570 shipments across 120 days (on time, at risk, delayed, cancelled, completed), ~190 planning runs with ~560 vehicles and ~170 unplanned orders, ~660 tenders, ~530 placements, ~900 deliveries, ~840 proofs (pending, late, rejected, resubmitted), ~160 shortage and damage lines with claims, ~530 trips (healthy, stale, lost, not started, completed), ~110 route deviations, ~1,360 dwell events, gaps, ~70 exceptions across modules, 28 contracts (some expiring, one expired, one pending, one draft), ~340 rates, diesel clauses with up to three revisions, ~530 kept ratings and ~95,000 location events counted. Every page marks the data as demonstration data. Numbers differ between carriers and lanes on purpose (cheap-and-poor, dear-and-good).

## API

Every route requires sign-in; permissions are enforced in the handlers. Interactive documentation: `/scalar` in development.

| Route | Purpose |
|---|---|
| `GET /api/v1/reports?search=&category=` | Catalogue the caller may open |
| `GET /api/v1/reports/{code}` , `/{code}/metadata` | Columns, filters, grouping, sorting, drills, export formats, permissions |
| `POST /api/v1/reports/{code}/execute` | `{ filters, groupBy, sort, page, pageSize, refresh }` → rows, columns, KPI cards, totals, charts, sections, filters applied, period, calculation version |
| `POST /api/v1/reports/{code}/export` | `{ format, filters, groupBy, sort, background }` → 200 with a finished job, or 202 with a queued one |
| `GET /api/v1/reports/jobs`, `/jobs/{id}`; `POST /jobs/{id}/cancel`, `/retry`; `GET /jobs/{id}/download` | Export jobs |
| `GET /api/v1/kpis`, `/kpis/{code}`, `/kpis/{code}/value`; `POST /kpis/calculate`; `PUT /kpis/{code}` | KPI definitions and calculation |
| `GET /api/v1/dashboards/executive`, `/control-tower` | The two dashboards (query string = filters) |
| `GET/POST/PUT/DELETE /api/v1/report-subscriptions`; `POST …/{id}/run-now` | Schedules |
| `GET/PUT /api/v1/reports/preferences` | Favourites, widgets, default filters, refresh interval |
| `GET /api/v1/reports/lookups/{name}`, `/data-source` | Filter values; where the data comes from |
| `GET/PUT /api/v1/reports/settings`, `PUT /{code}/definition`, `GET/PUT /scopes`, `GET /audit`, `POST /summary/rebuild` | Administration |

Errors are the platform's problem JSON with a stable `code`; an execution failure is `REPORT_EXECUTION_ERROR` with a trace id and no internals. An empty selection says "No data found for the selected filters", and a KPI that cannot be judged says "Not measurable" rather than 0%.

## Performance recommendations

1. **Push aggregation into the owning modules for large tenants.** Add `…AggregateAsync` methods to the provider contracts (for example OTD per carrier per day) and let the module aggregate in SQL; the engine already consumes numerators and denominators.
2. **Widen the summary tables** with the same pattern as `rpt_daily_transport_kpi`: per carrier (`rpt_transporter_performance`), per lane (`rpt_lane_performance`), freight spend, tracking and POD performance. Add dimensions to the key and have `ITrendSource` use them when a filter matches.
3. **Index** the owning modules' tables for the provider predicates: shipments by `(tenant, planned_pickup_date, transporter_id)`, deliveries by `(tenant, actual_delivery_at)`, tracked shipments by `(tenant, planned_arrival_at)`, ratings by `(tenant, calculated_at)`; check plans with `EXPLAIN` before releasing.
4. Move the report jobs and the scheduler to a separate worker process when export volume grows; they already read their work from `rpt_report_jobs` and `rpt_report_subscriptions`, so more than one instance is safe (a job is claimed with optimistic concurrency).
5. Store export files in object storage by implementing `IFileStore`.

## What is not here

Dashboard widget layout is stored (`rpt_user_preferences.WidgetsJson`) and favourites, default filters and refresh interval work; a drag-and-drop widget designer is not built. Customer-facing report pages are not built (data limits for customers exist on the server). Holiday calendars are a list in the settings, not a shared master. Pushing recipients' own copies by email attachment is not possible with the current mail contract (it has no attachments), so recipients receive a link.
