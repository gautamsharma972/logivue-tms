# Shipment Tracking & Visibility — integration guide

Module `Tms.Modules.Tracking` (schema `st`, tables `st_*`), web under `web/src/features/tracking`. Merge this on top of the Delivery & POD module.

## What it is
Live visibility of every trip: the driver's phone sends GPS fixes (trip-scoped, offline-first, batched, idempotent); one pipeline validates them and works out where the
vehicle is on its route, which places it entered, whether it left the route, how long it stood, when it will arrive and whether that is late; the control tower, a customer link and
reports present it. Four things are kept apart on purpose: **tracking health** (is the phone reporting?), **execution status** (where the trip is), **delivery risk** (will it be late?) and
**POD status** (Deliveries). A vehicle that stopped reporting is "stale" or "lost", never "parked".

## Ownership and boundaries
Tracking owns trips (`TrackedShipment`), sessions, locations, geofences, routes, deviations, dwell, ETA, alerts, exceptions, customer links and settings. It reads **nothing** from
other modules directly. Planning data comes from Shipments through `ITrackingPlanningIntegration`; everything leaves as events or feeds (below). `Tms.ArchitectureTests` enforces this.

## Tables
`st_shipments`, `st_shipment_stops`, `st_tracking_sessions`, `st_tracking_locations`, `st_current_vehicle_positions`, `st_tracking_devices`, `st_shipment_events`, `st_milestones`,
`st_geofences`, `st_geofence_presence`, `st_geofence_events`, `st_routes`, `st_route_deviations`, `st_dwell_events`, `st_tracking_gaps`, `st_eta_predictions`, `st_tracking_alerts`,
`st_tracking_exceptions`, `st_exception_notes`, `st_customer_tracking_links`, `st_settings`, `st_sync_records`. Migration `InitialTracking`.
Differences from the spec's list: there is **no `st_route_progress`** (progress lives on `st_shipments`: the engine overwrites it on every fix and the history is in the locations and ETA
predictions), and **no `st_tracking_audit_logs`** (all writes go to the platform audit log; high-churn live fields are `[AuditIgnore]`).

## Integration contracts (`Tms.SharedKernel.Contracts/Tracking.cs`)
- **In:** `ITrackingPlanningIntegration.GetAsync(tripReference)` → `PlannedTrackingContext` (stops with coordinates and planned times, road geometry, vehicle, driver, carrier). Implemented in Shipments
  (`ShipmentTrackingFeed`); the trip reference is the shipment number. A trip is created the first time it is needed (dispatch, or the driver opening it).
- **Out (events, transactional outbox, subscribers idempotent):** `TrackingStarted/Stopped/Completed/Stale/Lost`, `TrackingVehicleArrived/Departed`, `EnteredGeofence/ExitedGeofence`,
  `RouteDeviationDetected/Resolved`, `ExcessiveDwellDetected`, `UnplannedStopDetected`, `EtaUpdated`, `ShipmentAtRisk`, `ShipmentDelayed`.
- **Transporters** subscribes (`TrackingObservationSubscriber`) and keeps `TrackingObservation`s (idempotent by source event id); `GET /transporters/{id}/tracking-performance` shows tracking
  compliance, deviations and standing time per carrier. Feeds the planning policy later; it does not change scores yet.
- **Deliveries** subscribes (`DeliveryTrackingSiteSubscriber`) to arrival at a site and notes it on the delivery (`SiteReached`), so the driver arriving is not re-asked. Delivered is not accepted:
  Tracking marks the trip delivered from `DeliveryCompleted`, and the POD stays Deliveries' business.
- **Claims:** `ITrackingClaimsIntegration` returns `TrackingEvidence` (route, deviations, gaps, dwell, timeline) for a shipment; Tracking implements it, Claims will consume it.
- **Position feed:** `ITrackingPositionFeed` (vehicle positions, ETAs, actual route) for other modules.

