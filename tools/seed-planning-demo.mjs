#!/usr/bin/env node
// Seeds a realistic planning demo into a DEMO tenant through the public API (no database access).
//   node tools/seed-planning-demo.mjs
// Environment: TMS_API (default http://localhost:5080), TMS_TENANT (DEMO), TMS_EMAIL (admin@demo.tms), TMS_PASSWORD (the dev admin password).
// Safe to re-run: existing locations, carriers, contracts and the milk run are reused, and orders are only added once.
// Intended for development tenants only. It changes approval policies for transporter onboarding and contracts so that
// the demo data does not wait for human approvals; do not run it against a real tenant.

const API = (process.env.TMS_API ?? 'http://localhost:5080') + '/api/v1'
const TENANT = process.env.TMS_TENANT ?? 'DEMO'
const EMAIL = process.env.TMS_EMAIL ?? 'admin@demo.tms'
const PASSWORD = process.env.TMS_PASSWORD ?? 'Admin@12345678'

let token = ''
async function call(method, url, body, { form } = {}) {
  const headers = { 'x-tms-client': 'seed', ...(token && { authorization: 'Bearer ' + token }) }
  if (!form) headers['content-type'] = 'application/json'
  const response = await fetch(API + url, { method, headers, body: form ?? (body === undefined ? undefined : JSON.stringify(body)) })
  const text = await response.text()
  if (!response.ok) throw new Error(`${method} ${url} -> ${response.status} ${text.slice(0, 400)}`)
  return text ? JSON.parse(text) : null
}

const iso = (offsetDays = 0) => new Date(Date.now() + offsetDays * 86_400_000).toISOString().slice(0, 10)
const log = (...parts) => console.log('•', ...parts)

// ---- identity helpers (valid PAN / GSTIN so the real validators accept them)
const ALPHABET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'
function gstinWithCheck(stem) {
  let sum = 0
  for (let i = 0; i < stem.length; i++) {
    const product = ALPHABET.indexOf(stem[i]) * (i % 2 === 0 ? 1 : 2)
    sum += Math.floor(product / 36) + (product % 36)
  }
  return stem + ALPHABET[(36 - (sum % 36)) % 36]
}
const identity = (letters, digits) => { const pan = `${letters}${digits}K`; return { pan, gstin: gstinWithCheck(`27${pan}1Z`) } }

// ---- reference data
const LOCATIONS = [
  ['PUNE-PLANT', 'Pune Plant', 'Plant', 'Chakan MIDC Phase 2', 'Pune', 'Maharashtra', '410501', 18.7603, 73.8631],
  ['MUM-WH', 'Mumbai Warehouse', 'Warehouse', 'Bhiwandi Logistics Park', 'Mumbai', 'Maharashtra', '421302', 19.2967, 73.0631],
  ['C-NASHIK', 'Nashik Distribution', 'Customer', 'Ambad MIDC', 'Nashik', 'Maharashtra', '422010', 19.9975, 73.7898],
  ['C-KOLHAPUR', 'Kolhapur Dealer', 'Customer', 'Shiroli MIDC', 'Kolhapur', 'Maharashtra', '416122', 16.7050, 74.2433],
  ['C-NAGPUR', 'Nagpur Depot', 'Customer', 'Butibori MIDC', 'Nagpur', 'Maharashtra', '441108', 21.1458, 79.0882],
  ['C-SURAT', 'Surat Retail Hub', 'Customer', 'Sachin GIDC', 'Surat', 'Gujarat', '394230', 21.1702, 72.8311],
  ['C-VADODARA', 'Vadodara Stores', 'Customer', 'Makarpura GIDC', 'Vadodara', 'Gujarat', '390010', 22.3072, 73.1812],
  ['C-AHMEDABAD', 'Ahmedabad Warehouse', 'Customer', 'Naroda GIDC', 'Ahmedabad', 'Gujarat', '382330', 23.0225, 72.5714],
  ['C-BENGALURU', 'Bengaluru DC', 'Customer', 'Peenya Industrial Area', 'Bengaluru', 'Karnataka', '560058', 12.9716, 77.5946],
  ['C-HYDERABAD', 'Hyderabad DC', 'Customer', 'Medchal', 'Hyderabad', 'Telangana', '501401', 17.3850, 78.4867],
  ['C-INDORE', 'Indore Distributor', 'Customer', 'Pithampur Sector 1', 'Indore', 'Madhya Pradesh', '454775', 22.7196, 75.8577],
  ['C-DELHI', 'Delhi Hub', 'Customer', 'Bawana Industrial Area', 'Delhi', 'Delhi', '110039', 28.6139, 77.2090],
  ['S-TALEGAON', 'Talegaon Castings (supplier)', 'Supplier', 'Talegaon MIDC', 'Talegaon', 'Maharashtra', '410507', 18.7350, 73.6760],
  ['S-RANJANGAON', 'Ranjangaon Plastics (supplier)', 'Supplier', 'Ranjangaon MIDC', 'Ranjangaon', 'Maharashtra', '412220', 18.7000, 74.2600],
  ['S-HINJEWADI', 'Hinjewadi Electronics (supplier)', 'Supplier', 'Rajiv Gandhi IT Park', 'Pune', 'Maharashtra', '411057', 18.5913, 73.7389],
  ['S-CHAKAN', 'Chakan Fasteners (supplier)', 'Supplier', 'Chakan MIDC Phase 1', 'Chakan', 'Maharashtra', '410501', 18.7710, 73.8480],
]

