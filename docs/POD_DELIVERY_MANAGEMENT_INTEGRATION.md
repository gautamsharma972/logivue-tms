# Delivery execution and proof of delivery: integration notes

Module `Tms.Modules.Deliveries` (tables `pd_*`, web `features/deliveries`). Built to the "Delivery Execution & POD" specification, **milestones 1–8**.
Milestone 7 (dashboard, ageing, SLA monitoring, notifications, reports) and 8 (claims, freight-audit, transporter-performance and planning hand-offs) are built; known limits are at the end.

## What it is
A delivery is one drop of a shipment. The driver starts it, arrives, and completes it with quantities (delivered / short / damaged / rejected). Completing creates the
**proof of delivery (POD)**, a versioned evidence package (recipient, proof method, signature / one-time code / photos / contactless confirmation, GPS, times).
The POD is validated, optionally read from a paper scan (OCR), then accepted automatically or by a reviewer. Delivered is not accepted: they are separate states.

```
Delivery: Planned → Assigned → EnRoute → Arrived ⇄ Attempted → Delivered | PartiallyDelivered | Refused | Failed → Closed (POD accepted)   (Cancelled before arrival)
POD:      Draft → Captured → Submitted → UnderReview → Accepted | Rejected | ResubmissionRequired  → (resubmit) Submitted …   Accepted → correction → new version
```

## Ownership and boundaries
Owns: deliveries, items, attempts, events, discrepancies, PODs (+ items, evidence, signatures, validation results, review actions, OCR results and fields),
exceptions (+ notes), sync records, settings. Does not own orders, planning, transporters, claims or invoices. **No foreign keys to other modules**; it keeps reference
ids and display strings (`ShipmentId`/`ShipmentReference`, `OrderId`, `TransporterId`/`TransporterReference`, `VehicleReference`, `CustomerReference`…).

## Tables (schema `pd`, MySQL `pd_*`)
`pd_deliveries`, `pd_delivery_items`, `pd_delivery_attempts`, `pd_delivery_events`, `pd_delivery_discrepancies`, `pd_pod_records`, `pd_pod_items`, `pd_pod_evidence`,
`pd_pod_signatures`, `pd_pod_validation_results`, `pd_pod_review_actions`, `pd_pod_ocr_results`, `pd_pod_ocr_fields`, `pd_delivery_exceptions`, `pd_exception_notes`,
`pd_pod_sync_records`, `pd_settings`. (The specification's `pd_pod_documents`, `pd_pod_versions` and `pd_pod_notifications` are not separate tables: a paper POD is a
`pd_pod_evidence` row of type `PodDocument`, versions are `pd_pod_records.pod_version` / `supersedes_pod_id`, and there are no notifications yet.)
Every row is tenant-scoped and audited by the platform's automatic audit trail.

## Integration contracts (`Tms.SharedKernel.Contracts`)
| Contract | Direction | Purpose |
|---|---|---|
| `IShipmentDeliveryFeed` (`DeliveryFeed.cs`) | Shipments → Deliveries | Read-only view of a dispatched shipment: vehicle, driver and each drop (customer, address, coordinates, quantities, window, LR number). Implemented by `ShipmentDeliveryFeed`. |
| `ShipmentDispatched` (existing event) | Shipments → Deliveries | `ShipmentDispatchedSubscriber` opens one delivery per drop (idempotent). |
| `ITransporterDirectory`, `IFleetDirectory`, `ISequenceGenerator`, `IFileStore`, `IEmailSender` (existing) | consumed | transporter names, vehicles, numbering, private file storage, the delivery code email. |
| `DeliveryCompleted`, `PodAccepted`, `PodRejected`, `DeliveryExceptionRaised` | Deliveries → anyone | Domain events through the transactional outbox. Subscribed to by Deliveries itself (freight-audit reporting, notices, automatic claims) and by Transporters (`ProofPerformanceSubscribers`). `PodAccepted` carries `AcceptedFirstTime`, short / damaged quantities and `SubmittedWithinSla` / `DeliveredOnTime`. Subscribers are idempotent. |
| `IClaimsIntegration`, `IFreightAuditIntegration`, `IDeliveryReliabilityFeed` (`DeliveryFeed.cs`) | Deliveries → other modules | Hand-offs. The default adapters (`LocalAdapters.cs`) are **stand-ins**: claims are recorded in `pd_claim_handoffs`, freight-audit messages in `pd_integration_messages`. Replace by registering a real implementation. |
| `IPodOcrService` (module-internal contract, public) | replaceable | OCR provider. Default `TextLayerOcrService`. |

