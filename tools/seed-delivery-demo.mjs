#!/usr/bin/env node
// Seeds delivery execution and proof-of-delivery demo data into a DEMO tenant through the public API (no database access).
//   node tools/seed-planning-demo.mjs   (first: it creates the carriers this uses)
//   node tools/seed-delivery-demo.mjs
// Environment: TMS_API (default http://localhost:5080), TMS_TENANT (DEMO), TMS_EMAIL (admin@demo.tms), TMS_PASSWORD (the dev admin password).
// Adds 50 deliveries across the demo carriers in every state the screens handle: delivered and accepted, waiting for review, a shortage and damage case with a doubtful paper POD,
// rejected and resubmission-needed proofs, failed attempts, customer refusals and open exceptions. Safe to re-run only on a fresh database: it adds new deliveries each time.
// Development tenants only.
import { deflateSync } from 'node:zlib'

const API = (process.env.TMS_API ?? 'http://localhost:5080') + '/api/v1'
const TENANT = process.env.TMS_TENANT ?? 'DEMO'
const EMAIL = process.env.TMS_EMAIL ?? 'admin@demo.tms'
const PASSWORD = process.env.TMS_PASSWORD ?? 'Admin@12345678'

let token = ''
async function call(method, url, body, { form, allow } = {}) {
  const headers = { 'x-tms-client': 'seed', ...(token && { authorization: 'Bearer ' + token }) }
  if (!form) headers['content-type'] = 'application/json'
  const response = await fetch(API + url, { method, headers, body: form ?? (body === undefined ? undefined : JSON.stringify(body)) })
  const text = await response.text()
  if (!response.ok) {
    if (allow?.includes(response.status)) return null
    throw new Error(`${method} ${url} -> ${response.status} ${text.slice(0, 300)}`)
  }
  return text ? JSON.parse(text) : null
}
const log = (...parts) => console.log('•', ...parts)

// ---- files: a readable JPEG header (the server checks size and signature), a drawn PNG signature and a paper POD with a text layer
const jpeg = (seed) => Buffer.concat([Buffer.from([0xff, 0xd8, 0xff, 0xe0, 0, 4, 0, 0, 0xff, 0xc0, 0, 0x11, 8, 0x02, 0x58, 0x03, 0x20, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]), Buffer.from(String(seed)), Buffer.from([0xff, 0xd9])])
function signature() {
  const w = 60, h = 30
  const raw = Buffer.alloc(h * (w * 4 + 1))
  for (let x = 5; x < 55; x++) { const o = 15 * (w * 4 + 1) + 1 + x * 4; raw[o] = 10; raw[o + 1] = 10; raw[o + 2] = 90; raw[o + 3] = 255 }
  const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); return Buffer.concat([len, Buffer.from(type), data, Buffer.alloc(4)]) }
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 6
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))])
}
const paper = (...lines) => Buffer.from('%PDF-1.4\n1 0 obj\nBT /F1 12 Tf ' + lines.map((l) => `(${l}) Tj`).join(' ') + ' ET\nendobj\n%%EOF', 'latin1')

async function evidence(podId, bytes, type, name = 'capture.jpg', mime = 'image/jpeg') {
  const form = new FormData()
  form.set('type', type); form.set('latitude', '18.5204'); form.set('longitude', '73.8567'); form.set('deviceReference', 'demo-phone')
  form.set('file', new Blob([bytes], { type: mime }), name)
  return call('POST', `/pods/${podId}/evidence`, undefined, { form })
}
async function sign(podId, name) {
  const form = new FormData()
  form.set('signerName', name)
  form.set('file', new Blob([signature()], { type: 'image/png' }), 'signature.png')
  return call('POST', `/pods/${podId}/signature`, undefined, { form })
}

const here = { fix: { latitude: 18.5204, longitude: 73.8567, accuracyM: 12 }, deviceReference: 'demo-phone', at: null }
const CUSTOMERS = ['ABC Distributors', 'Surat Retail Hub', 'Vadodara Stores', 'Nashik Distribution', 'Kolhapur Dealer', 'Indore Distributor', 'Ahmedabad Warehouse', 'Hyderabad DC', 'Bengaluru DC', 'Delhi Hub']
const CITIES = ['Surat', 'Vadodara', 'Nashik', 'Kolhapur', 'Indore', 'Ahmedabad', 'Hyderabad', 'Bengaluru', 'Delhi', 'Pune']
const NAMES = ['Anil Kumar', 'Ramesh Patel', 'Sunita Shah', 'Vikram Rao', 'Priya Nair', 'Imran Khan']
const REASONS = ['CUSTOMER_UNAVAILABLE', 'SITE_CLOSED', 'ADDRESS_INCORRECT']

