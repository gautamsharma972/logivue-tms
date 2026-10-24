#!/usr/bin/env node
// Seeds a transporter-operations demonstration into a DEMO tenant through the public API (no database access).
//   node tools/seed-transportops-demo.mjs
// Environment: TMS_API (default http://localhost:5080), TMS_TENANT (DEMO), TMS_EMAIL (admin@demo.tms), TMS_PASSWORD (the dev admin password).
// Safe to re-run: it stops if the first demonstration customer already exists. Every record is labelled "Demo". Development tenants only.
//
// It builds what a small FMCG transporter would have after a few days: two customers with different detention rules, a CFA, five distributors,
// ten vehicles, delivery requests, LRs, a multi-stop trip, deliveries confirmed through the same QR endpoints a driver's phone uses (arrival inside the
// geofence), one POD uploaded by the driver, one uploaded by the office later, reviewed freight and detention, an invoice, and a simulated portal rejection.
// Arrival times recorded through the QR page are "now" (the server decides them); older deliveries are recorded by a supervisor with a reason, so their detention is predictable.

const API = (process.env.TMS_API ?? 'http://localhost:5080') + '/api/v1'
const OPS = API + '/ops'
let token = ''

async function call(method, url, body, extra = {}) {
  const headers = { 'x-tms-client': 'seed', ...(token && { authorization: 'Bearer ' + token }), ...extra }
  const init = { method, headers }
  if (body instanceof FormData) init.body = body
  else if (body !== undefined) { headers['content-type'] = 'application/json'; init.body = JSON.stringify(body) }
  const response = await fetch(url, init)
  const text = await response.text()
  if (!response.ok) { const e = new Error(`${method} ${url} -> ${response.status} ${text.slice(0, 300)}`); e.status = response.status; throw e }
  return text ? JSON.parse(text) : null
}
const get = (p) => call('GET', OPS + p)
const post = (p, b) => call('POST', OPS + p, b)
const log = (...a) => console.log('•', ...a)
const iso = (d) => d.toISOString()
const ago = (days, hours = 0) => new Date(Date.now() - days * 86_400_000 - hours * 3_600_000)
const dateOnly = (d) => d.toISOString().slice(0, 10)

// A small real PNG (one pixel) is enough for the proof-of-delivery demonstration: the server identifies files by their bytes.
const PNG = Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='), (c) => c.charCodeAt(0))
const file = (name, seed) => { const f = new FormData(); const bytes = new Uint8Array([...PNG, ...new TextEncoder().encode(seed)]); f.append('files', new Blob([bytes], { type: 'image/png' }), name); return f }

token = (await call('POST', API + '/auth/login', {
  tenantCode: process.env.TMS_TENANT ?? 'DEMO', email: process.env.TMS_EMAIL ?? 'admin@demo.tms', password: process.env.TMS_PASSWORD ?? 'Admin@12345678',
})).accessToken

const existing = (await get('/customers?search=DEMO-GODREJ')).items
if (existing.length) { log('already seeded (customer DEMO-GODREJ exists); nothing to do'); process.exit(0) }

await call('PUT', OPS + '/settings', {
  arrival: { maxAccuracyM: 100, tokenValidityDays: 30 }, numbering: { requestPrefix: 'REQ', lrPrefix: 'LR', tripPrefix: 'TRP', digits: 6 }, pod: { maxFiles: 10, maxBytes: 10485760 },
  invoice: { seriesPrefix: 'INV', supplierGstin: null, supplierStateCode: '27', requireApproval: false }, fleet: { operatingTransporterId: null },
})

// ---------------------------------------------------------------- customers
const customer = (code, name, gstin, city, state) => post('/customers', {
  code, name, gstin, billingLine1: 'Demo billing address', billingCity: city, billingState: state, billingPincode: '400079', billingContactName: 'Accounts Payable', billingContactEmail: 'ap@example.invalid', billingContactPhone: '9820000001',
  deliveryContactName: null, deliveryContactPhone: null, commercialContactName: null, commercialContactEmail: null, dueDays: 45, billingRequirements: 'One invoice per PO. Attach the accepted POD.',
  portalMethod: 'Customer vendor portal', portalReference: 'VEND-DEMO', requiredRequestFields: 'CustomerReference', status: 'Active', version: 0,
})
const godrej = await customer('DEMO-GODREJ', 'Demo Godrej Consumer Products (demonstration)', '27AABCG1234C1Z9', 'Mumbai', 'Maharashtra')
const marico = await customer('DEMO-MARICO', 'Demo Marico Limited (demonstration)', '27AABCM1234C1Z3', 'Mumbai', 'Maharashtra')
log('customers:', godrej.code, marico.code)