const VEHICLES_PER_TYPE = 3

const CARRIERS = [
  { name: 'Demo Shree Roadlines Pvt Ltd', trade: 'Shree Roadlines', ident: identity('DEMOA', '1234'), plates: 'MH12DS', drivers: ['Ramesh Yadav', 'Sunil Patil', 'Anil Shinde', 'Vijay More', 'Prakash Jadhav', 'Mahesh Kale'] },
  { name: 'Demo Bharat Cargo Carriers Pvt Ltd', trade: 'Bharat Cargo', ident: identity('DEMOB', '5678'), plates: 'MH14DB', drivers: ['Imran Shaikh', 'Dinesh Pawar', 'Santosh Gaikwad', 'Rahul Deshmukh', 'Ajay Bhosale', 'Nitin Kamble'] },
]

const TYPES_PER_CARRIER = ['ACE', 'PICKUP_1_5T', 'TRUCK_14FT', 'TRUCK_19FT', 'TRUCK_24FT', 'TRUCK_32FT_MXL']

// ---- steps
async function setPolicies() {
  // Contracts and carriers are activated without a human step. Demo tenants only.
  const policies = await call('GET', '/approvals/policies')
  for (const type of ['transporter_onboarding', 'freight_contract']) {
    const current = policies.find((p) => p.documentType === type)
    await call('PUT', `/approvals/policies/${type}`, {
      isActive: true, version: current.version,
      steps: [{ name: 'Demo: only for enormous amounts', requiredPermission: type === 'freight_contract' ? 'contracts.approve' : 'transporters.approve', minAmount: 1_000_000_000_000 }],
    })
  }
  log('approval policies set so demo carriers and contracts activate immediately')
}

async function ensureLocations() {
  const found = new Map()
  for (const [code, name, type, line1, city, state, pincode, latitude, longitude] of LOCATIONS) {
    const existing = (await call('GET', `/locations?search=${encodeURIComponent(code)}&pageSize=5`)).items.find((l) => l.code === code)
    found.set(code, existing ?? await call('POST', '/locations', { code, name, type, line1, city, state, pincode, latitude, longitude, isActive: true, version: null }))
  }
  log(`${found.size} locations ready`)
  return found
}

async function upload(transporterId, ownerKind, ownerId, kind, expiresOn) {
  const form = new FormData()
  form.set('ownerKind', ownerKind); form.set('ownerId', ownerId); form.set('kind', kind)
  if (expiresOn) form.set('expiresOn', expiresOn)
  form.set('file', new Blob(['%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n'], { type: 'application/pdf' }), 'demo.pdf')
  await call('POST', `/transporters/${transporterId}/documents`, undefined, { form })
}