let counter = 0
async function open(carrier, qty = 100, extra = {}) {
  counter++
  const i = counter - 1
  const d = await call('POST', '/deliveries', {
    shipmentReference: extra.shipmentReference ?? `SH-D${String(10000 + counter)}`, orderReference: `INV${9000 + counter}`, loadReference: null, tripReference: null, lrNumber: `LR-D${String(counter).padStart(4, '0')}`, sequence: 1,
    transporterId: carrier.id, transporterReference: carrier.legalName, vehicleId: null, vehicleReference: `MH12AB${String(1000 + counter)}`, driverName: 'Ramesh Yadav',
    customerReference: `CUST-${i % 10}`, customerName: CUSTOMERS[i % 10], customerPhone: '9876543210', customerEmail: `customer${i % 10}@example.test`,
    originReference: 'Pune', destinationReference: CITIES[i % 10], destinationAddress: `Plot ${counter}, ${CITIES[i % 10]}`, customerLatitude: null, customerLongitude: null, geofenceRadiusM: null,
    plannedDeliveryAt: new Date(Date.now() + (counter % 5 - 1) * 3_600_000).toISOString(), windowStart: null, windowEnd: new Date(Date.now() + 8 * 3_600_000).toISOString(),
    items: [{ sku: `SKU-${100 + i % 7}`, description: 'Auto components', orderedQuantity: qty, dispatchedQuantity: qty, unitOfMeasure: 'PKG' }, ...(counter % 3 === 0 ? [{ sku: `SKU-${200 + i % 4}`, description: 'Packaging', orderedQuantity: 40, dispatchedQuantity: 40, unitOfMeasure: 'PKG' }] : [])],
  })
  return d
}
async function arrive(d) {
  await call('POST', `/deliveries/${d.summary.id}/start`, here)
  return call('POST', `/deliveries/${d.summary.id}/arrive`, here)
}
const proof = (method = 'Photo', name = NAMES[counter % NAMES.length], ack = false) => ({ method, recipientName: name, recipientDesignation: 'Store manager', recipientPhone: '9876543210', recipientRemarks: null, driverConfirmed: method === 'Contactless', customerAcknowledged: ack })
const full = (d) => d.items.map((i) => ({ itemId: i.id, deliveredQuantity: i.dispatchedQuantity, shortQuantity: 0, damagedQuantity: 0, rejectedQuantity: 0, shortageReasonCode: null, damageType: null, damageReason: null, damageDescription: null, remarks: null }))
async function complete(d, outcome, items, p = proof(), disposition = null) {
  const done = await call('POST', `/deliveries/${d.summary.id}/complete`, { outcome, items, remainingDisposition: disposition, driverRemarks: 'Delivered at the gate', proof: p, context: here })
  return { delivery: done, podId: done.summary.podId }
}
// A delivery may carry a second, smaller line; the story is told on the largest one.
const biggest = (d) => d.items.reduce((a, b) => (b.dispatchedQuantity > a.dispatchedQuantity ? b : a))
const rest = (d) => { const main = biggest(d); return d.items.filter((i) => i.id !== main.id).map((i) => ({ itemId: i.id, deliveredQuantity: i.dispatchedQuantity, shortQuantity: 0, damagedQuantity: 0, rejectedQuantity: 0, shortageReasonCode: null, damageType: null, damageReason: null, damageDescription: null, remarks: null })) }
let photoSeed = Date.now()
const photo = (podId, type = 'PackagePhoto') => evidence(podId, jpeg(photoSeed++), type)