// ---------------------------------------------------------------- locations
const loc = (code, name, kind, customerId, line1, city, state, pin, lat, lon, radius = 150) => post('/locations', {
  code, name, kind, customerId, line1, city, state, pincode: pin, latitude: lat, longitude: lon, geofenceRadiusM: radius, accessNotes: null, status: 'Active', version: 0,
})
const cfa = await loc('DEMO-CFA-BHIWANDI', 'Demo CFA Bhiwandi (demonstration)', 'Cfa', null, 'Warehouse complex, Bhiwandi', 'Bhiwandi', 'Maharashtra', '421302', 19.2813, 73.0483, 200)
const pune = await loc('DEMO-DIST-PUNE', 'Demo Distributor Pune', 'Distributor', null, 'Chakan Industrial Area', 'Pune', 'Maharashtra', '410501', 18.7606, 73.8636)
const nashik = await loc('DEMO-DIST-NASHIK', 'Demo Distributor Nashik', 'Distributor', null, 'Satpur MIDC', 'Nashik', 'Maharashtra', '422007', 19.9975, 73.7898)
const thane = await loc('DEMO-DIST-THANE', 'Demo Distributor Thane', 'Distributor', null, 'Wagle Estate', 'Thane', 'Maharashtra', '400604', 19.2183, 72.9781, 120)
const surat = await loc('DEMO-DIST-SURAT', 'Demo Distributor Surat', 'Distributor', null, 'Ring Road', 'Surat', 'Gujarat', '395002', 21.1702, 72.8311)
await loc('DEMO-DIST-NOCOORD', 'Demo Distributor without a map point', 'Distributor', null, 'Address only', 'Nagpur', 'Maharashtra', '440001', null, null)
log('locations: 1 CFA, 5 distributors (one without coordinates, to show how arrival cannot be verified)')

// ---------------------------------------------------------------- commercial terms: two customers, two different detention rules
const rate = (from, to, method, r, extra = {}) => ({ originLocationId: from?.id ?? null, originCity: null, originState: null, destinationLocationId: to?.id ?? null, destinationCity: null, destinationState: null, material: null, method, rate: r, slabs: [], minimumCharge: null, ...extra })
const agreement = async (c, title, detention, rates, tax) => {
  const a = await post('/agreements', {
    customerId: c.id, title, effectiveFrom: '2026-04-01', effectiveTo: '2027-03-31', weightBasis: 'Actual', volumetricKgPerCbm: null, minimumFreight: 1500, detention, tax,
    billing: { requireAcceptedPod: true, allowBillingWithoutPod: false, requiredDocuments: 'Signed POD' }, extraCharges: [{ code: 'UNLOAD', name: 'Unloading assistance', amount: 250 }], rates,
  })
  return post(`/agreements/${a.id}/activate`)
}
// Godrej (demonstration): 4 hours free after arrival, then 200 per started hour, up to 2,000.
await agreement(godrej, 'Demo Godrej FY 2026-27', { applicable: true, freeTime: 'Duration', freeAmount: 4, freeUnit: 'Hour', chargeUnit: 'Hour', rate: 200, rounding: 'RoundUp', maxCharge: 2000, note: null },
  [rate(cfa, pune, 'PerKg', 3.5), rate(cfa, nashik, 'PerTonne', 4200, { minimumCharge: 2500 }), rate(cfa, thane, 'FlatPerConsignment', 3000)], { mode: 'Forward', ratePercent: 5, sac: '996511', note: null })
// Marico (demonstration): free until the end of the arrival day (India time), then 1,500 per started day.
await agreement(marico, 'Demo Marico FY 2026-27', { applicable: true, freeTime: 'EndOfDay', freeAmount: 0, freeUnit: 'Hour', chargeUnit: 'Day', rate: 1500, rounding: 'RoundUp', maxCharge: null, note: null },
  [rate(cfa, pune, 'WeightSlabs', 0, { slabs: [{ fromKg: 0, toKg: 1000, ratePerKg: 4.5 }, { fromKg: 1000, toKg: 5000, ratePerKg: 3.8 }, { fromKg: 5000, toKg: null, ratePerKg: 3.2 }] }), rate(cfa, surat, 'PerTonne', 5200)], { mode: 'Forward', ratePercent: 5, sac: '996511', note: null })
log('agreements: two, with different detention rules')

