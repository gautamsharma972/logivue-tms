# LogiVue TMS

Transport management: transporter master data, onboarding and compliance, eligibility and recommendation,
tendering with the vendor portal, vehicle placement, execution, proof of delivery and performance.

## Structure

```
backend/
  src/LogiVue.Tms.Api/                              Host: Program.cs (authentication, error handling, module wiring)
  src/Shared/LogiVue.Tms.Shared/                    Error contract, exceptions, audit and current-user abstractions
  src/Modules/TransporterManagement/
    Domain/                                         Entities and enums (tm_* tables)
    Application/                                    Services, rules, DTOs, integration contracts
    Infrastructure/                                 TransporterDbContext, configurations, seeds, migrations, workers
    Api/                                            Controllers, role sets, access filters, registration
  tests/LogiVue.Tms.Tests/                          Integration, unit and authorisation tests
frontend/   React + TypeScript + Vite (TanStack Query/Table, Recharts, Leaflet) - scaffold only
```

The module owns only `tm_*` tables and its own migrations history (`__tm_efmigrations_history`). It reaches Planning,
Claims, POD, Rates and Tracking only through the interfaces in `Application/Integration`.

## Prerequisites

- .NET SDK 10
- Node.js 20+ and npm (frontend)
- MySQL 8.0.x

## Running the API

Credentials and signing keys are never committed. Provide them through environment variables or user secrets.

```bash
export ConnectionStrings__TmsDb='Server=localhost;Port=3306;Database=logivue_tms;User ID=<user>;Password=<password>;CharSet=utf8mb4;'
export Authentication__SigningKey='<HMAC key, 32+ characters>'     # or Authentication__Authority for an OpenID Connect provider
cd backend/src/LogiVue.Tms.Api
dotnet run
```

Run from the project folder so `appsettings.json` loads. Start-up refuses to run without an issuer, an audience and
a key or authority.

- Swagger UI: `/swagger` (enabled in Development)
- Health: `/health` (anonymous)
- Module info: `GET /api/v1/transporter-management/info` (anonymous)
- On start-up the API applies the module's migrations.

### Authentication and authorisation

Every endpoint requires a signed-in caller unless it is anonymous (health and module info). Tokens carry:

| Claim | Meaning |
| --- | --- |
| `sub` | user id, recorded in the audit trail |
| `roles` | internal roles and/or transporter roles (see below) |
| `transporter_id` | set only for transporter users; it scopes every vendor query and command to that transporter |

Unauthenticated calls return `401 UNAUTHENTICATED`. Calls without the required role return `403 FORBIDDEN`.
Vendor calls without a transporter identity return `403 INTERNAL_ACCESS_ONLY`, and vendor calls without a
transporter role return `403 VENDOR_ROLE_REQUIRED`. Another transporter's record is reported as not found.

Role sets (`Api/Filters/AccessFilters.cs`). Reads are open to any signed-in internal user.

| Area | Roles that may change data |
| --- | --- |
| Transporter master data, fleet, lanes, capabilities, drivers | Transport Admin, Transport Manager, Transport Executive |
| Compliance documents (verify, reject), compliance evaluation | Compliance User, Transport Admin, Transport Manager |
| Document upload and edit | the above plus Transport Executive |
| Onboarding decisions | the configured role for each step (`tm_onboarding_steps`) |
| Tendering (create, send, award, cancel, decide on behalf) | Transport Admin, Transport Manager, Transport Executive |
| Placement, execution events, alerts | Operations User, Transport Executive, Transport Admin, Transport Manager |
| POD review | Compliance User, Transport Admin, Transport Manager |
| KPI recalculation and scorecards | Finance User, Transport Admin, Transport Manager |
| Planning rules | Transport Admin, Transport Manager |
| Vendor writes (accept, reject, counter-offer, vehicle, confirm, report, POD) | Transporter Admin, Transporter Operations User |
| Vendor reads | any transporter role, including Transporter Viewer |

## Testing

```bash
cd backend
dotnet test
```

Host-based tests run sequentially (`tests/LogiVue.Tms.Tests/AssemblyInfo.cs`). They use SQLite for the module
context, so unique indexes and transactions are enforced. Test identities come from headers through a test-only
authentication scheme, except `JwtAuthenticationTests`, which validates real signed tokens.

## Migrations

```bash
cd backend
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --context LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.TransporterDbContext \
  --project src/Modules/TransporterManagement/Infrastructure --startup-project src/LogiVue.Tms.Api --output-dir Persistence/Migrations
```

Entity Framework is pinned to 9.0.20 because the MySQL provider (Pomelo 9.0.0) does not yet support EF Core 10.

## API by area

Transporter master (Milestone 2)

