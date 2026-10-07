# TMS — working agreements

Multi-tenant SaaS Transport Management System (India: GST, e-way bill). Solo developer + Claude.
Scope, phases and per-module status: `docs/roadmap.md`. Decisions: `docs/adr/`.

## Layout
- `backend/` — .NET 10 modular monolith. `src/BuildingBlocks/*` (shared), `src/Modules/Tms.Modules.*` (one project per
  business module), `src/Tms.Api` (host only: wiring, no business logic). Tests in `backend/tests`.
- `web/` — React + TS + Vite + Ant Design. Feature folders under `src/features/*`, nav in `src/layouts/navigation.tsx`.

## Backend conventions
- Module internals: `Domain/` (entities, rules; no EF/ASP.NET), `Application/` (one file per use case: request record,
  FluentValidation validator, `internal` handler), `Infrastructure/` (DbContext, mappings, migrations), `Endpoints/`.
  A module exposes only `Add<Module>Module`, `Map<Module>Endpoints` and public request/response contracts.
  **Modules never reference each other** — use domain events / contracts. `Tms.ArchitectureTests` enforces this.
- Expected failures are `Result`/`Result<T>` values with a stable `Error.Code` (`area.reason`), never exceptions.
  Endpoints convert with `.ToHttpResult()` → RFC 9457 problem JSON. No MediatR: endpoints inject handlers directly.
- Every tenant-owned entity implements `ITenantScoped`; `TmsDbContext` adds the query filter (fails closed with no
  tenant) and `AuditSaveChangesInterceptor` blocks cross-tenant writes. Use `IgnoreQueryFilters()` only for
  pre-authentication lookups, with an explicit `TenantId` predicate.
- Aggregates derive from `AggregateRoot` (audit stamps + `Version` optimistic concurrency). Updates take the client's
  `Version` and set `Entry(x).Property(v => v.Version).OriginalValue`.
- All writes are audited automatically. Use `[AuditIgnore]` for secrets / high-churn fields.
- Permissions: declare as `PermissionDefinition`s, register in DI, guard endpoints with `.RequirePermission(...)`.
  Never let a caller grant permissions they don't hold (`RoleAssignmentPolicy`).
- Tables are named `{module}_{table}` via the module schema (MySQL has no schemas). Snake_case columns. Guid v7 keys.
- Warnings are errors. Nullable on. File-scoped namespaces. `.editorconfig` is authoritative.

## Cross-module collaboration
Modules talk only through `Tms.SharedKernel.Contracts` (e.g. `IApprovalGateway`, `ApprovalCompleted`, `IUserDirectory`).
Approvals: see `docs/approvals.md`. Dev/test-only endpoints live in `Tms.Api/DevEndpoints.cs`.

## Events, email, secrets
- Domain events go through a transactional outbox: delivered inline after commit, retried by `OutboxProcessor` on failure.
  **Subscribers must be idempotent.** Register a module's events with `AddDomainEventTypes(assembly)` and handlers with `AddDomainEventHandlers`.