Mapping of the specification's interfaces: events go through the existing outbox (the `IEventPublisher` role); validation is the pure `PodValidator` + `PodEngine`; confirmation is
`Delivery.Complete` + `ExecutionHandler`; `IFileStore` is the file-storage abstraction; `IClaimsIntegration` and `IFreightAuditIntegration` exist as named; `INotificationService` is the in-app
notice table (`pd_notifications`, `NotificationPublisher`); `IPodSlaService` is `SlaMonitor` + `AgeingCalculator`; `ITransporterPerformanceIntegration` is **event-based** (Transporters subscribes to
`DeliveryCompleted`, `PodSubmitted`, `PodAccepted`, `PodRejected`, `DeliveryExceptionRaised`; no interface is needed); `IDeliveryPlanningIntegration` is `IDeliveryReliabilityFeed` (lane reliability).

## Shared files modified
`Tms.SharedKernel/Contracts/DeliveryFeed.cs` (new), `Shipments/Integration/ShipmentDeliveryFeed.cs` (new) and its registration in `ShipmentsModule`;
`Tms.Api/Program.cs` (register, map, migrate, health check); `Tms.slnx`; test projects reference the module; `ModuleBoundaryTests` lists it.
Planning and Transporters implementations are untouched. The existing Shipments delivery/POD (per-order delivery and staff-verified proof) **keeps working in parallel**
and is not retired; both can exist until the new module is verified.

## API (all under `/api/v1`)
Deliveries: `GET/POST deliveries`, `GET/PUT deliveries/{id}`, `POST deliveries/{id}/assign|start|arrive|attempt|complete|fail|refuse|reschedule|cancel|close|pod`,
`POST deliveries/{id}/otp/issue|verify`.
Proofs: `GET pods`, `GET pods/review-queue`, `GET pods/{id}`, `GET pods/{id}/review` (the workbench), `PUT pods/{id}/proof`, `POST pods/{id}/submit|resubmit|validate|review|approve|reject|correction`,
`GET/POST pods/{id}/evidence` (multipart, `Idempotency-Key`), `DELETE pods/{id}/evidence/{evidenceId}?reason=`, `POST pods/{id}/signature`, `POST/GET pods/{id}/ocr`, `POST pods/{id}/ocr/review`,
`GET pod-evidence/{id}/file`, `GET pod-signatures/{id}/file` (authenticated streaming; there are no public URLs).
Exceptions: `GET/POST delivery-exceptions`, `GET delivery-exceptions/{id}`, `POST …/acknowledge|assign|investigate|escalate|notes|resolve|close`.
Mobile: `GET mobile/deliveries` (the deliveries to do plus the rules and reason lists for offline use), `POST mobile/sync` (batch of commands).
Settings: `GET delivery-settings`, `PUT delivery-settings/{key}`.
Dashboard: `GET pod-dashboard/summary|ageing|ageing/items|compliance|exceptions`; reports `GET delivery-reports/{deliveries|pods|ageing|shortages|damages|failed|exceptions|compliance|performance}?format=csv|xlsx`.
Notices: `GET delivery-notifications`, `POST delivery-notifications/read-all|{id}/read`.
Hand-offs: `POST deliveries/{id}/claims`, `GET deliveries/{id}/billing`, `GET delivery-reliability?origin&destination&days`, `GET transporters/{id}/proof-performance`.
Errors are RFC 9457 problem JSON with a stable `code` (`deliveries.*`, `pods.*`, `exceptions.*`, `mobile.*`, `ocr.*`, `settings.*`).