async function main() {
  const login = await call('POST', '/auth/login', { tenantCode: TENANT, email: EMAIL, password: PASSWORD })
  token = login.accessToken
  const carriers = (await call('GET', '/transporters?status=Active&pageSize=20')).items
  if (carriers.length === 0) throw new Error('No active transporters: run tools/seed-planning-demo.mjs first.')
  const carrier = (n) => carriers[n % carriers.length]

  // 1. Delivered in full with a photo: accepted by itself (12)
  for (let n = 0; n < 12; n++) {
    const d = await arrive(await open(carrier(n)))
    const { podId } = await complete(d, 'Full', full(d), proof(n % 4 === 0 ? 'Signature' : 'Photo'))
    if (n % 4 === 0) await sign(podId, NAMES[n % NAMES.length])
    await photo(podId)
    await call('POST', `/pods/${podId}/submit`)
  }
  log('12 deliveries delivered in full and accepted')

  // 2. Delivered, proof still being prepared (6)
  for (let n = 0; n < 6; n++) {
    const d = await arrive(await open(carrier(n)))
    await complete(d, 'Full', full(d))
  }
  log('6 delivered, proof not yet submitted')

  // 3. The demonstration case: 100 dispatched, 95 delivered, 3 short, 2 damaged; signature, GPS, 4 photos; the paper reads quantity at 64%
  {
    const d = await arrive(await open(carrier(0), 100, { shipmentReference: 'SH10025' }))
    const item = biggest(d)
    const { podId } = await complete(d, 'Shortage', [{ itemId: item.id, deliveredQuantity: 95, shortQuantity: 3, damagedQuantity: 2, rejectedQuantity: 0, shortageReasonCode: 'SHORT_LOADED', damageType: 'BROKEN', damageReason: 'Crushed in transit', damageDescription: null, remarks: null }, ...rest(d)], proof('Signature', 'Ramesh Patel', true))
    await sign(podId, 'Ramesh Patel')
    await photo(podId); await photo(podId, 'SitePhoto'); await photo(podId, 'DamagePhoto'); await photo(podId, 'DamagePhoto')
    await evidence(podId, paper('Shipment Number: SH10025 [98%]', 'Vehicle Number: ' + d.summary.vehicleReference + ' [96%]', 'Delivered Quantity: 92 [64%]', 'Recipient Name: Ramesh Patel [90%]'), 'PodDocument', 'delivery-challan.pdf', 'application/pdf')
    await call('POST', `/pods/${podId}/submit`)
    log('demonstration case ready: SH10025 (95 delivered, 3 short, 2 damaged; the paper POD quantity is doubtful) is waiting in the review queue')
  }

  // 4. Shortage and damage cases waiting for review (5)
  for (let n = 0; n < 5; n++) {
    const d = await arrive(await open(carrier(n + 1), 60))
    const item = biggest(d)
    const damaged = n % 2 === 0
    const items = [{ itemId: item.id, deliveredQuantity: damaged ? 56 : 54, shortQuantity: damaged ? 0 : 6, damagedQuantity: damaged ? 4 : 0, rejectedQuantity: 0, shortageReasonCode: damaged ? null : 'MISSING_IN_TRANSIT', damageType: damaged ? 'CRUSHED' : null, damageReason: damaged ? 'Crushed under load' : null, damageDescription: null, remarks: null }, ...rest(d)]
    const { podId } = await complete(d, damaged ? 'Damaged' : 'Shortage', items, proof('Photo', NAMES[n], true))
    await photo(podId)
    if (damaged) await photo(podId, 'DamagePhoto')
    await call('POST', `/pods/${podId}/submit`)
  }
  log('5 shortage / damage cases waiting for review')

  // 5. Paper PODs: clean (accepted), doubtful (review), unreadable (review) (3 + 2 + 1)
  for (let n = 0; n < 6; n++) {
    const ref = `SH-P${String(20000 + n)}`
    const d = await arrive(await open(carrier(n), 100, { shipmentReference: ref }))
    const { podId } = await complete(d, 'Full', full(d), proof('Photo', 'Anil Kumar'))
    await photo(podId)
    const lines = n < 3 ? [`Shipment Number: ${ref} [98%]`, `Vehicle Number: ${d.summary.vehicleReference} [97%]`, 'Delivered Quantity: ' + d.items.reduce((s, i) => s + i.dispatchedQuantity, 0) + ' [98%]']
      : n < 5 ? [`Shipment Number: ${ref} [91%]`, 'Vehicle Number: MH99ZZ0000 [97%]', 'Delivered Quantity: 100 [70%]'] : ['Nothing legible']
    await evidence(podId, paper(...lines), 'PodDocument', 'delivery-challan.pdf', 'application/pdf')
    await call('POST', `/pods/${podId}/submit`)
  }
  log('6 paper PODs submitted (3 clean, 2 doubtful, 1 unreadable)')

  // 6. Rejected and sent back (3 + 2)
  for (let n = 0; n < 5; n++) {
    const d = await arrive(await open(carrier(n), 80))
    const item = biggest(d)
    const { podId } = await complete(d, 'Shortage', [{ itemId: item.id, deliveredQuantity: 77, shortQuantity: 3, damagedQuantity: 0, rejectedQuantity: 0, shortageReasonCode: 'SHORT_LOADED', damageType: null, damageReason: null, damageDescription: null, remarks: null }, ...rest(d)], proof('Photo', NAMES[n], true))
    await photo(podId)
    await call('POST', `/pods/${podId}/submit`)
    await call('POST', `/pods/${podId}/review`, n < 3 ? { action: 'reject', reason: 'The photo does not show the delivery address' } : { action: 'resubmission', reason: 'Add a photo of the unloading bay' })
  }
  log('5 proofs sent back (3 rejected, 2 asked for more evidence)')

  // 7. Failed deliveries, a failed attempt, refusals, partials (3 + 1 + 3 + 2)
  for (let n = 0; n < 3; n++) {
    const d = await arrive(await open(carrier(n)))
    await call('POST', `/deliveries/${d.summary.id}/fail`, { reasonCode: REASONS[n], remarks: 'Could not deliver', context: here })
  }
  {
    const d = await arrive(await open(carrier(1)))
    await call('POST', `/deliveries/${d.summary.id}/attempt`, { reasonCode: 'CUSTOMER_UNAVAILABLE', driverRemarks: 'Shop shut', customerRemarks: null, recipientName: null, context: here })
  }
  for (let n = 0; n < 3; n++) {
    const d = await arrive(await open(carrier(n)))
    await call('POST', `/deliveries/${d.summary.id}/refuse`, { reasonCode: ['WRONG_ITEM', 'DAMAGED_GOODS', 'LATE_DELIVERY'][n], recipientName: NAMES[n], remarks: 'Not accepted', customerAcknowledged: true, context: here })
  }
  for (let n = 0; n < 2; n++) {
    const d = await arrive(await open(carrier(n), 100))
    const item = biggest(d)
    await complete(d, 'Partial', [{ itemId: item.id, deliveredQuantity: 80, shortQuantity: 0, damagedQuantity: 0, rejectedQuantity: 20, shortageReasonCode: null, damageType: null, damageReason: null, damageDescription: null, remarks: null }, ...rest(d)], proof(), n === 0 ? 'Backorder' : 'Reschedule')
  }
  log('3 failed, 1 failed attempt, 3 refused, 2 partially delivered')

  // 8. Not started yet (assigned / en route)
  for (let n = 0; n < 3; n++) await open(carrier(n))
  for (let n = 0; n < 3; n++) { const d = await open(carrier(n)); await call('POST', `/deliveries/${d.summary.id}/start`, here) }
  log('6 deliveries still to do')

  // 9. Some exceptions owned, escalated and resolved
  const exceptions = (await call('GET', '/delivery-exceptions?pageSize=50&openOnly=true')).items
  if (exceptions[0]) await call('POST', `/delivery-exceptions/${exceptions[0].id}/assign`, { ownerUserId: null, department: 'Customer service', dueAt: null, severity: null })
  if (exceptions[1]) await call('POST', `/delivery-exceptions/${exceptions[1].id}/escalate`, { reason: 'Key account' })
  if (exceptions[2]) await call('POST', `/delivery-exceptions/${exceptions[2].id}/resolve`, { resolution: 'Credit note issued', rootCause: 'Short loaded at the warehouse', responsibleParty: 'Warehouse', actionTaken: 'Raised with the warehouse', financialImpact: 1500, claimReference: null })
  log(`${exceptions.length} open exceptions`)

  // 10. Age some deliveries so the ageing buckets, overdue notices and dashboard have something to show.
  //     Uses the dev-only endpoint, which only exists when the API runs in Development.
  const waiting = (await call('GET', '/deliveries?pageSize=100')).items.filter((x) => ['Pending', 'InPreparation', 'Submitted', 'UnderReview', 'Rejected', 'ResubmissionRequired'].includes(x.podStatus) && ['Delivered', 'PartiallyDelivered'].includes(x.status))
  const AGES = [30, 60, 100, 200, 400, 800]
  let aged = 0
  for (const [n, x] of waiting.entries()) {
    // 204 answers with no body; a 404 means the API is not running in Development, so there is nothing to age with
    const r = await fetch(API + `/dev/deliveries/${x.id}/age`, { method: 'POST', headers: { 'x-tms-client': 'seed', 'content-type': 'application/json', authorization: 'Bearer ' + token }, body: JSON.stringify({ hours: AGES[n % AGES.length] }) })
    if (r.status === 404) break
    if (!r.ok) throw new Error(`age ${x.number} -> ${r.status}`)
    aged++
  }
  log(`${aged} deliveries aged back in time (ageing buckets and overdue notices)`)

  console.log(`\nDone: ${counter} deliveries.\n  Deliveries → Delivery & proof;  proofs → Proofs & review (review queue);  Delivery exceptions;  Delivery rules;  Proof dashboard and Delivery reports show ageing and compliance.\n  The demonstration case is shipment SH10025: open it in the review queue to see the paper POD read at 98% / 96% / 64%.`)
}

main().catch((e) => { console.error(e.message); process.exit(1) })
