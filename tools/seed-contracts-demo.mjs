#!/usr/bin/env node
// Seeds the freight contract demo book into a DEMO tenant through the dev API: contracts per carrier with rate versions, DPH rules and diesel history,
// accessorials, capacity and SLA commitments, and kept ratings. Safe to re-run.
//   node tools/seed-planning-demo.mjs   (first: it creates the carriers this uses)
//   node tools/seed-contracts-demo.mjs
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
const result = await call('POST', '/dev/contracts/seed-demo', { carriers }, token)
console.log('•', result.message)
console.log('  Open /contracts/dashboard, then try /contracts/simulator and /contracts/ratings.')
