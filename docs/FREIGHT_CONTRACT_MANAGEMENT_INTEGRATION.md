# Freight Contract Management (Module 5) — integration guide

Implemented by **extending `Tms.Modules.Contracts`** (decision: no parallel module). Tables stay `contracts_*`, not `fc_*`.
Existing `/freight/quote` and `FreightCalculator` keep working; the new rating engine sits beside them and reuses them for base freight.

## What it does
Contract lifecycle (versions, approval, suspend/resume/cancel, renewal), rate cards with slabs/priority/min-max/validity, DPH
(diesel) rules with a price index, accessorials, capacity and SLA commitments, Excel/CSV import, a rating engine with full
explainability, simulator/what-if/compare, kept rating history, dashboards and reports.

## Rating engine (`Domain/RatingEngine.cs`, version `1.0`)
1. **Contract eligibility**: approved, in force on the shipment date (suspension windows honoured), carrier and service match.
2. **Lane scoring**: origin + destination specificity (City 3, Zone 2, State 1, Any 0); highest wins.
3. **Exclusions**: each rejected rate gets a code and reason (expired, wrong vehicle, weight outside band, capability missing...).
4. **Best per contract**; same priority and score ⇒ `FREIGHT_RATE_CONFLICT` (never a silent pick).
5. **Pipeline**: base freight (slabs/flat/per-km + card min/max) → DPH → discount → accessorials → rounding.
Result carries lines, reasons, exclusions and a trace, so "why ₹41,320?" is answerable from the response alone.
Failures are values: `FREIGHT_RATE_NOT_FOUND` (with advice), `FREIGHT_RATE_CONFLICT`, `FREIGHT_INPUT_MISSING`.
A rating can be **kept** (committed) against a shipment: it stores request, result, contract/rate/DPH versions and calculation
version, can be **reproduced** (re-rated and compared), and overridden (`contracts.rating.override`, reason required).

## DPH
Versioned `DphRuleSpec`: formula (percentage variation, fixed per step, per-km per step, impact %/step, cap), threshold,
step, direction (up/down/both), frequency, base price/date. Prices come from `contracts_diesel_prices` (region/date/source).
Keeping a rating pins a `DphPeriodSnapshot`, so a later price change never alters a past price. `POST /dph/rules/{id}/calculate`
previews a rule against any price.

## Accessorials
Fixed, PerUnit, Tiered, PercentOfFreight, Reimbursed; triggered by request flags. Legacy `ContractTerms` charges are mapped
unless the contract defines the same code.

## Bulk import
`GET /freight-rates/import/template` → fill → `POST /freight-rates/import` (multipart) → batch with a row-by-row preview
(issues per field) → `POST .../rows/{n}` to correct → `POST .../apply` (valid rows only, into a **draft revision**, never
straight to active) or `DELETE` to discard. Columns are matched case/punctuation-insensitively
(`origin, destination, vehicle, ratetype, rate, weightfrom, weightto, minimum, maximum, priority, validfrom, validto, dphcode, ...`).
Validation reuses `RateValidator`: duplicates, same-priority conflicts (error), overlaps at different priority (warning),
unknown DPH code, cross-contract overlap. `GET /freight-rates/export` writes the same layout.

## Simulation
`/freight-rating/simulate` (no record), `/what-if` (override DPH price, discount, rate, date), `/compare` (all eligible
contracts side by side). None writes anything; `/calculate` and `/qualify` can commit when given a shipment reference.

## API (all under `/api/v1`, bearer auth)
| Group | Routes |
|---|---|
| `/contracts`, `/freight-contracts` | CRUD, submit, approve/reject, suspend/resume/cancel, renew, create-version, renewal-impact, documents (+verify), dph-rules/accessorials/capacity/sla (GET/PUT) |
| `/freight-rates` | list, create, get, history, create-version, validate, export, template |
| `/freight-rates/import` | upload, list, get, correct row, apply, discard |
| `/freight-rating` | calculate, qualify, simulate, what-if, compare, history, get, reproduce, override (POST/DELETE) |
| `/dph` | rules (list/create/update), rules/{id}/calculate, price-index (GET/POST) |
| `/freight-contract-dashboard` | summary, expiry, rate-coverage, validation, rate-usage |
| `/freight-contract-reports` | list, `{report}` (download) |
| `/accessorials` | list, create, update |

Permissions: `contracts.read`, `.manage`, `.approve`, `.rate`, `.rating.override`, `.verify` (staff-only). Vendors never reach these.

## Events (transactional outbox, idempotent subscribers)
`ContractActivated/Expired/Superseded/Suspended`, `ContractRenewalDue`, `RateSuperseded`, `RateExpired`, DPH revision due.
There is no separate `IContractEventPublisher`: the outbox is the abstraction.

## Integration contracts (`Tms.SharedKernel.Contracts/FreightRating.cs`)
- `IFreightRatingService` — Planning/Shipments request ratings; modules never reference Contracts directly.
- `IFreightActualsProvider` — Tracking/POD supply actuals (distance, delays) for audit comparison.
- `IContractualBaselineService` — Freight Audit reads the contractual price a bill should match.
- `IApprovalGateway.DecideAsync` — approvers decide from the contract screen.
Local adapters are registered by the module; replace by registering another implementation first.

## Demo data
`POST /api/v1/dev/contracts/seed-demo` (dev only) or `node tools/seed-contracts-demo.mjs` after `seed-planning-demo.mjs`.
Rate versions V7/V8 in the demo are synthetic history.

## Files
- **Module-specific**: everything in `backend/src/Modules/Tms.Modules.Contracts`, `backend/tests/Tms.UnitTests/Contracts`,
  `backend/tests/Tms.IntegrationTests/{FreightContractApiTests,FreightRatingApiTests,FreightBulkApiTests,ContractsDemoSeedTests}.cs`,
  `web/src/features/contracts/*`, migration `FreightRatingEngine`.
- **Shared, changed**: `SharedKernel/Contracts/{FreightRating,Approvals}.cs`, `Tms.Modules.Approvals/.../ApprovalGateway.cs`,
  `Tms.Api/DevEndpoints.cs`, `web/src/{App.tsx,layouts/navigation.tsx,lib/api/{endpoints,queryKeys,types}.ts}`.

## Merge
Apply the `FreightRatingEngine` migration (`Database:MigrateOnStartup` in dev; deploy step in prod). It only adds columns and
tables. Existing contracts keep working: new rate fields default (priority 100, no bands, no validity).

## Known limits and deviations
- Approved and Active are one status (Active = approved, in force by dates).
- Import rows are stored as JSON on the batch (one table), not three.
- Progressive and base+excess slab tables cannot be exported to a single row.
- `fc_` table prefix not used.
- The engine is rule-based; results are never labelled "optimal".