async function ensureCarrier(spec, vehicleTypes) {
  let carrier = (await call('GET', `/transporters?search=${encodeURIComponent(spec.name)}&pageSize=5`)).items.find((t) => t.legalName === spec.name)
  if (!carrier) {
    const created = await call('POST', '/transporters', {
      legalName: spec.name, tradeName: spec.trade, pan: spec.ident.pan, gstin: spec.ident.gstin, contactPerson: 'Dispatch Desk', phone: '9822012345',
      email: `ops@${spec.trade.toLowerCase().replaceAll(' ', '')}.example`, addressLine1: 'Transport Nagar', addressLine2: null, city: 'Pune', state: 'Maharashtra', pincode: '411019',
      serviceModes: ['Ftl', 'Ptl'], version: null,
    })
    const banked = await call('PUT', `/transporters/${created.id}/bank`, { accountHolder: spec.name, accountNumber: '123456789012', ifsc: 'HDFC0001234', bankName: 'HDFC Bank', version: created.version })
    for (const kind of ['PanCard', 'CancelledCheque', 'GstCertificate']) await upload(created.id, 'Transporter', created.id, kind)
    carrier = await call('POST', `/transporters/${created.id}/submit`)
    log(`carrier ${spec.trade} created (${carrier.status})`, banked ? '' : '')
  }

  const existing = (await call('GET', `/transporters/${carrier.id}/vehicles?pageSize=50`)).items
  const expiry = iso(300)
  let n = existing.length
  for (const code of TYPES_PER_CARRIER) {
    const type = vehicleTypes.find((t) => t.code === code)
    // Planning gives every trip its own vehicle, so a carrier needs several of a type for a busy day.
    const have = existing.filter((v) => v.vehicleTypeId === type.id).length
    for (let copy = have; copy < VEHICLES_PER_TYPE; copy++) {
      n++
      const vehicle = await call('POST', `/transporters/${carrier.id}/vehicles`, { registrationNumber: `${spec.plates}${1000 + n}`, vehicleTypeId: type.id, ownership: 'Owned', make: 'Tata', yearOfManufacture: 2023, isActive: true, version: null })
      await upload(carrier.id, 'Vehicle', vehicle.id, 'RegistrationCertificate')
      for (const kind of ['Insurance', 'Fitness', 'Permit']) await upload(carrier.id, 'Vehicle', vehicle.id, kind, expiry)
      // One 32 ft truck is in the workshop for a few days, so the planner has to work around it (vehicle-unavailable scenario).
      if (spec.plates === 'MH14DB' && code === 'TRUCK_32FT_MXL' && copy === VEHICLES_PER_TYPE - 1) {
        await call('PUT', `/vehicles/${vehicle.id}`, {
          registrationNumber: vehicle.registrationNumber, vehicleTypeId: type.id, ownership: 'Owned', make: 'Tata', yearOfManufacture: 2023, isActive: true, version: vehicle.version,
          availability: 'InMaintenance', availableFrom: iso(3), availableTo: null, availabilityNote: 'Gearbox repair',
        })
      }
    }
  }

  const drivers = (await call('GET', `/transporters/${carrier.id}/drivers?pageSize=50`)).items
  for (let i = drivers.length; i < spec.drivers.length; i++) {
    const driver = await call('POST', `/transporters/${carrier.id}/drivers`, { fullName: spec.drivers[i], phone: `98220${10000 + i + (spec.plates === 'MH14DB' ? 50 : 0)}`, licenseNumber: `MH12${2018 + (i % 4)}${String(4000000 + i * 137 + (spec.plates === 'MH14DB' ? 9000 : 0)).padStart(7, '0')}`, isActive: true, version: null })
    await upload(carrier.id, 'Driver', driver.id, 'DrivingLicense', expiry)
  }
  log(`${spec.trade}: ${TYPES_PER_CARRIER.length} vehicles and ${spec.drivers.length} drivers ready`)
  return carrier
}

const state = (s) => ({ kind: 'State', state: s, city: null, zoneCode: null })
const terms = { volumetricKgPerCbm: 250, detentionFreeHours: 24, detentionRatePerHour: 150, loadingCharge: 0, unloadingCharge: 0, multiDropChargePerPoint: 1200, minChargePerConsignment: 0, notes: null }

