#!/usr/bin/env node
// Seeds demo roles and users into a DEMO tenant through the public API (no database access).
//   node tools/seed-planning-demo.mjs   (first: it creates the carriers the vendor users belong to)
//   node tools/seed-users-demo.mjs
// Environment: TMS_API (default http://localhost:5080), TMS_TENANT (DEMO), TMS_EMAIL (admin@demo.tms), TMS_PASSWORD (the dev admin password).
// Safe to re-run: roles and users that exist are skipped. Every seeded user has the password in USER_PASSWORD. Development tenants only.

const API = (process.env.TMS_API ?? 'http://localhost:5080') + '/api/v1'
const USER_PASSWORD = 'Demo@12345678'

let token = ''
async function call(method, url, body) {
  const headers = { 'x-tms-client': 'seed', 'content-type': 'application/json', ...(token && { authorization: 'Bearer ' + token }) }
  const response = await fetch(API + url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await response.text()
  if (!response.ok) throw new Error(`${method} ${url} -> ${response.status} ${text.slice(0, 400)}`)
  return text ? JSON.parse(text) : null
}
const log = (...parts) => console.log('•', ...parts)

token = (await call('POST', '/auth/login', {
  tenantCode: process.env.TMS_TENANT ?? 'DEMO',
  email: process.env.TMS_EMAIL ?? 'admin@demo.tms',
  password: process.env.TMS_PASSWORD ?? 'Admin@12345678',
})).accessToken

const ROLES = [
  { name: 'Planner', audience: 'Internal', description: 'Plans loads, runs the optimizer and creates shipments.',
    permissions: ['shipments.read', 'shipments.plan', 'contracts.read', 'transporters.read', 'transporters.select', 'transporters.performance.read', 'tracking.read'] },
  { name: 'Transport Manager', audience: 'Internal', description: 'Manages transporters, contracts and performance; approves shipments.',
    permissions: ['transporters.read', 'transporters.manage', 'transporters.approve', 'transporters.select', 'transporters.performance.read', 'transporters.performance.manage',
      'contracts.read', 'contracts.manage', 'contracts.approve', 'shipments.read', 'shipments.plan', 'shipments.approve', 'approvals.read.all'] },
  { name: 'POD Reviewer', audience: 'Internal', description: 'Reviews proofs of delivery and handles delivery exceptions.',
    permissions: ['deliveries.read', 'deliveries.manage', 'deliveries.pod.review', 'deliveries.exceptions.manage', 'shipments.read', 'shipments.pod.verify'] },
  { name: 'Control Tower', audience: 'Internal', description: 'Watches live tracking, geofences and customer links.',
    permissions: ['tracking.read', 'tracking.manage', 'tracking.geofences.manage', 'tracking.links.manage', 'shipments.read'] },
  { name: 'Viewer', audience: 'Internal', description: 'Read-only access to operational data.',
    permissions: ['shipments.read', 'contracts.read', 'transporters.read', 'deliveries.read', 'tracking.read', 'transporters.performance.read'] },
  { name: 'Auditor', audience: 'Internal', description: 'Reads the audit trail, users and roles.',
    permissions: ['audit.read', 'users.read', 'roles.read', 'approvals.read.all'] },
  { name: 'Vendor Portal', audience: 'External', description: 'Transporter staff: respond to tenders, maintain own fleet and papers.',
    permissions: ['shipments.respond', 'transporters.self.manage', 'transporters.performance.self', 'deliveries.execute', 'tracking.execute'] },
  { name: 'Driver', audience: 'External', description: 'Driver mobile app: deliveries and GPS tracking.',
    permissions: ['deliveries.execute', 'tracking.execute'] },
]

const existingRoles = await call('GET', '/roles')
const roleId = {}
for (const r of existingRoles) roleId[r.name] = r.id
for (const r of ROLES) {
  if (roleId[r.name]) { log('role exists:', r.name); continue }
  const created = await call('POST', '/roles', { name: r.name, description: r.description, permissions: r.permissions, audience: r.audience })
  roleId[r.name] = created.id
  log('role created:', r.name, `(${r.permissions.length} permissions)`)
}

const transporters = (await call('GET', '/transporters?pageSize=50')).items
const shree = transporters.find((t) => (t.tradeName ?? t.legalName).startsWith('Shree'))
const bharat = transporters.find((t) => (t.tradeName ?? t.legalName).startsWith('Bharat'))

const USERS = [
  ['planner@demo.tms', 'Priya Planner', 'Internal', ['Planner']],
  ['manager@demo.tms', 'Manoj Manager', 'Internal', ['Transport Manager']],
  ['pod@demo.tms', 'Pooja POD Reviewer', 'Internal', ['POD Reviewer']],
  ['tower@demo.tms', 'Tarun Tower', 'Internal', ['Control Tower']],
  ['viewer@demo.tms', 'Vikram Viewer', 'Internal', ['Viewer']],
  ['auditor@demo.tms', 'Anita Auditor', 'Internal', ['Auditor']],
  ...(shree ? [['vendor@shree.demo', 'Shree Vendor User', 'Transporter', ['Vendor Portal'], shree.id], ['driver@shree.demo', 'Shree Driver', 'Driver', ['Driver'], shree.id]] : []),
  ...(bharat ? [['vendor@bharat.demo', 'Bharat Vendor User', 'Transporter', ['Vendor Portal'], bharat.id]] : []),
]

const existingEmails = new Set((await call('GET', '/users?pageSize=100')).items.map((u) => u.email.toLowerCase()))
for (const [email, fullName, type, roles, transporterId] of USERS) {
  if (existingEmails.has(email)) { log('user exists:', email); continue }
  await call('POST', '/users', {
    email, fullName, password: USER_PASSWORD, type, roleIds: roles.map((n) => roleId[n]),
    transporterId: type === 'Internal' ? null : transporterId, requirePasswordChange: false,
  })
  log('user created:', email, `(${type}: ${roles.join(', ')})`)
}
console.log(`\nAll seeded users sign in with organisation DEMO and password ${USER_PASSWORD}.`)