- Refresh tokens are an HttpOnly cookie (`tms_refresh`); cookie endpoints require the `X-TMS-Client` header. Access tokens stay in memory.
- Sensitive columns use `IFieldEncryptor` (converter in the module's DbContext) plus `[AuditMask]`. Never log or audit raw secrets.
- Staff roles and external (vendor/driver) roles are separate audiences; new permissions are staff-only unless marked `ExternalAllowed`.
- Operator commands live in `Tms.Api` (`tenant:create`); there is intentionally no public tenant-creation endpoint.

## Planning (Shipments)
- Planning rules stay pure domain code. The optimizer asks `IFleetDirectory`/`ITransporterDirectory` (Transporters, via SharedKernel contracts)
  for real vehicles and drivers through `FleetAllocator`: one vehicle/driver per trip per plan, availability by day, never double-booked.
  A price used only as a yardstick (backhaul saving) must not need a free vehicle (`ReferenceAsync`).
- A plan is an immutable snapshot (`PlanningRun.Plan`); lock flags, assigned vehicle/driver and the run `Log` live in it. Manual changes
  need a comment except a plain re-order. `background: true` runs are calculated by `PlanningWorker` (in-memory queue) as the requesting tenant.
- New planning attributes live on `Order` (`SetPlanningAttributes`), `VehicleType` and `Vehicle`; pairs of incompatible product categories
  are `ProductCompatibilityRule`s, enforced by `CompatibilityPolicy`.
- Exports are built in `PlanExport`; PDF uses the dependency-free `SimplePdf` (Latin text only: transliterate, never emit raw unicode).

## Pricing
Shipments (`docs/shipments.md`): orders → shipments → tender → accept → dispatch (LR numbers) → deliver. Planning rules
(`VehicleSizer`, `ModeAdvisor`, `ConsolidationPlanner`) are pure domain code; prices come only from `IFreightQuoteService`,
fleet paperwork only from `IFleetDirectory`. Vendors see a shipment only once tendered to them and never its price.

Delivery and POD live in the Shipments module (per-order delivery, staff-verified proof, `PodVerified` / `DeliveryExceptionReported` events
for billing and claims). A vendor never verifies its own proof (`shipments.pod.verify` is staff-only).

Planning runs (`docs/planning-module.md`): `IPlanningOptimizer` (rule-based, behind an interface) produces an immutable, versioned
`PlanSnapshot`; re-planning adds a version, never edits one. Never label a heuristic result "optimal".

All freight prices come from `Tms.Modules.Contracts` (`docs/contracts.md`): pure `FreightCalculator` + `RateSelector`.
Approved contracts are immutable (revise instead); "in force" is judged by dates and approval history so past shipments
stay priceable. Money shown next to an invoice must use `formatInrExact` (paise), not `formatInr`.

## Freight rating (Module 5)
Details in `docs/FREIGHT_CONTRACT_MANAGEMENT_INTEGRATION.md` (extends `Tms.Modules.Contracts`, tables stay `contracts_*`). `RatingEngine` is pure:
eligibility → lane specificity → exclusions (each with a code) → best per contract; conflicts are `FREIGHT_RATE_CONFLICT`, never a silent pick.
Pipeline: base → DPH → discount → accessorials → rounding; every result carries lines, reasons, exclusions and a trace. A kept rating stores the
contract/rate/DPH versions and a DPH period snapshot so later changes never alter it. Imports land in a draft revision, never straight to active.
Other modules rate only through `IFreightRatingService`; audit reads `IContractualBaselineService`.

## Transporter performance and selection
Details in `docs/transporter-management.md`. Transporters learns about loads only from Shipments events and
`IShipmentOperationsFeed`; Shipments planning asks `ITransporterPlanningPolicy`. KPIs: carrier-attributed delays only,
no planned time = not measurable, minimum sample renormalises weights, claims lower-is-better. Alerts are evaluated
lazily on read. Subscribers are idempotent.
Tendering to several transporters is a `TenderRound` in Shipments (sequential or broadcast; deadlines settled lazily on read; a vendor never sees
prices or other invitees). Document-paper rules and master lists are per-tenant data over built-in defaults (`DocumentPolicy`, `MasterCatalog`);
never hard-code which papers are required.

## Deliveries and proof of delivery
Module `Tms.Modules.Deliveries` (`docs/POD_DELIVERY_MANAGEMENT_INTEGRATION.md`, tables `pd_*`): delivery execution, versioned PODs, OCR review, exceptions, offline sync.
Delivered is not accepted: delivery status and POD status are separate. Quantities are reported as given and a difference is raised, never absorbed. An accepted POD is
never edited (a correction is a new version). Blame is a finding, never a default (`ResponsibleParty.Unknown`). Mobile commands are idempotent by client key. Evidence files are
only served through authenticated endpoints. OCR reads a digital POD from its text layer and anything else with a local Ollama vision model (`Deliveries:Ocr`); a model's confidence is only believed for a value found in its own transcription. Uploads pass `IFileScanner`. Every delivery step is a published event. Proof ageing, SLA notices and the dashboard are computed lazily on read; "Not applicable" is a null rate, never zero. Claims and freight audit are reached only through `IClaimsIntegration` / `IFreightAuditIntegration` (local stand-in adapters); Transporters learns proof outcomes from Deliveries events. Policy (required evidence, reasons, thresholds, auto-accept) lives in per-tenant settings, never in code. It reads Shipments only through `IShipmentDeliveryFeed`.

## Shipment tracking
Module `Tms.Modules.Tracking` (`docs/SHIPMENT_TRACKING_VISIBILITY_INTEGRATION.md`, tables `st_*`): trips fed by Shipments (`ITrackingPlanningIntegration`), driver-phone GPS, one pipeline
(validate → dedupe → store → route → geofence → deviation → dwell → ETA/risk → alerts/exceptions → events). Tracking health (is the phone reporting?), execution status, delivery risk and POD
status are four separate things; a lost phone is never reported as a parked truck. Suspicious points are stored but never move the vehicle. Alerts are automatic and de-duplicated; an exception needs
a person and is never auto-resolved. Stale/lost and escalation are evaluated lazily on read (`TrackingHealthMonitor`), not by a cross-tenant worker. The customer link is anonymous: only the token's
hash is stored, every failure is a 404, and it exposes no price, note or exception. Thresholds are per-tenant settings, never code. Other modules learn of tracking only through shared-kernel events and feeds.

## Vendor portal (transporter users)
A user of type `Transporter` carries a `trn` claim (`ICurrentUser.TransporterId`). Any module serving transporter-owned data
must scope by it and answer another company's ids with **404, not 403**. In Transporters this lives in `TransporterAccess`;
reuse that pattern (and its tests in `VendorPortalTests`) for shipments, bills and claims.
Files: use `IFileStore`; validate by magic bytes (`FileSniffer`), never trust name or declared content type.
India identifiers (PAN, GSTIN with checksum, IFSC, mobile, vehicle plate) live in `Tms.SharedKernel.India`.

## Web conventions
- Server state via TanStack Query; keys in `lib/api/queryKeys.ts`. HTTP only through `lib/api/client.ts`
  (token attach, single-flight refresh). Errors surface as `ApiError`; map `fieldErrors` onto forms with `applyFieldErrors`.
- UI permission checks (`Can`, `useAuth().can`) are UX only; the API is the authority.
- New pages are lazy-loaded routes + a `navigation.tsx` entry.

## Commands
```bash
cd backend && dotnet build Tms.slnx && dotnet test --solution Tms.slnx
cd web && npm run typecheck && npm test && npm run lint && npm run build
```
Test runner is Microsoft.Testing.Platform (xunit v3) — use `dotnet test --solution/--project`, not VSTest flags.
EF Core is pinned to 9.x because Pomelo (MySQL provider) has no EF Core 10 release yet; upgrade both together.