async function ensureContract(carrier, type, title, rates) {
  const existing = (await call('GET', `/contracts?search=${encodeURIComponent(title)}&pageSize=5`)).items.find((c) => c.title === title)
  if (existing && existing.status !== 'Draft') return existing // a half-finished draft from an interrupted run is completed below
  const draft = existing ? await call('GET', `/contracts/${existing.id}`) : await call('POST', '/contracts', { transporterId: carrier.id, type, title, effectiveFrom: iso(-10), effectiveTo: iso(355), paymentTermsDays: 30, estimatedAnnualSpend: 5_000_000, ownerUserId: null, terms, fuel: null, version: null })
  const withRates = await call('PUT', `/contracts/${draft.summary.id}/rates`, { rates, version: draft.version })
  const active = await call('POST', `/contracts/${withRates.summary.id}/submit`)
  log(`contract ${title} (${active.summary.status})`)
  return active.summary
}

async function ensureContracts(carriers, vehicleTypes) {
  const id = (code) => vehicleTypes.find((t) => t.code === code).id
  const flat = (from, to, code, amount, bothWays = true) => ({ origin: state(from), destination: state(to), bothWays, vehicleTypeId: id(code), minDistanceKm: null, maxDistanceKm: null, pricing: { kind: 'flatTrip', amountPerTrip: amount } })
  const slabs = (from, to, rates, minCharge) => ({ origin: state(from), destination: state(to), bothWays: false, vehicleTypeId: null, minDistanceKm: null, maxDistanceKm: null,
    pricing: { kind: 'weightSlabs', mode: 'Whole', slabs: rates.map(([f, t, r]) => ({ fromKg: f, toKg: t, ratePerKg: r })), minCharge, minChargeableKg: 50 } })

  await ensureContract(carriers[0], 'Ftl', 'Demo FTL: Shree Roadlines', [
    flat('Maharashtra', 'Gujarat', 'TRUCK_14FT', 28_000), flat('Maharashtra', 'Gujarat', 'TRUCK_19FT', 38_000), flat('Maharashtra', 'Gujarat', 'TRUCK_32FT_MXL', 52_000),
    flat('Maharashtra', 'Maharashtra', 'ACE', 4_500), flat('Maharashtra', 'Maharashtra', 'PICKUP_1_5T', 7_500), flat('Maharashtra', 'Maharashtra', 'TRUCK_14FT', 12_000),
    flat('Maharashtra', 'Maharashtra', 'TRUCK_19FT', 17_000), flat('Maharashtra', 'Maharashtra', 'TRUCK_24FT', 21_000), flat('Maharashtra', 'Maharashtra', 'TRUCK_32FT_MXL', 24_000),
    flat('Maharashtra', 'Karnataka', 'TRUCK_14FT', 46_000), flat('Maharashtra', 'Karnataka', 'TRUCK_19FT', 58_000), flat('Maharashtra', 'Karnataka', 'TRUCK_32FT_MXL', 82_000),
    flat('Maharashtra', 'Telangana', 'TRUCK_14FT', 50_000), flat('Maharashtra', 'Telangana', 'TRUCK_32FT_MXL', 90_000),
  ])
  await ensureContract(carriers[1], 'Ftl', 'Demo FTL: Bharat Cargo', [
    flat('Maharashtra', 'Gujarat', 'TRUCK_14FT', 31_000), flat('Maharashtra', 'Gujarat', 'TRUCK_32FT_MXL', 48_000), // cheaper than Shree on the big truck
    flat('Maharashtra', 'Maharashtra', 'TRUCK_14FT', 13_000), flat('Maharashtra', 'Maharashtra', 'TRUCK_32FT_MXL', 22_500),
    flat('Maharashtra', 'Telangana', 'TRUCK_32FT_MXL', 86_000),
  ])
  await ensureContract(carriers[1], 'Ptl', 'Demo PTL: Bharat Cargo', [
    slabs('Maharashtra', 'Gujarat', [[0, 500, 14], [500, 1500, 10], [1500, 3000, 8], [3000, null, 7]], 900),
    slabs('Maharashtra', 'Maharashtra', [[0, 500, 11], [500, 1500, 8], [1500, 3000, 6.5], [3000, null, 5.5]], 600),
  ])
}