## API (`/api/v1`)
Mobile (permission `tracking.execute`, a vendor sees only its own carrier's trips): `GET mobile/tracking/trips`, `GET mobile/tracking/trips/{trip}`, `POST …/start`, `…/stop`, `…/location`,
`…/location/batch`, `…/status`, `…/sync` (replays everything saved offline; every command carries a client key, repeating it changes nothing).
Staff: `GET tracking/shipments` (+ `/{id}`, `/current-location`, `/timeline`, `/eta`, `/route`, `/locations`, `/exceptions`, `/health`, `/analytics`, `/evidence`, `/links`), `POST …/eta/override`,
`DELETE …/eta/override`, `POST …/milestones`, `POST …/delay-reason`, `POST tracking/deviations/{id}/reason`, `POST tracking/eta/recalculate`; `GET control-tower/{summary,shipments,map,exceptions,vehicles}`;
`GET tracking/vehicles`, `/{vehicle}/current`, `/{vehicle}/history`; geofences CRUD; alerts (list, acknowledge, resolve); exceptions (list, get, acknowledge, assign, escalate, resolve, close, notes);
`POST tracking/links`, `POST tracking/links/{id}/revoke`; `GET/PUT tracking/settings/{key}`; `GET tracking/reports/{report}` (CSV/Excel: shipments, vehicles, deviations, dwell, eta-accuracy, tracking-health, delays,
exceptions, planned-vs-actual). Anonymous: `GET public/tracking/{token}` (rate-limited, every failure is a 404). Live: SignalR hub `/hubs/tracking` (events `position`, `alert`, `exception`, `notification`);
the web falls back to polling every 20 s and says so.

## Permissions
`tracking.read`, `tracking.manage`, `tracking.execute` (ExternalAllowed: the driver), `tracking.configure`, `tracking.geofences.manage`, `tracking.links.manage`. New permissions are staff-only unless marked.

## How a location is processed
validate (accuracy, speed, jumps, timestamps, mock flag, repeats) → dedupe by client id → store (suspicious points are kept but **never** move the vehicle, fire a geofence or feed an ETA; late points are history only)
→ current position → route progress (match to the road, never backwards beyond tolerance) → geofence (needs N consecutive fixes or S seconds, ignores poor accuracy) → deviation → dwell/unplanned stop →
ETA and risk → alerts and exceptions → events → live push. Risk: delay ≤10 min on time, ≤30 at risk, ≤60 delayed, above that severely delayed; confidence is capped at 0.95.

## Alerts and exceptions
An alert is automatic, de-duplicated by a key and resolves itself when its cause clears. An exception needs a person, is raised only by rules marked `CreatesException`, and is **never** auto-resolved
(it shows "cause cleared" instead). Stale/lost detection and escalation are evaluated lazily whenever tracking is read (`TrackingHealthMonitor`, throttled per tenant by `Tracking:HealthCheckMinimumSeconds`,
default 15), not by a cross-tenant worker.

## Configuration (per tenant, `st_settings`, defaults in code)
`interval`, `health`, `validation`, `geofence`, `route`, `dwell`, `eta`, `alerts` (rules and the escalation chain), `retention`, `links`, `milestones`. Edited on the *Tracking rules* page.

## Customer link
32 random bytes, base64url; only the SHA-256 hash is stored and the token is shown once. Anonymous; expiry and revocation; no price, internal exception, note or ranking is exposed. Opens `/track/:token`.

## Demo data
`node tools/seed-planning-demo.mjs` then `node tools/seed-tracking-demo.mjs` (dev API `POST /api/v1/dev/tracking/seed-demo`, Development/Testing only). 21 trips over five lanes, ten vehicles and drivers, seven geofences, GPS trails through the
real pipeline. Scenarios: **SH-10025** off route and ~30 min late with a High exception; **SH-10031** tracking lost after a 36-minute gap; **SH-10040** at risk; **SH-10041** severely delayed; **SH-10042** stale;
**SH-10043** excess standing at the drop; **SH-10044** an unplanned stop; **SH-10056** a deviation that cleared; two completed; two not started; one loading. Re-running adds nothing.

## Shared files changed
`SharedKernel/Contracts/Tracking.cs` (new), `DeliveryFeed.cs`; Shipments (`Routing.cs` `GetGeometryAsync`, `RoutingProviders.cs` OSRM geometry, `ShipmentTrackingFeed`, `ShipmentsModule`);
Transporters (`TrackingObservation`, subscriber, handler, migration `TrackingObservations`, endpoint); Deliveries (`SiteReached`, `TrackingSubscribers`); `Tms.Api` (`Program.cs`, `DevEndpoints.cs`, csproj);
`Tms.slnx`, test projects, `ModuleBoundaryTests`; web `App.tsx`, `navigation.tsx`, `types.ts`, `endpoints.ts`, `queryKeys.ts`; `CLAUDE.md`, `docs/roadmap.md`.

## Differences from the specification (deliberate)
- `ITrackingLocationProvider` takes a whole `LocationBatch`, not one location; milestone, geofence, deviation, dwell and ETA services take an internal `TrackingContext`.
- Trip reference = shipment number (one number for both).
- No `st_route_progress` / `st_tracking_audit_logs` (above).
- Map pins are merged by a small built-in grid cluster, not a clustering library.

## Known limits (said plainly)
- **The browser driver page cannot track in the background**: it works only while open with the screen on (it requests a wake lock). It is a stand-in for a native app; the API is ready for one.
- No server-side map matching: a point is matched to the planned route only. Without a road server (OSRM) the route is a straight-line estimate and the page says so.
- The ETA is rule-based (remaining distance, recent speed, standing time, planned dwell), not a learned model; there is no live traffic.
- A GPS device, telematics or carrier-API provider is not built; each is another class behind `ITrackingLocationProvider`.
- The replay player and the Leaflet tiles use OpenStreetMap's public server: fine for office use, not for heavy traffic.
- Thumbnails and notifications beyond in-app (email, SMS, WhatsApp) are not built; the notification service is an interface with only the in-app implementation.

## Merge notes
Add `Tms.Modules.Tracking` to the solution and the API project, call `AddTrackingModule`, `MapTrackingEndpoints`, `InitialiseTrackingAsync` in `Program.cs`; apply migrations `InitialTracking` (st) and `TrackingObservations` (tp/transporters).
`npm i @microsoft/signalr`. Allow the `access_token` query string for `/hubs/tracking` only (done in `TrackingModule`).
