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
| `DeliveryAssigned`, `DeliveryStarted`, `VehicleArrived`, `DeliveryAttempted`, `DeliveryCompleted`, `DeliveryPartiallyCompleted`, `DeliveryFailed`, `CustomerRefused`, `ShortageRecorded`, `DamageRecorded`, `PodCaptured`, `PodSubmitted`, `PodValidated`, `PodRejected`, `PodResubmissionRequested`, `PodAccepted`, `DeliveryExceptionRaised`, `DeliveryExceptionResolved` | Deliveries → anyone | The full event list of the specification, through the transactional outbox. `PodValidated` is raised on every validation run. Subscribed to by Deliveries itself (freight-audit reporting, notices, automatic claims) and by Transporters (`ProofPerformanceSubscribers`). `PodAccepted` carries `AcceptedFirstTime`, short / damaged quantities and `SubmittedWithinSla` / `DeliveredOnTime`. Subscribers are idempotent. |
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
Deliveries: `GET/POST deliveries` (filters: status, proof status, customer, transporter, vehicle, lane, service type, dates, has exception / discrepancy), `GET/PUT deliveries/{id}`, `GET deliveries/{id}/items`, `POST deliveries/{id}/items/reconcile` (a dry run: nothing is saved or corrected), `POST deliveries/{id}/assign|start|arrive|attempt|complete|fail|refuse|reschedule|cancel|close|pod`,
`POST deliveries/{id}/otp/issue|verify`.
Proofs: `POST pods` (starts the proof of a completed delivery), `GET pods`, `GET pods/review-queue`, `GET pods/{id}`, `GET pods/{id}/review` (the workbench), `PUT pods/{id}/proof`, `POST pods/{id}/submit|resubmit|validate|review|approve|reject|correction`,
`GET/POST pods/{id}/evidence` (multipart, `Idempotency-Key`), `DELETE pods/{id}/evidence/{evidenceId}?reason=`, `POST pods/{id}/signature`, `POST/GET pods/{id}/ocr`, `POST pods/{id}/ocr/review`,
`GET pod-evidence/{id}/file`, `GET pod-signatures/{id}/file` (authenticated streaming; there are no public URLs).
Exceptions: `GET/POST delivery-exceptions`, `GET delivery-exceptions/{id}`, `POST …/acknowledge|assign|investigate|escalate|notes|resolve|close`, `POST …/attachments` (multipart; a photo or PDF kept with the exception), `GET exception-attachments/{id}/file`.
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
Behind `IPodOcrService`; reading runs in a background worker (as the submitting tenant) so submission never waits, and each field keeps the provider's reading with a reviewer's correction beside it.
A document is read by `CompositePodOcrService`:
1. **Text layer** (`TextLayerOcrService`): a digital POD that uses the labelled `Label: value [96%]` layout is read exactly, with no model.
2. **Ollama** (`OllamaPodOcrService`, off unless `Deliveries:Ocr:Enabled` is true): anything else goes to a local vision-language model, so the document never leaves the machine.
   Settings: `Deliveries:Ocr:{Enabled, BaseUrl (http://localhost:11434), Model (qwen2.5vl:7b), TimeoutSeconds}`. `appsettings.Development.json` enables it.
   Setup: `ollama pull qwen2.5vl:7b` (about 6 GB; 16 GB of RAM is enough). A photograph is sent as an image, a scanned PDF has its embedded page picture taken out (best effort), a PDF with plain text is sent as text.
   Any other Ollama vision model can be named in `Model` (for example `llama3.2-vision`, `minicpm-v` or `gemma3`); Qwen2.5-VL was the strongest document reader of those available.
3. Neither works: the failure is reported for a person, never guessed.

**Confidence from a model is not trusted as given.** The model is asked to transcribe the page first; a field counts as *read* only if its value appears in that transcription (capped at 97%, because a model is never as sure as a text layer).
A value that does not appear in the transcription was inferred and is capped at 40%, below every review threshold. Quantities are reduced to their number ("95 cartons" → 95). The reconciliation with the delivery and the human review stay the real controls.
Measured on one synthetic challan photograph on an Apple M5 / 16 GB: about 40 s for the first read (the model loading), 20–40 s after; every field was read correctly. That first check was one document.