// Planning attributes that make the demo interesting: urgent and low-priority orders, products that must not share a truck, a hazardous load.
const PRIORITY = { 'DEMO-022': 'Urgent', 'DEMO-024': 'High', 'DEMO-007': 'High', 'DEMO-016': 'Low', 'DEMO-010': 'Low' }
const CATEGORY = { 'DEMO-009': 'FOOD', 'DEMO-010': 'CHEMICALS', 'DEMO-011': 'FOOD', 'DEMO-014': 'CHEMICALS', 'DEMO-012': 'FMCG', 'DEMO-004': 'ELECTRONICS' }
const HAZARDOUS = new Set(['DEMO-014'])
const RETURN_TYPES = { 'DEMO-R01': ['CustomerReturn', 'Customer rejected the delivery'], 'DEMO-R02': ['DamagedMaterial', 'Damaged in transit'], 'DEMO-R03': ['EmptyPackaging', 'Empty crates to be reused'], 'DEMO-R04': ['RejectedMaterial', 'Failed incoming quality check'], 'DEMO-R05': ['ReplacementPickup', 'Replaced goods sent separately'] }

function orderBody(locations, o) {
  const [ref, pickup, drop, kg, cbm, readyDays, byDays, window, direction] = o
  const from = locations.get(pickup)
  const to = locations.get(drop)
  const party = (l) => ({ name: l.name, line1: l.line1, city: l.city, state: l.state, pincode: l.pincode, contactName: 'Store Manager', contactPhone: '9822000000' })
  return {
    direction: direction ?? 'Forward', reference: ref, pickup: party(from), drop: party(to), weightKg: kg, volumeCbm: cbm, packages: Math.max(1, Math.round(kg / 40)), description: ref.startsWith('DEMO-MR') ? 'Components' : 'Finished goods',
    readyDate: iso(readyDays), deliverByDate: byDays === null ? null : iso(byDays), notes: null, pickupLocationId: from.id, dropLocationId: to.id,
    deliveryWindowFrom: window ? `${window[0]}:00` : null, deliveryWindowTo: window ? `${window[1]}:00` : null,
    priority: PRIORITY[ref] ?? 'Normal', productCategory: CATEGORY[ref] ?? null, handling: ref === 'DEMO-009' ? 'TemperatureControlled' : 'Standard',
    isHazardous: HAZARDOUS.has(ref), isStackable: true, longestItemM: ref === 'DEMO-005' ? 6.5 : null,
    ...((direction ?? 'Forward') === 'Reverse'
      ? { returnType: (RETURN_TYPES[ref] ?? ['SupplierReturn', 'Supplier collection'])[0], returnReason: (RETURN_TYPES[ref] ?? ['SupplierReturn', 'Supplier collection'])[1], pickupWindowFrom: ref.startsWith('DEMO-R0') ? '09:00:00' : null, pickupWindowTo: ref.startsWith('DEMO-R0') ? '17:00:00' : null }
      : {}),
  }
}

