import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shipmentsApi } from '@/lib/api/endpoints'
import type { ShipmentDto, ShipmentQuotesDto, ShipmentStatus, UserProfile } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { ShipmentDetailPage } from './ShipmentDetailPage'

const auth = vi.hoisted(() => ({ user: null as unknown, permissions: new Set<string>() }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: auth.user, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
  Can: ({ permission, children }: { permission: string; children: React.ReactNode }) => (auth.permissions.has(permission) ? children : null),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  shipmentsApi: { get: vi.fn(), quotes: vi.fn(), tenders: vi.fn(), startTender: vi.fn(), tender: vi.fn(), fleetOptions: vi.fn(), accept: vi.fn(), reject: vi.fn() },
}))

const party = { name: 'Acme Stores', line1: 'Plot 1', city: 'Surat', state: 'Gujarat', pincode: '395003', contactName: null, contactPhone: null }

function shipment(status: ShipmentStatus, overrides: Partial<ShipmentDto> = {}): ShipmentDto {
  const tendered = status !== 'Draft'
  return {
    summary: {
      id: 's1', number: 'SH-00012', status, mode: 'Ftl', lane: 'Pune, Maharashtra → Surat, Gujarat', plannedPickupDate: '2026-10-06', orderCount: 1,
      totalWeightKg: 9000, transporterId: tendered ? 't1' : null, transporterName: tendered ? 'Shree Roadlines' : null, vehicleRegistration: null, utilization: null,
      freightEstimate: tendered ? 41500 : null,
    },
    vehicleTypeId: null, vehicleTypeName: '32 ft multi-axle', distanceKm: null, totalVolumeCbm: null, contractId: tendered ? 'c1' : null, contractReference: tendered ? 'CN-00001' : null,
    estimateLines: tendered ? [{ code: 'FREIGHT', description: 'Flat rate for the trip', amount: 41500 }] : null,
    overrideReason: null, tenderedAt: tendered ? '2026-10-04T09:00:00Z' : null, rejectionCount: 0, lastRejectionReason: null, vehicleId: null, driverId: null, driverName: null,
    driverPhone: null, acceptedAt: null, dispatchedAt: null, deliveredAt: null, cancelReason: null,
    orders: [{ orderId: 'o1', orderNumber: 'ORD-00001', dropSequence: 1, lrNumber: null, pickup: party, drop: party, weightKg: 9000, volumeCbm: null, description: 'Auto parts' }],
    version: 1,
    ...overrides,
  }
}

const quotes: ShipmentQuotesDto = {
  message: null,
  quotes: [
    { contractId: 'c1', contractReference: 'CN-00001', transporterId: 't1', transporterName: 'Shree Roadlines', mode: 'Ftl', lane: 'x', total: 41500, isCheapest: true, lines: [], notes: [] },
    { contractId: 'c2', contractReference: 'CN-00002', transporterId: 't2', transporterName: 'Blue Dart Freight', mode: 'Ftl', lane: 'x', total: 44000, isCheapest: false, lines: [], notes: [] },
  ],
}

const profile = (transporterId: string | null): UserProfile => ({ id: 'u1', email: 'a@b.c', fullName: 'User', type: transporterId ? 'Transporter' : 'Internal', transporterId, tenantCode: 'DEMO', tenantName: 'Demo', roles: [], permissions: [], mustChangePassword: false })

function renderPage(s: ShipmentDto) {
  vi.mocked(shipmentsApi.get).mockResolvedValue(s)
  vi.mocked(shipmentsApi.quotes).mockResolvedValue(quotes)
  vi.mocked(shipmentsApi.tenders).mockResolvedValue([])
  return renderWithProviders(<Routes><Route path="/shipments/:id" element={<ShipmentDetailPage />} /></Routes>, { route: '/shipments/s1' })
}

