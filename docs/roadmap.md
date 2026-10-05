# Roadmap — modules 1–8 (current scope)

Status: ✅ done · 🔨 in progress · ⬜ not started · ⏸ deferred

| Phase | Scope | Status |
|---|---|---|
| 0. Foundations | Solution skeleton, tenancy, auth (JWT + rotating refresh), RBAC, audit trail, users/roles admin UI, web shell, tests | ✅ |
| 0a. Pilot readiness | Git + CI + Docker; transactional outbox with retry/dead-letter; email delivery; password change/forgot/reset/forced change; operator tenant provisioning (`tenant:create`) with invitation link; HttpOnly refresh cookie + CSRF header; external (vendor) vs staff roles; AES-GCM encryption of bank account numbers with masked audit; OpenTelemetry traces/metrics | ✅ (Docker files untested: Docker is not installed here) |
| 0b. Approval engine | Policy per document type, ordered steps gated by permission + amount threshold, segregation of duties, time-boxed delegation, inbox UI, completion event (see `docs/approvals.md`). Lane/other attribute conditions not yet supported. | ✅ |
| 0c. Notifications | Email + in-app, templates | ⬜ |
| 6a. Delivery execution & POD | Deliveries, versioned ePOD with signature / one-time code / photos / GPS, shortage and damage, refusals and failed attempts, OCR review workbench, exceptions, offline-first driver screens with idempotent sync (`docs/POD_DELIVERY_MANAGEMENT_INTEGRATION.md`). Dashboard / ageing / SLA / reports and claims, freight-audit and performance hand-offs are not built yet. | 🟡 |
| 1a. Transporters (2) | Vendor master with PAN/GSTIN/IFSC validation, onboarding via the approval engine, vehicles + vehicle types, drivers, compliance documents with expiry tracking and a watchlist, vendor-portal user scoping. **Performance KPIs, scorecards, rankings, eligibility/recommendation, placements, claims, costs, capacity, alerts, lanes/capabilities/planning rules, contacts and branches are ported from the old `transporter-management` branch (`docs/transporter-management.md`); tendering modes (sequential, broadcast, counter-offers, expiry), document rules and master lists are ported too.** | ✅ |
| 1b. Contracts (4) | FTL/PTL/dedicated contracts, city/zone/state lane rates, weight & distance slabs, diesel escalation (DPH) clause with price history, revisions, approval, expiry job + renewal emails, document repository, freight price engine + rate finder. **CSV rate import and detention checks (needs bill audit) are next.** | ✅ |
| 2. Shipments & planning (1) | Orders, shipments (one pickup, many drops), consolidation suggestions + backhaul hints, vehicle sizing, FTL-vs-PTL advice from contract prices, ranked quotes with override reason, tender → vendor accept (vehicle + driver, compliance and overload gated) / decline, dispatch with LR numbers, deliver, utilisation report (see `docs/shipments.md`). **Multi-pickup milk runs, map/road-distance routing, adding orders to a draft in the UI and the vendor-side PTL booking flow are next.** | ✅ |
| 2b. Planning & load optimisation (1) | Versioned planning runs with an explainable rule-based optimizer (payload + volume, FTL/PTL from contract prices, consolidation savings, objectives, unplanned reasons, locks, approval + commit into draft shipments). Stage 1 adds the Locations master, OSRM/estimate routing, ETAs, deadlines and ₹/tonne-km. Stage 2 adds stop sequencing, delivery windows, consolidation economics, return pickups on forward routes and validated manual edits. Stage 3 adds milk-run templates with daily recalculation, the planning KPI dashboard, CSV/Excel export and a demo-data script. Stage 4 adds order/vehicle master data (priority, handling, hazardous, dimensions, availability), compatibility rules, real vehicle + driver allocation without double-booking, locks at four levels, background runs with progress and a planning log, empty-km and cost-per-tonne/shipment KPIs, PDF export and committing milk runs into shipments. **Open:** workbench filters, multi-pickup and a duration limit in normal plans, route restrictions, the remaining hard-coded constants (see `docs/planning-audit.md`). | ✅ Phases 1–4 (open items listed) |
| 3. Procurement (3) | RFQ, spot bids, comparison, approval | ⬜ |
| 4. Delivery (6) | Per-order delivery confirmation (receiver, packages received/damaged), POD upload with staff verify/reject, ageing worklist and report, shortage/damage events for claims, `PodVerified` for billing (see `docs/shipments.md`). **OCR, e-POD by OTP/driver app and delivery corrections are later.** | ✅ |
| 5. Billing & audit (7) | Freight calc from contract, bill verification, excess/duplicate detection, accrual; GST, TDS | ⬜ |
| 6. Claims (8) | Transit damage/shortage claims, approval, insurance, recovery | ⬜ |
| 7. Tracking (5) | Provider adapter (SIM/LBS vs GPS TBD), geofence, ETA heuristic | ⬜ |
| 8. E-way bill | Via a GSP — **deferred by decision** | ⏸ |

Out of scope for now: modules 9–17 (analytics, alerts, integrations, mobile, dashboards, AI, ISO 27001), except what
modules 1–8 need minimally (a small notification layer, basic list views).

## Open decisions
- Tracking source: SIM/cell-tower consent-based tracking vs. driver-app GPS vs. device GPS (affects phase 7 only).
- Order source: manual entry, file import, or ERP (affects phase 2 inputs).
- GST/TDS treatment of freight (GTA, RCM) — confirm with a CA before phase 5.