// [reference, pickup, drop, kg, cbm, readyInDays, deliverByInDays|null, [window from, to]|null, direction]
const ORDERS = [
  // Pune → Gujarat: several small loads to the same corridor (consolidate), one heavy (needs a big truck)
  ['DEMO-001', 'PUNE-PLANT', 'C-SURAT', 2500, 8, 0, 2], ['DEMO-002', 'PUNE-PLANT', 'C-SURAT', 1800, 6, 0, 2], ['DEMO-003', 'PUNE-PLANT', 'C-VADODARA', 3200, 10, 0, 3],
  ['DEMO-004', 'PUNE-PLANT', 'C-AHMEDABAD', 4000, 14, 0, 3], ['DEMO-005', 'PUNE-PLANT', 'C-AHMEDABAD', 12500, 45, 0, 4], ['DEMO-006', 'PUNE-PLANT', 'C-VADODARA', 900, 3, 0, 3],
  ['DEMO-007', 'PUNE-PLANT', 'C-SURAT', 600, 2, 0, 2, ['10:00', '16:00']], ['DEMO-008', 'PUNE-PLANT', 'C-AHMEDABAD', 2200, 7, 0, 4],
  // Pune → Maharashtra: part loads and mid-size
  ['DEMO-009', 'PUNE-PLANT', 'C-NASHIK', 350, 1.2, 0, 3], ['DEMO-010', 'PUNE-PLANT', 'C-NASHIK', 280, 1, 0, 3], ['DEMO-011', 'PUNE-PLANT', 'C-KOLHAPUR', 450, 1.5, 0, 3],
  ['DEMO-012', 'PUNE-PLANT', 'C-KOLHAPUR', 3800, 12, 0, 2], ['DEMO-013', 'PUNE-PLANT', 'C-NAGPUR', 5500, 16, 0, 4], ['DEMO-014', 'PUNE-PLANT', 'C-NASHIK', 5200, 18, 0, 2],
  ['DEMO-015', 'PUNE-PLANT', 'C-NAGPUR', 1200, 4, 0, 5], ['DEMO-016', 'PUNE-PLANT', 'C-NASHIK', 120, 0.4, 0, 3, ['09:00', '13:00']],
  // Long lanes
  ['DEMO-017', 'PUNE-PLANT', 'C-BENGALURU', 8500, 28, 0, 5], ['DEMO-018', 'PUNE-PLANT', 'C-BENGALURU', 2400, 9, 0, 5], ['DEMO-019', 'PUNE-PLANT', 'C-HYDERABAD', 6000, 20, 0, 5], ['DEMO-020', 'PUNE-PLANT', 'C-HYDERABAD', 950, 3, 0, 6],
  // Orders that cannot be planned, on purpose, each with a clear reason
  ['DEMO-021', 'PUNE-PLANT', 'C-INDORE', 3000, 10, 0, 4],        // no contract rate to Madhya Pradesh
  ['DEMO-022', 'PUNE-PLANT', 'C-DELHI', 7500, 25, 0, 0],         // no rate, and due today
  ['DEMO-023', 'PUNE-PLANT', 'C-AHMEDABAD', 32000, 90, 0, 4],    // heavier than the biggest vehicle
  // Tight but feasible deadline
  ['DEMO-024', 'PUNE-PLANT', 'C-SURAT', 1500, 5, 0, 0],
  // Second origin
  ['DEMO-025', 'MUM-WH', 'C-SURAT', 2000, 7, 0, 2], ['DEMO-026', 'MUM-WH', 'C-NASHIK', 1100, 4, 0, 2], ['DEMO-027', 'MUM-WH', 'C-AHMEDABAD', 3000, 11, 0, 3],
  // Reverse logistics: returns and empties going back to the plant from customers the trucks already visit
  ['DEMO-R01', 'C-SURAT', 'PUNE-PLANT', 700, 3, 0, 5, null, 'Reverse'], ['DEMO-R02', 'C-NASHIK', 'PUNE-PLANT', 450, 2, 0, 5, null, 'Reverse'],
  ['DEMO-R03', 'C-KOLHAPUR', 'PUNE-PLANT', 900, 4, 0, 5, null, 'Reverse'], ['DEMO-R04', 'C-AHMEDABAD', 'PUNE-PLANT', 1500, 8, 0, 6, null, 'Reverse'],
  ['DEMO-R05', 'C-VADODARA', 'PUNE-PLANT', 600, 2.5, 0, 6, null, 'Reverse'],
  // Milk-run inbound: parts from nearby suppliers to the plant (plan these from Planning → Milk runs)
  ['DEMO-MR01', 'S-CHAKAN', 'PUNE-PLANT', 1800, 6, 0, null, null, 'Reverse'], ['DEMO-MR02', 'S-TALEGAON', 'PUNE-PLANT', 2400, 8, 0, null, null, 'Reverse'],
  ['DEMO-MR03', 'S-RANJANGAON', 'PUNE-PLANT', 3100, 10, 0, null, null, 'Reverse'], ['DEMO-MR04', 'S-HINJEWADI', 'PUNE-PLANT', 900, 3, 0, null, null, 'Reverse'],
  ['DEMO-MR05', 'S-CHAKAN', 'PUNE-PLANT', 700, 2, 0, null, null, 'Reverse'],
]