**Benchmark on 11 synthetic pages** (`tools/ocr-benchmark`: clean, free text, skewed scan, low-res fax, handwritten form, phone photo, scanned PDF, Hindi/English form, sparse page, stamp over text, all handwritten; 103 fields with known answers):
every expected field was read correctly (103 of 103), nothing was missed, and 16–72 s per page after the first load. What went wrong along the way, and what each fix was:
- The model filled a field with a value that belongs to another (invoice number as delivery number, a vehicle plate as transporter), or read the page title ("DELIVERY CHALLAN") as a delivery number, with full confidence. Fixes: per-field definitions in the prompt; a value used for two fields holds both below the review threshold; a code field with no digit is held.
- It returned the receiver's company instead of the person, and "No damage noted." as a damage remark. Fixed in the prompt and by dropping "no damage" answers.
- At temperature 0 it sometimes looped ("token repeat limit reached") or ran for minutes on a dense page. Reads now use a small temperature, are retried warmer up to three times, and are capped at 2048 tokens.
- On a form with handwriting its transcription skipped the handwritten words, so every value was held for review (safe, but useless). The prompt now asks for handwritten and stamped text.
- Dates written "05 10 2026", "05.10.2026", "05-Oct-26" and similar were compared as different from the system date; they are now parsed.
These are generated pages. Real challans (crumpled, stamped, faint, mixed scripts) will do worse; run your own before relying on it, and keep the human review for anything a person would not trust.

## Files
Uploads (proof evidence and exception attachments) are identified by their bytes (`FileSniffer`), size-limited, and then passed to `IFileScanner` (`Tms.SharedKernel.Files`) before they are stored. The default `NoFileScanner` scans nothing:
register a ClamAV / cloud scanner to turn it on; a file it refuses is rejected with `pods.file_infected` / `exceptions.file_infected`. Files are served only through authenticated endpoints.

## Dashboard, ageing, notices and hand-offs
- **Ageing** uses exact timestamps (no whole-day rounding) and three stages: waiting for a proof, waiting for review, and rejected / sent back. Buckets default to 0–1, 2–3, 4–7, 8–15, 16–30 and >30 days and are configurable.
- **Not applicable** is returned as `null` when nothing could be measured (e.g. no delivered loads): never a zero, never a failure. Failed and refused deliveries need no proof and are not counted as misses.
- **SLA and notices** are evaluated lazily when the dashboard or the notice list is read (no cross-tenant worker). Each notice is created once per case and kind; the bell in the header shows unread ones.
- **Billing**: `GET deliveries/{id}/billing` reports eligibility and invoice hold from the proof status; "Delivered" is not "billable" while the hold setting is on.
- **Transporter performance**: proof-based rates (on time, proof in time, accepted first time, rejection, shortage, damage, refusals, failures) are shown next to the scorecard. They do **not** change the scorecard weights.

## Known limits
Not built: PDF generation of the ePOD, thumbnails and image compression (the browser scales images; there is no server-side imaging library), customer-site geofence configuration screens, a customer-facing portal,
a customer-facing QR code (a QR proof method exists: the driver scans the QR the customer shows, which holds the delivery code, and it is verified exactly like the typed code; the browser must offer `BarcodeDetector`, otherwise the code is typed), and S3 storage (the `IFileStore` abstraction supports it).
Times are stored in UTC and the business day is fixed to India Standard Time (the product is India-only; there is no per-tenant timezone). Rate limiting is only the platform's.
Notices are in-app only (no email or SMS); OTP is sent by email only. The default claims and freight-audit adapters are local stand-ins until those modules exist.
The OCR default reads only the text layer. The OCR worker queue is in-memory: a job lost in a restart stays `Queued` and is re-requested with `POST pods/{id}/ocr`.
Demo data: `node tools/seed-delivery-demo.mjs` after `tools/seed-planning-demo.mjs` (50 deliveries; shipment `SH10025` is the 95 / 3 / 2 case with a doubtful paper POD). Against a Development API it also
backdates some deliveries (dev-only `POST dev/deliveries/{id}/age`) so ageing and overdue notices have something to show.