```
GET/POST      /api/v1/transporters                            PUT /api/v1/transporters/{id}
POST          /api/v1/transporters/{id}/contacts | branches    PUT .../contacts/{id} | .../branches/{id}
GET/POST      /api/v1/transporters/{id}/vehicles              PUT .../vehicles/{id}
GET/POST      /api/v1/transporters/{id}/drivers               PUT .../drivers/{id}
GET/POST      /api/v1/transporters/{id}/lanes                 PUT .../lanes/{id}
GET/POST      /api/v1/transporters/{id}/capabilities          DELETE .../capabilities/{id}
GET           /api/v1/transporter-management/lookups/transporter-types | capability-types | document-types | service-types | onboarding-steps
```

Onboarding, compliance and documents (Milestone 3)

```
POST          /api/v1/transporters/{id}/submit | approve | reject | suspend | activate | deactivate | blacklist
GET           /api/v1/transporters/{id}/approval-history
GET/POST      /api/v1/transporters/{id}/documents             PUT .../documents/{id}
POST          /api/v1/transporters/{id}/documents/{id}/verify | reject        GET .../documents/{id}/file
GET           /api/v1/transporters/{id}/compliance            POST /api/v1/transporter-management/compliance/evaluate
GET           /api/v1/transporter-management/alerts           POST .../alerts/{id}/acknowledge | resolve
```

Eligibility and recommendation (Milestone 4)

```
POST          /api/v1/transporters/eligibility/check
POST          /api/v1/transporters/recommendations
GET           /api/v1/transporters/planning/eligible
GET/POST      /api/v1/transporters/{id}/planning-rules        POST .../planning-rules/{id}/deactivate
GET/POST      /api/v1/transporters/{id}/scorecards            generate a weighted score for a period
```

Tendering and the vendor portal (Milestone 5)

```
POST          /api/v1/tenders                                 GET /api/v1/tenders  |  /{id}
POST          /api/v1/tenders/{id}/send | award | cancel
POST          /api/v1/tenders/{id}/accept | reject            recorded on behalf of a transporter
POST          /api/v1/tenders/{id}/counter-offer/accept | decline
GET           /api/v1/vendor/dashboard | profile | tenders | tenders/{id} | loads
POST          /api/v1/vendor/tenders/{id}/accept | reject | counter-offer
POST          /api/v1/vendor/loads/{id}/vehicle               confirm a fleet vehicle and driver
```

Placement, execution, POD and performance (Milestone 6)

```
POST          /api/v1/vehicle-placement                       GET (paged, transporterId, status)
POST          /api/v1/vehicle-placement/{id}/place | no-show | cancel
POST          /api/v1/executions   |   GET /api/v1/executions/{id}   |   POST /api/v1/executions/{id}/events
GET           /api/v1/pods (paged) | /{id} | /{id}/file
POST          /api/v1/pods/{id}/start-review | accept | reject | request-resubmission
GET           /api/v1/transporters/performance/{id}/operations?from&to   KPIs and metrics (not stored)
POST          /api/v1/transporters/performance/recalculate    rebuild monthly KPIs from operational records
GET           /api/v1/vendor/placements (paged) | pods (paged) | pods/{id}/file
POST          /api/v1/vendor/placements/{id}/confirm | report
POST          /api/v1/vendor/loads/{loadReference}/pod        multipart: File, PodDate, ReceivedBy
```

## Rules that matter

- **Placement** runs Requested, Confirmed, VehicleAssigned, Reported, Placed, LoadingStarted, with NoShow and Cancelled
  as exits. A different vehicle after assignment counts as a replacement. Vehicle assignment goes through the tender
  endpoint, which carries it to the placement. A no-show needs the grace period to pass.
- **Delays** are attributed through `tm.execution.delayPolicy`. Only carrier-attributable delays count against a
  transporter. Non-carrier and unattributed delays are excluded from the score until someone attributes them.
- **KPIs** are derived, never edited. Monthly buckets store numerator and denominator for the transporter, each lane and
  each vehicle type. A value with nothing to measure is null (Not Measurable), never zero.
- **Scorecards** pool the period's KPIs, exclude any KPI below the minimum sample, and renormalise the weights.
  Claims are scored in the right direction (lower is better). Each scorecard refreshes the planning read model, and
  planning reads its overall score from there.
- **Driver licences**: an expired licence blocks that driver from assignment, not the transporter. A driver is matched
  to an assignment by mobile number.
- **POD** is created on delivery. Its SLA is `tm.pod.submissionSlaHours`. Overdue PODs, placements, pickups and
  deliveries raise alerts, which resolve automatically once the record catches up.
- **Eligibility** reads pooled KPIs over the window. Lane values win when the lane's sample is large enough.

## Configuration (database-held)

Business policy is stored as configuration rows, read through `ITransporterSettings`, and never hard-coded.