async function ensureOrders(locations) {
  const existing = await call('GET', '/orders?search=DEMO-&pageSize=5')
  if (existing.totalCount > 0) {
    log(`orders: ${existing.totalCount} demo orders already exist, none added`)
    return
  }
  for (const o of ORDERS) await call('POST', '/orders', orderBody(locations, o))
  log(`${ORDERS.length} orders created`)
}

async function ensureCompatibility() {
  const rules = await call('GET', '/planning/compatibility-rules')
  if (rules.some((r) => r.categoryA === 'CHEMICALS' && r.categoryB === 'FOOD')) return
  await call('POST', '/planning/compatibility-rules', { categoryA: 'FOOD', categoryB: 'CHEMICALS', reason: 'Contamination risk' })
  log('compatibility rule FOOD / CHEMICALS added')
}

async function ensureMilkRun(locations, vehicleTypes) {
  const existing = (await call('GET', '/planning/milk-runs?search=DEMO-MR&pageSize=5')).items.find((m) => m.code === 'DEMO-MR-PUNE')
  if (existing) {
    log('milk run already exists')
    return
  }
  const stop = (code, minutes, window) => ({ locationId: locations.get(code).id, type: 'Pickup', serviceMinutes: minutes, windowFrom: window?.[0] ? `${window[0]}:00` : null, windowTo: window?.[1] ? `${window[1]}:00` : null })
  await call('POST', '/planning/milk-runs', {
    code: 'DEMO-MR-PUNE', name: 'Pune supplier collection', depotLocationId: locations.get('PUNE-PLANT').id, vehicleTypeId: vehicleTypes.find((t) => t.code === 'TRUCK_14FT').id,
    maxStops: 6, maxDurationMinutes: 540, departureTime: '07:00:00', days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
    stops: [stop('S-CHAKAN', 25, ['08:00', '11:00']), stop('S-HINJEWADI', 25), stop('S-TALEGAON', 30), stop('S-RANJANGAON', 30, ['09:00', '15:00'])], isActive: true, version: null,
  })
  log('milk run DEMO-MR-PUNE created (Chakan → Hinjewadi → Talegaon → Ranjangaon, back to the Pune plant)')
}

async function main() {
  const login = await call('POST', '/auth/login', { tenantCode: TENANT, email: EMAIL, password: PASSWORD })
  token = login.accessToken
  log(`signed in to ${TENANT} at ${API}`)
  await setPolicies()
  const vehicleTypes = await call('GET', '/vehicle-types')
  const locations = await ensureLocations()
  const carriers = []
  for (const spec of CARRIERS) carriers.push(await ensureCarrier(spec, vehicleTypes))
  await ensureContracts(carriers, vehicleTypes)
  await ensureCompatibility()
  await ensureOrders(locations)
  await ensureMilkRun(locations, vehicleTypes)
  console.log(`
Demo ready. Try this:
  1. Planning → Workbench: select the DEMO-0xx and DEMO-R0x orders (leave DEMO-MR orders for the milk run), choose "Run planning".
     Look at: consolidation savings, FTL vs part load, return pickups on the way back, and the unplanned orders
     (DEMO-021 no rate to Madhya Pradesh, DEMO-022 no rate and due today, DEMO-023 heavier than any vehicle).
  2. Planning → Milk runs → "Plan a day" on Pune supplier collection: stops with nothing to collect are skipped, the vehicle follows the load.
  3. Planning → KPIs for the totals, charts and exports (empty km, cost per tonne and per shipment, PDF).
  4. Planning → Rules shows the FOOD / CHEMICALS rule: DEMO-009/011 (food) and DEMO-010/014 (chemicals) never share a truck.
     One Bharat Cargo 32 ft truck is in the workshop for three days, so the planner works around it (Transporters → Fleet shows why).`)
}

main().catch((e) => { console.error('Seeding failed:', e.message); process.exit(1) })