// ---------------------------------------------------------------- requests, LRs, trips
const line = (name, qty, kg, cbm = null, pk = qty) => ({ materialCode: null, materialName: name, quantity: qty, unit: 'PKG', weightKg: kg, volumeCbm: cbm, dimensions: '60x40x40', packages: pk })
async function lrFromRequest(c, to, po, lines, daysAgo) {
  const r = await post('/requests', { customerId: c.id, customerReference: po, receivedAt: iso(ago(daysAgo + 1)), emailReference: `Dispatch request ${po}`, cfaLocationId: cfa.id, destinationLocationId: to.id, requestedDate: dateOnly(ago(daysAgo)), priority: 'Normal', vehicleTypeRequested: '22 ft', instructions: 'Deliver at the main gate', remarks: null, lines, version: 0 })
  await post(`/requests/${r.id}/validate`)
  await post(`/requests/${r.id}/ready`)
  return post('/lrs', { requestId: r.id })
}
const trip = (lrs, reg, driver) => post('/trips', { cfaLocationId: cfa.id, vehicleId: null, vehicleRegistration: reg, transporterId: null, driverName: driver, driverPhone: '9867000000', plannedDate: dateOnly(new Date()), remarks: null, lrIds: lrs.map((l) => l.lr.lr.id) })
const cfaEvents = async (t, start) => {
  const at = (m) => iso(new Date(start.getTime() + m * 60_000))
  for (const [type, m] of [['CfaReached', 0], ['LoadingStarted', 12], ['LoadingCompleted', 55], ['CfaDeparted', 70]]) await post(`/trips/${t.trip.id}/events`, { type, actualAt: at(m), remarks: null })
}
const office = (lr, type, when, reason) => post(`/lrs/${lr.lr.lr.id}/destination-events`, { type, actualAt: iso(when), reason })
const token_ = (c) => c.qrPath.replace('/q/', '')
const qr = (t, tail, body, key) => call('POST', `${API}/public/ops/lr/${t}${tail}`, body, key ? { 'Idempotency-Key': key } : {})

// 1. Multi-stop trip (Godrej to Pune and Thane): delivered 3 days ago. The driver's phone confirmed the Pune arrival through the QR page; Thane was recorded by the supervisor.
const start3 = ago(3)
const l1 = await lrFromRequest(godrej, pune, 'GDJ-PO-1001', [line('Soap cartons', 200, 2400), line('Detergent', 100, 1600)], 3)
const l2 = await lrFromRequest(godrej, thane, 'GDJ-PO-1002', [line('Hair colour cartons', 80, 900)], 3)
const t1 = await trip([l1, l2], 'MH43AB1001', 'Ramesh Patil')
await cfaEvents(t1, start3)
await office(l1, 'DestinationArrival', new Date(start3.getTime() + 5 * 3_600_000), 'Demonstration: arrival recorded by the supervisor for an older delivery')
await office(l1, 'UnloadingCompleted', new Date(start3.getTime() + 11.2 * 3_600_000), 'Demonstration: from the delivery note (6 h 10 min at the gate)')
await office(l2, 'DestinationArrival', new Date(start3.getTime() + 8 * 3_600_000), 'Demonstration: arrival recorded by the supervisor for an older delivery')
await office(l2, 'UnloadingCompleted', new Date(start3.getTime() + 10 * 3_600_000), 'Demonstration: from the delivery note (2 h at the gate)')
// POD for stop 1 is uploaded by the office later; for stop 2 straight away "by the driver" is shown on the live delivery below.
await call('POST', `${OPS}/lrs/${l1.lr.lr.id}/pod`, file('pod-pune.png', 'pune'))
await call('POST', `${OPS}/lrs/${l1.lr.lr.id}/pod/accept`, { reason: 'Signed and stamped' })
await call('POST', `${OPS}/lrs/${l2.lr.lr.id}/pod`, file('pod-thane.png', 'thane'))
await call('POST', `${OPS}/lrs/${l2.lr.lr.id}/pod/accept`, { reason: 'Clear copy' })
log('multi-stop trip', t1.trip.number, 'delivered; POD uploaded by the office and accepted')

// 2. Marico delivery on a slab rate, 3 days late at the gate under the end-of-day rule; POD pending, then rejected and corrected.
const start2 = ago(2)
const l3 = await lrFromRequest(marico, pune, 'MRC-PO-2001', [line('Oil cartons', 300, 3200)], 2)
const t2 = await trip([l3], 'MH43AB1002', 'Imran Shaikh')
await cfaEvents(t2, start2)
await office(l3, 'DestinationArrival', new Date(start2.getTime() + 4 * 3_600_000), 'Demonstration: arrival recorded by the supervisor for an older delivery')
await office(l3, 'UnloadingCompleted', new Date(start2.getTime() + 33 * 3_600_000), 'Demonstration: unloading deferred to the next day by the distributor')
await call('POST', `${OPS}/lrs/${l3.lr.lr.id}/pod`, file('pod-marico-1.png', 'm1'))
await call('POST', `${OPS}/lrs/${l3.lr.lr.id}/pod/reject`, { reason: 'Receiver stamp is missing' })
await call('POST', `${OPS}/lrs/${l3.lr.lr.id}/pod`, file('pod-marico-2.png', 'm2'))
await call('POST', `${OPS}/lrs/${l3.lr.lr.id}/pod/accept`, { reason: 'Corrected copy has the stamp' })