## Permissions
`deliveries.read`, `deliveries.manage`, `deliveries.execute` (**external-allowed**: transporter users), `deliveries.pod.review` (staff only), `deliveries.exceptions.manage`,
`deliveries.configure`. A transporter user sees only its own company's deliveries, proofs and exceptions; another company's id answers **404**. A vendor never reviews or accepts proof.

## Offline-first behaviour
The driver screens (`/driver`) keep a copy of their deliveries and rules in IndexedDB and save every action to a queue first. Commands carry a client-chosen key
(`ClientRecordId`) and the device's own time and location; `POST mobile/sync` applies them in order and **remembers each key**, so a batch sent twice (the answer was lost)
changes nothing. A command that no longer fits (the delivery moved on elsewhere) is reported as `Conflict` for the driver to see and is never forced; a bad one is `Failed` and retried.
Photos and signatures upload after the delivery is completed on the server, with the same idempotency, and the proof is submitted last. Sync state is always shown:
saved / pending sync / synced / failed.

## Configuration (per tenant, `pd_settings`, defaults in `DeliverySettingDefaults`)
Evidence required (signature, code, GPS, photo count, geofence, contactless, gallery), quantity tolerance and whether unreconciled quantities block completion, acknowledgement
rules, OCR on/off and confidence thresholds (identifying fields, others, remarks, whole document), automatic acceptance (off for any discrepancy unless allowed), image limits,
SLA hours (submission / review / resubmission, monitored), ageing bucket boundaries (days), whether freight audit holds an invoice until the proof is accepted, automatic claims, exception severity / due / escalation, and every reason list (attempt, shortage, damage types, refusal).

## OCR
Behind `IPodOcrService`; the default **reads the text layer of a PDF or text file** (`Label: value [confidence%]`) and cannot read a photograph: that is reported as a failure
for a person, never guessed. Plug in Textract / Azure / Google by registering another `IPodOcrService`. Reading runs in a background worker (as the submitting tenant) so submission
never waits; each field keeps the provider's reading, and a reviewer's correction sits beside it with an audited reason.

## Dashboard, ageing, notices and hand-offs
- **Ageing** uses exact timestamps (no whole-day rounding) and three stages: waiting for a proof, waiting for review, and rejected / sent back. Buckets default to 0–1, 2–3, 4–7, 8–15, 16–30 and >30 days and are configurable.
- **Not applicable** is returned as `null` when nothing could be measured (e.g. no delivered loads): never a zero, never a failure. Failed and refused deliveries need no proof and are not counted as misses.
- **SLA and notices** are evaluated lazily when the dashboard or the notice list is read (no cross-tenant worker). Each notice is created once per case and kind; the bell in the header shows unread ones.
- **Billing**: `GET deliveries/{id}/billing` reports eligibility and invoice hold from the proof status; "Delivered" is not "billable" while the hold setting is on.
- **Transporter performance**: proof-based rates (on time, proof in time, accepted first time, rejection, shortage, damage, refusals, failures) are shown next to the scorecard. They do **not** change the scorecard weights.

## Known limits
Customer-site geofence configuration screens, PDF generation of the ePOD, S3 storage (the `IFileStore` abstraction supports it), virus-scanning hook, rate limiting beyond the platform's.
Notices are in-app only (no email or SMS); OTP is sent by email only. The default claims and freight-audit adapters are local stand-ins until those modules exist.
The OCR default reads only the text layer. The OCR worker queue is in-memory: a job lost in a restart stays `Queued` and is re-requested with `POST pods/{id}/ocr`.
Demo data: `node tools/seed-delivery-demo.mjs` after `tools/seed-planning-demo.mjs` (50 deliveries; shipment `SH10025` is the 95 / 3 / 2 case with a doubtful paper POD). Against a Development API it also
backdates some deliveries (dev-only `POST dev/deliveries/{id}/age`) so ageing and overdue notices have something to show.
