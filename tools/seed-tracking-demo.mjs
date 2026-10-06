#!/usr/bin/env node
// Seeds the tracking demo day into a DEMO tenant through the dev API: 21 trips on five lanes, ten vehicles and drivers, seven geofences, GPS trails and trips in every
// condition (on time, at risk, severely delayed, off route, a deviation that cleared, tracking stale, tracking lost, excess standing time, an unplanned stop, completed, not started).
// The trips go through the real location pipeline, so what the control tower shows is what the engine concluded. Safe to re-run: trips that exist are skipped.
//   node tools/seed-planning-demo.mjs   (first: it creates the carriers this uses)
//   node tools/seed-tracking-demo.mjs
// Environment: TMS_API (default http://localhost:5080), TMS_TENANT (DEMO), TMS_EMAIL (admin@demo.tms), TMS_PASSWORD (the dev admin password). Development tenants only.
const API = (process.env.TMS_API ?? 'http://localhost:5080') + '/api/v1'
const headers = { 'x-tms-client': 'seed', 'content-type': 'application/json' }

async function call(method, url, body, token) {
  const response = await fetch(API + url, { method, headers: { ...headers, ...(token && { authorization: 'Bearer ' + token }) }, body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await response.text()
  if (!response.ok) throw new Error(`${method} ${url} -> ${response.status} ${text.slice(0, 300)}`)
  return text ? JSON.parse(text) : null
}

const login = await call('POST', '/auth/login', { tenantCode: process.env.TMS_TENANT ?? 'DEMO', email: process.env.TMS_EMAIL ?? 'admin@demo.tms', password: process.env.TMS_PASSWORD ?? 'Admin@12345678' })
const token = login.accessToken
const carriers = (await call('GET', '/transporters?status=Active&pageSize=20', undefined, token)).items.map((t) => ({ id: t.id, name: t.legalName }))
if (carriers.length === 0) throw new Error('No active transporters: run tools/seed-planning-demo.mjs first.')
const result = await call('POST', '/dev/tracking/seed-demo', { carriers }, token)
console.log('•', result.message, `(${result.alerts} alerts, ${result.exceptions} exceptions)`)
console.log('  Open the control tower at /tracking. Interesting trips: SH-10025 (off route, ~30 min late, High exception), SH-10031 (tracking lost, 36-minute gap),')
console.log('  SH-10040 (at risk), SH-10041 (severely delayed), SH-10042 (stale), SH-10043 (excess standing at the drop), SH-10044 (unplanned stop), SH-10054/55 (not started).')
