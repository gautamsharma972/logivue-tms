# Transporter Operations

For a small transporter that carries FMCG goods from a CFA (depot) to distributors for customers such as Godrej or Marico. It replaces email and spreadsheets
for delivery requests, LRs, trips, delivery proof, customer freight, detention and invoices.
Demonstration data is labelled "DEMO"/"demonstration" and is for a development or demo environment only.

## 1. What it does
Delivery request → LR (one per consignment, with a secure QR) → trip with several stops → CFA timeline (reached, loading, departed) → driver scans the QR on the phone:
**Confirm Arrival** (location checked, only here) → **Confirm Unloading** (no location) → **Upload POD** or **POD not available yet** (no location) → office reviews the POD →
freight and detention are calculated per customer → invoice → submission to the customer's portal, rejection/resubmission → payments and receivables.

## 2. Setting up
1. **Customers** (Transporter Operations → Customers): name, GSTIN (validated), billing address, payment due days, portal method/reference, which request fields are mandatory.
2. **Locations**: the CFA and each distributor, with latitude/longitude and a **geofence radius in metres** (default 150). A location without coordinates cannot verify an arrival; the
   driver is told so and a supervisor must record the arrival with a reason.
3. **Commercial terms** (per customer, effective-dated, approved terms are never edited — revise instead): freight rates by lane (location, city, state, any) and optional material;
   weight basis (actual, volumetric, higher of); per-kg, per-tonne, flat or weight slabs; minimum freight; other charges; **detention policy**; tax settings; billing rules.
4. **Settings**: arrival accuracy limit, POD file limits, LR number format, invoice series, supplier GSTIN/state.

## 3. Day to day
- **Delivery requests**: register manually; keep the customer's email as an attachment; review → incomplete/validated → ready → convert to LRs.
- **LRs**: print the LR page (browser print / save as PDF). The QR encodes only a link; the code can be revoked and reissued.
- **Trips**: choose vehicle and driver (name and phone, no account), order the stops, record CFA events. Corrections need a reason and keep the original.
- **Driver instructions** (also printed on the page): scan the QR; the page opens without asking for location. At the destination tap *Confirm Arrival* and allow location when the phone asks.
  After unloading tap *Confirm Unloading*. Then take photos of the signed POD and upload, or tap *POD not available yet*.
- If arrival is refused (location denied, unavailable, too inaccurate, or outside the radius) the page says why and nothing is recorded. A supervisor with the override permission can record it with a reason; this is audited.
- **POD review**: uploading never accepts a POD. The office accepts or rejects (reason required); every uploaded version and decision is kept. Late PODs can be uploaded by the office.

## 4. Freight, detention and invoices
- **Billing workbench**: for LRs whose POD is accepted, calculate freight (lane, billable weight, rate or slab, minimum, charges) and detention (arrival → unloading, free allowance in hours or days,
  per-hour/day rate, rounding, cap). Missing or invalid times put the LR **on hold for review**, never zero. Every calculation keeps the terms version used and a trace.
- **Invoices**: select eligible LRs of one customer (consolidation allowed). An LR can be on only one active invoice. Tax follows the customer's settings (rate, SAC, forward/reverse charge;
  IGST or CGST+SGST from the GSTIN state codes). Numbers are gap-free per financial year (`INV/26-27/00001`). Optional approval. An issued invoice cannot be changed — cancel and reissue.
- **Submission tracking**: record submission to the customer portal, acknowledgement, rejection with reason, resubmission. **Payments**: record against invoices; part payment, mismatch flagging, receivables ageing.

## 5. Roles and permissions
| Permission | Allows |
|---|---|
| ops.read | View everything in the module |
| ops.dispatch | Requests, LRs, trips, CFA events |
| ops.masters.manage | Customers and locations |
| ops.pod.upload / ops.pod.review | Upload POD for the office / accept or reject |
| ops.arrival.override | Record an arrival that failed the location check |
| ops.commercial.manage | Customer terms |
| ops.billing.calculate | Freight and detention calculation |
| ops.invoice.manage / ops.payment.manage | Invoices and submissions / payments |
| ops.configure | Module settings |

Demo roles: Ops Dispatcher, Ops Supervisor, Ops Billing (`tools/seed-users-demo.mjs`). Demo data: `tools/seed-transportops-demo.mjs` (set `TMS_API`).

## 6. Reports
18 reports under "Transporter Operations" (see REPORTS_AND_ANALYTICS.md), permission `reports.operations`.

## 7. Troubleshooting
| Message | Meaning |
|---|---|
| Link not valid | Unknown, expired or revoked QR (all look the same on purpose) |
| Location not allowed / unavailable / too inaccurate / outside | Arrival not recorded; retry nearer or ask a supervisor |
| Calculation on hold | Missing arrival/unloading time or no terms in force |
| Rate conflict | Two equally specific rates match; fix the terms |
| Already invoiced | LR is on an active invoice |

## 8. Known limitations
- Emails are stored, not parsed. No live customer-portal integration (tracking is manual). No e-invoice/IRN.
- The location check is a device-reported signal; it is **not tamper-proof proof of the vehicle's physical presence**.
- LR print is a browser print page, not a server-made PDF.
- Operations reports have no built-in demonstration data and show only recorded records; the Delivery Request Register lists requests that became LRs.
- Reports show totals and charts; ops KPIs are not in the KPI catalogue yet.