describe('ShipmentDetailPage', () => {
  beforeEach(() => vi.clearAllMocks())

  describe('as a planner', () => {
    beforeEach(() => {
      auth.user = profile(null)
      auth.permissions = new Set(['shipments.plan', 'shipments.read'])
    })

    it('offers the load to the lowest quote without asking for a reason', async () => {
      vi.mocked(shipmentsApi.tender).mockResolvedValue(shipment('Tendered'))
      const user = userEvent.setup()
      renderPage(shipment('Draft'))

      expect(await screen.findByText('Lowest', undefined, { timeout: 5000 })).toBeInTheDocument()
      await user.click((await screen.findAllByRole('button', { name: 'Offer this load' }))[0]!)
      await user.click(await screen.findByRole('button', { name: 'Offer load' }))

      expect(shipmentsApi.tender).toHaveBeenCalledWith('s1', 'c1', null)
    })

    it('demands a reason before offering the load to a dearer transporter', async () => {
      vi.mocked(shipmentsApi.tender).mockResolvedValue(shipment('Tendered'))
      const user = userEvent.setup()
      renderPage(shipment('Draft'))

      await user.click((await screen.findAllByRole('button', { name: 'Offer this load' }))[1]!)
      const dialog = await screen.findByRole('dialog')
      expect(within(dialog).getByRole('button', { name: 'Offer load' })).toBeDisabled()

      await user.type(within(dialog).getByLabelText('Reason for not choosing the lowest quote'), 'Cheapest has no trucks')
      await user.click(within(dialog).getByRole('button', { name: 'Offer load' }))

      expect(shipmentsApi.tender).toHaveBeenCalledWith('s1', 'c2', 'Cheapest has no trucks')
    })

    it('shows the freight estimate once the load has been offered', async () => {
      renderPage(shipment('Tendered'))

      expect(await screen.findByText('Freight estimate')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Withdraw offer' })).toBeInTheDocument()
    })
  })

  describe('as a vendor', () => {
    beforeEach(() => {
      auth.user = profile('t1')
      auth.permissions = new Set(['shipments.respond'])
    })

    it('can accept or decline a tendered load but never sees what it is estimated to cost', async () => {
      renderPage(shipment('Tendered', { estimateLines: null, contractReference: null, summary: { ...shipment('Tendered').summary, freightEstimate: null } }))

      expect(await screen.findByRole('button', { name: /Accept/ })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Decline' })).toBeInTheDocument()
      expect(screen.queryByText('Freight estimate')).not.toBeInTheDocument()
      expect(screen.queryByText('Contract')).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Dispatch' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Cancel shipment' })).not.toBeInTheDocument()
    })

    it('cannot accept until a vehicle and a driver are chosen', async () => {
      vi.mocked(shipmentsApi.fleetOptions).mockResolvedValue({
        vehicles: [{ id: 'v1', label: 'MH12AB1234 · 32 ft', isOk: true, warn: false, issues: [] }],
        drivers: [{ id: 'd1', label: 'Ramesh Yadav', isOk: true, warn: false, issues: [] }],
      })
      const user = userEvent.setup()
      renderPage(shipment('Tendered'))

      await user.click(await screen.findByRole('button', { name: /Accept/ }))
      const dialog = await screen.findByRole('dialog')
      expect(within(dialog).getByRole('button', { name: 'Accept load' })).toBeDisabled()
    })

    it('must give a reason to decline', async () => {
      vi.mocked(shipmentsApi.reject).mockResolvedValue(shipment('Draft'))
      const user = userEvent.setup()
      renderPage(shipment('Tendered'))

      await user.click(await screen.findByRole('button', { name: 'Decline' }))
      const dialog = await screen.findByRole('dialog')
      expect(within(dialog).getByRole('button', { name: 'Decline' })).toBeDisabled()
      await user.type(within(dialog).getByLabelText('Why are you declining?'), 'No truck free')
      await user.click(within(dialog).getByRole('button', { name: 'Decline' }))

      expect(shipmentsApi.reject).toHaveBeenCalledWith('s1', 'No truck free')
    })
  })
})
