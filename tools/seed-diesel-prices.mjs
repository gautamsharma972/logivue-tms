#!/usr/bin/env node
// Seeds the fuel price index (diesel, ₹ per litre) for every Indian state and union territory, the main cities,
// the six macro regions and "National", through the public API. The same prices feed Rate masters → Diesel prices
// and the DPH rules of contracts.
//   node tools/seed-diesel-prices.mjs
// Environment: TMS_API (default http://localhost:5080), TMS_TENANT (DEMO), TMS_EMAIL (admin@demo.tms), TMS_PASSWORD (the dev admin password),
//              TMS_PRICE_DATE (default 2026-04-01: the price applies from that day until a later price is recorded).
// Safe to re-run: a price that is already recorded for a region and date is skipped.
//
// The prices are INDICATIVE retail pump prices for demonstration (rounded, close to recent levels). They are not live
// quotes. Record the actual pump price of the day with a newer date whenever it changes; an adjustment always reads the
// latest price on or before the shipment date.

const API = (process.env.TMS_API ?? 'http://localhost:5080') + '/api/v1'
const DATE = process.env.TMS_PRICE_DATE ?? '2026-04-01'
const SOURCE = 'Indicative pump price - replace with the current OMC price'

// State / union territory -> diesel price per litre (₹).
const STATES = {
  'Andaman and Nicobar Islands': 82.46,
  'Andhra Pradesh': 97.0,
  'Arunachal Pradesh': 83.0,
  'Assam': 91.9,
  'Bihar': 93.7,
  'Chandigarh': 82.45,
  'Chhattisgarh': 92.0,
  'Dadra and Nagar Haveli and Daman and Diu': 88.0,
  'Delhi': 87.62,
  'Goa': 88.0,
  'Gujarat': 90.4,
  'Haryana': 88.1,
  'Himachal Pradesh': 88.0,
  'Jammu and Kashmir': 88.9,
  'Jharkhand': 92.0,
  'Karnataka': 89.2,
  'Kerala': 95.5,
  'Ladakh': 87.8,
  'Lakshadweep': 90.4,
  'Madhya Pradesh': 91.8,
  'Maharashtra': 92.2,
  'Manipur': 88.2,
  'Meghalaya': 89.3,
  'Mizoram': 88.6,
  'Nagaland': 88.5,
  'Odisha': 92.0,
  'Puducherry': 87.9,
  'Punjab': 88.5,
  'Rajasthan': 90.6,
  'Sikkim': 90.0,
  'Tamil Nadu': 92.4,
  'Telangana': 95.7,
  'Tripura': 88.6,
  'Uttar Pradesh': 87.9,
  'Uttarakhand': 88.4,
  'West Bengal': 92.0,
}

// Main cities, where pump prices are quoted.
const CITIES = { 'Mumbai': 92.15, 'Kolkata': 91.76, 'Chennai': 92.39, 'Bengaluru': 88.99, 'Hyderabad': 95.7, 'Ahmedabad': 90.2, 'Pune': 92.5, 'Jaipur': 90.6 }

// Macro regions: the average of their states, so a contract can use one region for a whole part of the country.
const REGIONS = {
  'North India': ['Delhi', 'Haryana', 'Punjab', 'Himachal Pradesh', 'Uttarakhand', 'Uttar Pradesh', 'Rajasthan', 'Chandigarh', 'Jammu and Kashmir', 'Ladakh'],
  'South India': ['Andhra Pradesh', 'Telangana', 'Karnataka', 'Kerala', 'Tamil Nadu', 'Puducherry', 'Lakshadweep'],
  'East India': ['West Bengal', 'Odisha', 'Bihar', 'Jharkhand', 'Andaman and Nicobar Islands'],
  'West India': ['Maharashtra', 'Gujarat', 'Goa', 'Dadra and Nagar Haveli and Daman and Diu'],
  'Central India': ['Madhya Pradesh', 'Chhattisgarh'],
  'North-East India': ['Assam', 'Arunachal Pradesh', 'Manipur', 'Meghalaya', 'Mizoram', 'Nagaland', 'Sikkim', 'Tripura'],
}

const avg = (names) => Math.round((names.reduce((s, n) => s + STATES[n], 0) / names.length) * 100) / 100
const rows = [
  ...Object.entries(STATES),
  ...Object.entries(CITIES),
  ...Object.entries(REGIONS).map(([name, members]) => [name, avg(members)]),
  ['National', avg(Object.keys(STATES))],
]

let token = ''
async function call(method, url, body) {
  const headers = { 'x-tms-client': 'seed', 'content-type': 'application/json', ...(token && { authorization: 'Bearer ' + token }) }
  const response = await fetch(API + url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await response.text()
  if (!response.ok) { const e = new Error(`${method} ${url} -> ${response.status} ${text.slice(0, 300)}`); e.status = response.status; throw e }
  return text ? JSON.parse(text) : null
}

token = (await call('POST', '/auth/login', {
  tenantCode: process.env.TMS_TENANT ?? 'DEMO',
  email: process.env.TMS_EMAIL ?? 'admin@demo.tms',
  password: process.env.TMS_PASSWORD ?? 'Admin@12345678',
})).accessToken

let added = 0, skipped = 0
for (const [region, price] of rows) {
  try {
    await call('POST', '/diesel-prices', { region, effectiveFrom: DATE, pricePerLitre: price, source: SOURCE })
    added++
  } catch (e) {
    if (e.status === 409) skipped++
    else throw e
  }
}
console.log(`• ${added} prices recorded from ${DATE}, ${skipped} already present (${rows.length} regions: ${Object.keys(STATES).length} states/UTs, ${Object.keys(CITIES).length} cities, ${Object.keys(REGIONS).length} macro regions, National)`)
