# 0002 — Transporter Operations: sell side kept apart, anonymous QR token

Status: accepted

- Contracts is buy-side (tied to a transporter). Customer freight is a separate pure calculator in `Tms.Modules.TransportOps`; nothing in Contracts was generalised.
- The module owns its POD tables and reads other modules only through SharedKernel contracts (`to_*` tables, schema `to`).
- The driver page is anonymous. The QR holds an opaque 32-byte token; only its SHA-256 is looked up, every failure is the same 404, writes take an idempotency key, and the arrival/unloading
  rules are enforced on the server. The token is also stored encrypted so the office can reprint the same code.
- Location is read only at Confirm Arrival and checked on the server against the stop's own radius. It is a device signal, not proof of presence.
- Invoice numbers are gap-free per financial year, allocated inside the invoice transaction. Issued invoices are immutable.