| Key | Purpose |
| --- | --- |
| `tm.scorecard.weights.default` | KPI weights for scorecards (percentages) |
| `tm.scorecard.minimumSampleSize` | Minimum denominator before a KPI is scored |
| `tm.recommendation.weights`, `tm.recommendation.scoring` | Recommendation weights and scoring parameters |
| `tm.performance.thresholds`, `tm.performance.windowDays` | Performance bands and the eligibility window |
| `tm.eligibility.restrictions` | Optional performance-based exclusions (off by default) |
| `tm.placement.alertMinutesBefore`, `tm.placement.graceMinutes` | Placement alert lead time and grace |
| `tm.pod.submissionSlaHours` | POD submission SLA |
| `tm.execution.delayPolicy` | Pickup and delivery tolerance and the delay reasons with their attribution |
| `tm.tender.responseSlaMinutes`, `tm.tender.rejectionReasons`, `tm.tender.allowCounterOffer` | Tender behaviour |
| `tm.alerts.enabled`, `tm.alerts.delayEscalationMinutes` | Alerting |
| `tm.compliance.renewalReminderDays` | Expiry reminders |

Background workers (`TransporterManagement:*`): `ComplianceEvaluation` (every 360 minutes), `TenderExpiry` (every 30
seconds), `OverdueMonitor` (every 5 minutes). Each can be disabled.

## Design decisions

These choices are in place. Each one can be changed if the business disagrees.

- **Extra tables beyond the minimum table list:** planning rules, configuration settings, lookups and service types.
  The brief asks for auditable planning rules and database-held configuration, and forbids hard-coded types.
- **Broadcast tenders** are one invitation row per transporter, sharing a tender number, because each transporter
  responds independently.
- **Identifiers are `long`** throughout the module. The placeholder host code that used `int` has been removed.
- **Estimated cost** is the figure used for ranking and eligibility. It is not the freight audit figure.
- **Lane identity** for planning rules is the transporter lane's id.
- **Vehicle-type and location references are not validated** against other modules' masters. Those masters belong to
  other modules, so this module stores references only.
- **Detail views** return contacts, branches and capabilities. Vehicles and lanes are paged separately so a large fleet
  does not bloat the detail.
- **Documents are stored locally** under `App_Data/tm-documents`, behind `IDocumentStorage`. Object storage can replace
  it without changing callers. A failed commit removes the stored file.
- **Driver-level documents** are attached to a driver record. Driver licences use the same verification and expiry rules
  as vehicle documents.
- **KPI buckets are calendar months.** Eligibility and scorecards pool the buckets in their window.
- **Pending POD** on the vendor dashboard means awaiting submission or resubmission.
- **A declined counter-offer** closes the invitation as rejected with reason `RATE_ISSUE`, so sequential tenders move on.
- **Documentation issues are carrier-attributable.** "Other" is unattributed until someone classifies it. Both are
  configurable in `tm.execution.delayPolicy`.
- **Expired driver licences** block only that driver. The transporter stays allocatable.

## Gaps still open

- **Claims source:** claims are recorded in this module's `tm_claims` table through an internal API, and read through
  `ITransporterClaimsProvider`. When the Claims module exists, replace `LocalClaimsProvider` with its adapter. The claims
  workflow itself stays out of this module.
- **Cost and availability sources:** invoiced cost comes from `tm_load_costs` (recorded internally) and availability from
  `tm_capacity_days` (reported by vendors). Replace `LocalCostProvider` and `LocalAvailabilityProvider` with the finance
  feed and fleet telemetry when they exist. Until then, these KPIs are Not Measurable where there are no records.
- **Scale:** eligibility still reads every transporter row (the master list is small) and loads fleet, document and KPI
  rows only for the lane's candidates. Each request still holds those rows in memory. Moving scoring into the database is
  the next step if candidate counts grow.
- **Exports** are built in the browser from the rows on screen (CSV, and Excel via SpreadsheetML). Server-side export for
  full datasets and PDF are not built.
- **Frontend coverage:** built screens are the transporter list, 360 tabs (overview, fleet and drivers, lanes, compliance,
  performance with claims, cost and capacity, scorecard with trend, benchmark), ranking, benchmark, tenders, vehicle
  placement, alerts, and the vendor portal (dashboard, tenders, loads, POD, drivers, capacity). Not yet built: the
  transporter create and edit forms, onboarding and approval screens, the recommended-transporter list, and the exceptions
  screen (the exceptions API is not built either).
- **Exceptions** have no API yet. The brief's exceptions workflow (assign, resolve, escalate) is not implemented.
- **Demo data** (brief section 69) is not seeded.
- **Live database clean-up:** the development database still contains tables from the removed placeholder code
  (shipments, trips, vehicles, drivers, locations) and the old host migrations history. They are unused. Dropping
  them is a manual step that needs your confirmation.
- **Production identity provider:** startup refuses a signing key outside Development and requires
  `Authentication:Authority`. The roles and `transporter_id` claims must be configured in that provider.
- **Frontend sign-in:** the session box takes a pasted bearer token, kept in sessionStorage for the tab. Production needs
  the identity provider's sign-in flow.
