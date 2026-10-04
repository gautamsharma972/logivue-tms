# Market research and codebase gap analysis

*Prepared 3 Oct 2026. Scope: requirement modules 1–8 only. "Requirement" below means your business-requirements sheet.*

## 0. Corrections to earlier statements

| What I said earlier | What current sources say | Impact |
|---|---|---|
| GTA freight is "5% with no input credit, or 12% with credit" | The 12% option was folded into **18% with full ITC** (GST 2.0); **5% without ITC** continues; default for a GTA that does not opt for forward charge is **5% under reverse charge**, with a **self-invoice** by the recipient. The GTA's choice is fixed for the whole financial year. [taxgarden](https://taxgarden.in/blog/gst-on-transportation-freight-gta-rcm-india-2026) | Billing (module 7) must model the GTA's declared option per financial year, and generate RCM self-invoices. Keep rates in configuration. |
| TDS on freight is "section 194C" | From 1 Apr 2026 it is **section 393(1) Sl.6(i)** of the Income-tax Act 2025; rates unchanged (1% individual/HUF, 2% others; none for ≤10 carriages with PAN declaration). [startupadvisory](https://www.startupadvisory.in/blog-section-194c-tds-on-contractor.htm) | Store the TDS rule by *payment code*, not by old section number. Have a CA confirm. |
| e-way bill deferred | Changes went live **1 Aug 2026**: ship-to GSTIN mandatory, voluntary EWB closure, 180-day invoice limit, 360-day extension cap. [caclubindia](https://www.caclubindia.com/articles/gstns-einvoice-api-and-eway-bill-by-irn-changes-from-1st-august-2026-a-stepbystep-compliance-checklist-for-businesses-55804.asp) | Still deferrable by your decision, but any Indian shipper will expect it, and consignee GSTIN/state/PIN master data needs to be designed for it now. |

All three are tax/regulatory facts from secondary sources. Treat them as inputs for your CA to confirm, not as advice.

## 1. Market snapshot

**Global enterprise tier.** The 2026 Gartner Magic Quadrant for TMS (published 30 Mar 2026, 16 vendors) names five Leaders: **Oracle** (19th year), **Blue Yonder** (19th), **SAP** (12th), **Manhattan** (8th) and **E2open** (4th). [e2open summary](https://www.e2open.com/resources/2026-gartner-magic-quadrant-for-transportation-management-systems), [SAP](https://news.sap.com/2026/04/sap-a-leader-2026-gartner-magic-quadrant-tms/). These are the price/feature ceiling for large multinationals; they are not who a mid-market Indian manufacturer buys first.

**Direction of travel (Gartner and vendor commentary).** AI agents for exception handling, appointment scheduling and freight procurement; predictive dynamic ETAs (vendor-claimed 20–35% better accuracy than static ETAs); predictive carrier selection; convergence with supply-chain orchestration. [Locus](https://locus.sh/blogs/how-ai-is-transforming-transport-management-systems/), [Supply Chain 24/7](https://www.supplychain247.com/article/gartner-top-supply-chain-technology-trends-2026)

**India, shipper-side (our real competitors).** Per [Fretron's 2026 comparison](https://www.fretron.com/blog/best-tms-software-india-2026/) (a competitor's marketing, so read for positioning, not neutrality):

| Vendor | Positioning | What they lead with |
|---|---|---|
| Fretron | Mid-market manufacturers, 100–500 shipments/day | Procurement, dispatch, in-plant logistics, tracking, freight accounting, SAP/Tally |
| SuperProcure | Manufacturers focused on cost | Reverse e-auctions, indent allocation, ePOD, freight accounting, FASTag/Vahan |
| Shipsy | Global multimodal enterprises | TMS+WMS, RFQ, rate contracts, 4-way invoice match |
| Traqo | Small manufacturers (<50 shipments/day) | WhatsApp-native workflow, SIM+GPS tracking, OCR ePOD, e-way bill |
| Pando | $100M+ freight spend | AI carrier selection, consolidation, "AI agents" (~$200K/yr) |
| Freight Tiger | Tata ecosystem | Digital freight network, matching, settlement |
| Locus, FarEye, LogiNext | Last-mile/e-commerce | Route optimisation, delivery tracking — **not** our segment |

**Buyer criteria that recur:** manufacturing depth (procurement → dispatch → accounting as one flow), India readiness (e-way bill, FASTag, weight-based billing, ERP/Tally), implementation in weeks not months, accessible pricing.

**Market size — treat as soft.** Published figures disagree wildly: India TMS at USD 340M (2024) growing 6.75%/yr vs USD 1.04B (2025) growing 20%/yr, in different reports ([IMARC](https://www.imarcgroup.com/india-transportation-management-system-market), [Grand View](https://www.grandviewresearch.com/horizon/outlook/transportation-management-system-market/india)); the wider Indian logistics market is cited at ~USD 230B (2024) → 360B (2030). I would not put any single figure in an investor or customer document without buying the underlying report.

**Where this leaves us.** The credible position is a **mid-market, India-native, shipper-side TMS** that is faster to deploy than SAP/Oracle and deeper on freight *money* (contracts → audit → claims) than the visibility-first tools. That matches your requirement sheet, which is heavy on procurement, contracts, billing audit and claims.

## 2. Competitor expectations vs the requirement, module by module

"Table stakes" = a buyer will assume it. "Differentiator" = wins deals. "Beyond requirement" = competitors have it, your sheet doesn't ask for it.

| Module | Table stakes (competitors) | Differentiator | Beyond requirement (do not build now) | Our status |
|---|---|---|---|---|
| 2 Transporter mgmt | Onboarding workflow, documents with expiry, vendor portal | **Auto-verification of RC/fitness/insurance/permit and licence via ULIP (Vahan/Sarathi)** — used by large shippers for pre-dispatch checks ([SuperProcure](https://www.superprocure.com/blog/ulip-api-impact-on-indian-logistics-comprehensive-discussion/), [TCIL](https://tcil.com/blog/ulip-building-indias-digital-backbone-for-integrated-logistics/)); scorecards | Transporter marketplace/network | **Built** (master, onboarding+approval, fleet, documents, vendor scoping). Scorecards, placement and OTP/POD tracking wait for shipments. Manual document entry today; ULIP verification not built. |
| 3 Procurement & bidding | RFQ, spot booking, rate comparison, approval | **Reverse e-auctions, indent-to-transporter allocation by rules (share-of-business)** | AI carrier agents | Not started. Approval engine ready. |
| 4 Contracts | Lane/zone rate cards, slabs, validity | **Diesel-price (DPH) escalation formula, PTL vs FTL recommendation** | Contract analytics AI | Not started. Riskiest data model. |
| 1 Planning | Load→vehicle allocation, consolidation | Multi-drop/milk-run optimisation | Last-mile route optimisation (Locus/FarEye territory) | Not started. |
| 5 Tracking | GPS + FASTag, geofence, ETA | **SIM-based fallback** (Airtel/Jio/Vi), dynamic ETA | WhatsApp-first driver UX | Not started. Provider undecided. |
| 6 POD | ePOD, ageing | **OCR POD; ePOD triggers billing** ("delivered but not invoiced" gap) | — | Not started. |
| 7 Billing & audit | Contract-rate validation, duplicate detection | **Multi-point / 4-way match** (contract, weight, distance, POD); detention validated against an event; weight discrepancy (loading vs unloading) | Payment-rails integration | Not started. Domain design should follow section 4. |
| 8 Claims | Damage/shortage claim workflow, insurance tracking | Root-cause analytics, recovery ageing | — | Not started. |

## 3. Codebase audit against enterprise standard

### Measured facts

- 7,073 lines of backend source (excluding migrations), 2,723 lines of tests, 4,365 lines of web source. 49 API endpoints, 12 permissions.
- **168 backend tests** (unit, architecture, integration on real MySQL) and **19 web tests**, all passing.
- **0 vulnerable or deprecated NuGet packages; `npm audit` reports 0 vulnerabilities.**
- Strict compiler settings (warnings are errors), architecture tests enforcing module boundaries, central package management, RFC 9457 errors, optimistic concurrency, audit trail, tenant isolation proven by tests.

### Gaps, by severity

Verified by search of the source (not assumed). "Absent" means zero matches in the code.

**P0 — blocks a first real customer**

| # | Gap | Evidence | Why it matters |
|---|---|---|---|
| 1 | **No way to create a tenant** other than the development seeder | no tenant endpoint/command | Cannot onboard a customer. |
| 2 | **No password change, forgot/reset, or first-login change** | 0 matches | Admins set initial passwords that users can never change. |
| 3 | **Not under version control; no CI; no Dockerfile/compose; no IaC** | no `.git`, `.github`, `Dockerfile` | No repeatable build, no review trail, nothing deployable. |
| 4 | **No notification delivery** (email/SMS) | 0 matches | Approvals, invites and expiry alerts are in-app only; module 10 and half of module 2/4 need it. |
| 5 | **No background job runner** | 0 matches | Contract expiry, document expiry, accruals, ageing all need scheduled work. |
| 6 | **Event handling is not reliable**: domain events run in-process after commit with no outbox/retry | 0 matches for outbox | If the subscriber fails, e.g. after an approval is decided, the transporter can stay "Pending" forever while the approval says "Approved". |

**P1 — needed before a production pilot / security review**

| # | Gap | Evidence |
|---|---|---|
| 7 | Refresh token in `localStorage` (XSS-stealable); should be an HttpOnly same-site cookie | `session.ts` |
| 8 | JWT signed with a shared symmetric key (HS256), no key rotation | `TokenService` |
| 9 | **Bank account numbers and PAN stored in plaintext** (no field-level encryption); DPDP "reasonable security safeguards" will be judged on this | 0 encryption references |
| 10 | No MFA, no SSO (OIDC/SAML) — large buyers' IT will ask on day one | 0 matches |
| 11 | No observability: only logs; no metrics or distributed tracing | 0 matches |
| 12 | No idempotency keys on POST (double-submit creates duplicates; matters most for payments/bills) | 0 matches |
| 13 | Rate limiting only on login; no global/per-tenant limit | no global limiter |
| 14 | Permission claims live in the token: role changes take up to 15 min to apply and tokens grow with the catalogue | design (ADR 0001) |
| 15 | Vendor users can be given internal roles (no "external role" concept) | noted earlier |
| 16 | Development credentials/keys are in a committed config file (local-only, but a pattern to remove before git) | `appsettings.Development.json` |
| 17 | No test coverage measurement, no load/performance test, no automated browser E2E (I ran browser checks by hand) | none in tooling |
| 18 | No backup/restore or DR procedure written or rehearsed (requirement #16 asks for it) | none |

**P2 — quality and scale**

Caching absent (fine at this size). No soft-delete/retention policy (DPDP requires purpose-based retention). Audit log has no tamper-evidence or archival. No i18n (Hindi/regional) or formal accessibility audit. `dotnet ef` migrations run at startup in development only (correct) but there is no production migration job yet. Vehicle types should become a shared master-data concern before contracts. OpenAPI-generated TypeScript client not yet used (types are hand-mirrored).

### Status update (4 Oct 2026) — pilot-readiness block

| Gap | Status |
|---|---|
| 1 Tenant provisioning | **Done** — `tenant:create` operator command; no passwords generated, admin gets a single-use invitation link |
| 2 Password change / forgot / reset / forced change | **Done** (reset revokes all sessions and clears lockout) |
| 3 Git / CI / Docker | **Done**: git initialised (no commits made), GitHub Actions workflow, Dockerfiles, compose. **Docker images not built or run here (Docker not installed)** |
| 4 Notification delivery | **Partly**: email abstraction (SMTP via MailKit, log provider for dev) used by password flows. Approval/expiry notification *content* is still to build |
| 5 Background jobs | **Partly**: hosted outbox worker exists; scheduled domain jobs (expiry scans, accruals) still to build |
| 6 Reliable events | **Done** — transactional outbox, inline delivery then worker retry with backoff, dead-lettering, metrics |
| 7 Refresh token in localStorage | **Done** — HttpOnly SameSite=Strict cookie scoped to /api/v1/auth, CSRF header required |
| 9 Plaintext bank account | **Done** — AES-256-GCM with key ids for rotation; audit shows last 4 only. PAN remains plaintext (searchable identifier) |
| 15 Vendor users holding staff roles | **Done** — role audience (staff/external), external roles limited to permissions marked external-allowed |
| 11 Observability | **Done** — OpenTelemetry traces/metrics, OTLP export when configured |
| 8 JWT HS256 shared key / rotation, 10 MFA/SSO, 12 idempotency keys, 13 global rate limit, 14 token-borne permissions, 17 coverage/E2E/load tests, 18 backup/DR | **Open** |

### Compliance horizon (India)

- **DPDP Rules 2025** were notified 13 Nov 2025; **substantive duties apply from 14 May 2027**: itemised notices, purpose-based retention, reasonable security safeguards, breach reporting (Board immediately, affected individuals within 72 hours). As a SaaS vendor you are a data processor but the customer remains accountable, so they will push obligations to you contractually. [EY](https://www.ey.com/en_in/insights/cybersecurity/transforming-data-privacy-digital-personal-data-protection-rules-2025), [K&S](https://ksandk.com/data-protection-and-data-privacy/dpdp-data-breach-notification-timeline/)
- Driver data (phone numbers, licence numbers, **location**) is personal data. SIM-based tracking is consent-driven by design (SMS/IVR/missed-call consent with stored proof); accuracy is roughly 50–100 m urban, 200–500 m highway, and can degrade to 2 km on sparse rural networks. [Telenity](https://www.telenity.com/location-based-services-india/solutions/api-to-track-sim/), [dotmove](https://www.dotmove.in/sim-based-tracking-india). Plan consent capture, retention and a purpose limit *before* building module 5.

## 4. Recommendations

### A. Insert a short "pilot readiness" block before Contracts (≈ 3–4 weeks)

In order: (1) git + CI (build, test, `dotnet list package --vulnerable`, `npm audit`) + Dockerfile/compose; (2) tenant provisioning command + admin bootstrap; (3) password change/forgot/reset and forced change on first login; (4) background-job host + email delivery (one provider interface, SMTP implementation); (5) **outbox + retry for domain events**; (6) refresh token to HttpOnly cookie; (7) OpenTelemetry traces/metrics; (8) field encryption for bank account/PAN via a key from the environment; (9) "external role" flag so vendor users cannot hold internal roles.

None of this is a requirement-sheet feature, but items 1–5 are prerequisites for requirement items that *are* (approval workflow notifications, alerts, expiry reminders, SaaS controls in module 16).

### B. Feature order inside modules 1–8, informed by competitors

1. **Contracts (4)** — model rate cards as versioned, effective-dated; slabs by weight and distance; DPH escalation as a stored formula + dated diesel index; pure, heavily unit-tested `calculateFreight`. Everything in 1, 3 and 7 depends on this.
2. **Shipments + LR + planning (1)**, with manual allocation first, PTL-vs-FTL as a rule on contract cost.
3. **Procurement (3)** — RFQ and spot first; reverse auction and share-of-business allocation are the proven India differentiators, so design for them.
4. **POD (6)** — ePOD upload; POD arrival as the trigger that makes a shipment billable. OCR later.
5. **Billing & audit (7)** — design the match as *contract × actual weight × distance × POD*, with detention accepted only against a recorded event; duplicate detection on transporter + LR + amount, not invoice number alone ([general practice](https://gingercontrol.com/blog/freight-invoice-audit-guide)); GTA option per financial year; RCM self-invoice; TDS by payment code.
6. **Claims (8)**, then **Tracking (5)** once the provider and consent model are decided.

### C. Optional additions that stay inside the requirement

- **ULIP/Vahan/Sarathi verification** — falls under requirement 11 (integrations) and directly strengthens requirement 2 (compliance). It turns manual document entry into verified data. Needs ULIP onboarding; I have not verified access terms.
- **E-way bill via a GSP** — your call to defer stands, but it is table stakes for Indian shippers.

### D. Things the research suggests you should *not* build now

Last-mile route optimisation, a transporter marketplace, WhatsApp-native workflows and agentic AI. Competitors have them, your requirement sheet does not, and module 15 (AI) is out of the current scope.

## 5. Limits of this research

- Vendor comparisons come largely from competitors' own blogs and so favour their authors. I did not trial any product.
- The Gartner report itself is paywalled; only vendor press summaries were available.
- Market-size figures conflict and are unverified.
- Tax and regulatory points come from secondary sources and need professional confirmation.
- The code audit is by inspection and automated checks, not a penetration test or a load test.