// 3. A delivery in progress, completed through the driver's QR page itself (arrival inside the geofence), POD pending.
const l4 = await lrFromRequest(godrej, pune, 'GDJ-PO-1003', [line('Soap cartons', 150, 1800)], 0)
const t3 = await trip([l4], 'MH43AB1003', 'Sunil More')
await cfaEvents(t3, ago(0, 5))
const tk = token_(l4)
const bad = await qr(tk, '/arrival', { latitude: 18.80, longitude: 73.90, accuracyM: 10, capturedAt: iso(new Date()), locationError: null, clientKey: 'seed-bad' }).catch((e) => e.status)
log('arrival from 6 km away is refused by the server:', bad)
await qr(tk, '/arrival', { latitude: 18.7607, longitude: 73.8637, accuracyM: 12, capturedAt: iso(new Date()), locationError: null, clientKey: 'seed-arrive' }, 'seed-arrive')
await qr(tk, '/unloading', { clientKey: 'seed-unload' }, 'seed-unload')
await qr(tk, '/pod-pending', { clientKey: 'seed-pending' }, 'seed-pending')
log('LR', l4.lr.lr.number, 'delivered through the QR page; POD marked pending by the driver')

// 4. In transit (so the QR page shows Confirm Arrival) and one not yet on a trip.
const l5 = await lrFromRequest(marico, surat, 'MRC-PO-2002', [line('Oil cartons', 120, 1400)], 0)
const t4 = await trip([l5], 'MH43AB1004', 'Anil Shinde')
await cfaEvents(t4, ago(0, 3))
const l6 = await lrFromRequest(godrej, nashik, 'GDJ-PO-1004', [line('Soap cartons', 60, 700)], 0)
log('in transit:', l5.lr.lr.number, '· waiting for a trip:', l6.lr.lr.number)
const tokens = { inTransit: l5.qrPath, waiting: l6.qrPath, pending: l4.qrPath }

// ---------------------------------------------------------------- billing
const calc = async (lr) => (await post('/billing/calculate', { lrIds: [lr.lr.lr.id] })).calculations[0]
const reviewed = []
for (const l of [l1, l2, l3]) {
  const c = await calc(l)
  if (c.status === 'Calculated') reviewed.push(await post(`/billing/calculations/${c.id}/review`, { note: 'Checked against the delivery note' }))
  else log('held:', c.lrNumber, c.holds.join(' '))
}
const inv1 = await post('/invoices', { calculationIds: reviewed.filter((r) => r.customerId === godrej.id).map((r) => r.id), invoiceDate: dateOnly(new Date()), customerReference: 'GDJ-PO-1001/1002', notes: 'Demonstration invoice' })
const inv2 = await post('/invoices', { calculationIds: reviewed.filter((r) => r.customerId === marico.id).map((r) => r.id), invoiceDate: dateOnly(new Date()), customerReference: 'MRC-PO-2001', notes: 'Demonstration invoice' })
await post(`/invoices/${inv1.summary.id}/submit`, { portalReference: 'PORTAL-DEMO-1', documentReference: 'invoice.pdf', notes: 'Uploaded by hand' })
await post(`/invoices/${inv1.summary.id}/reject-submission`, { reason: 'Demonstration: PO number is missing on the invoice' })
await post(`/invoices/${inv1.summary.id}/submit`, { portalReference: 'PORTAL-DEMO-2', documentReference: 'invoice-v2.pdf', notes: 'Resubmitted with the PO number' })
await post(`/invoices/${inv1.summary.id}/acknowledge`, { portalReference: 'ACK-DEMO-77' })
await post(`/invoices/${inv1.summary.id}/payments`, { amount: 5000, receivedOn: dateOnly(new Date()), mode: 'NEFT', reference: 'UTR-DEMO-1', remarks: 'Part payment' })
await post(`/invoices/${inv2.summary.id}/submit`, { portalReference: 'PORTAL-DEMO-3', documentReference: 'invoice.pdf', notes: null })
await post(`/invoices/${inv2.summary.id}/reject-submission`, { reason: 'Demonstration: vendor code does not match the portal' })
log('invoices:', inv1.summary.number, '(accepted, part paid)', inv2.summary.number, '(rejected by the customer, to resubmit)')

console.log('\nTransporter operations demonstration is ready.')
console.log('Open the driver page for a vehicle that is on the road:  ' + tokens.inTransit)
console.log('        a delivered LR with its POD pending:             ' + tokens.pending)
console.log('        an LR waiting for a trip (nothing to do yet):     ' + tokens.waiting)
console.log('Sign in as dispatch@demo.tms / supervisor@demo.tms / billing@demo.tms (password Demo@12345678) after running seed-users-demo.mjs.')
