import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { performanceApi } from '@/lib/api/endpoints'
import type { AlertDto, PlacementDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { AlertsPage } from './AlertsPage'
import { RecordsPanel } from './performance/RecordsPanel'
import { PlacementsPage } from './PlacementsPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(), transporterId: null as string | null }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  Can: ({ permission, children }: { permission: string; children: React.ReactNode }) => (auth.permissions.has(permission) ? children : null),
  useAuth: () => ({ user: { id: 'u1', transporterId: auth.transporterId }, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  performanceApi: {
    placements: vi.fn(), reportPlacement: vi.fn(), placePlacement: vi.fn(), noShow: vi.fn(), cancelPlacement: vi.fn(), alerts: vi.fn(), acknowledgeAlert: vi.fn(), resolveAlert: vi.fn(),
    claims: vi.fn(), recordClaim: vi.fn(), setClaimValue: vi.fn(), resolveClaim: vi.fn(), costs: vi.fn(), recordCost: vi.fn(), capacity: vi.fn(), saveCapacity: vi.fn(), executions: vi.fn(),
  },
}))

const placement = (over: Partial<PlacementDto> = {}): PlacementDto => ({
  id: 'p1', shipmentId: 's1', shipmentNumber: 'SH-00001', transporterId: 't1', vehicleId: 'v1', vehicleRegistration: 'MH12AB1234', requiredAt: '2026-10-06T04:30:00Z', reportedAt: null,
  placedAt: null, loadingStartedAt: null, status: 'VehicleAssigned', slaStatus: 'Pending', delayMinutes: null, replacementCount: 0, reason: null, events: [], ...over,
})

const alert = (over: Partial<AlertDto> = {}): AlertDto => ({
  id: 'a1', alertType: 'PICKUP_OVERDUE', severity: 'Medium', transporterId: 't1', transporterName: 'Shree Roadlines', shipmentId: 's1', shipmentNumber: 'SH-00001',
  message: 'Pickup for SH-00001 is overdue.', status: 'Open', createdAt: '2026-10-06T05:00:00Z', acknowledgedAt: null, resolvedAt: null, resolution: null, ...over,
})

const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 })

beforeEach(() => {
  vi.clearAllMocks()
  auth.permissions = new Set(['transporters.performance.read', 'transporters.performance.manage'])
  auth.transporterId = null
})

describe('PlacementsPage', () => {
  it('lets staff place a vehicle and record a no-show with a reason', async () => {
    vi.mocked(performanceApi.placements).mockResolvedValue(page([placement({ slaStatus: 'Overdue' })]))
    vi.mocked(performanceApi.noShow).mockResolvedValue(placement({ status: 'NoShow' }))
    const user = userEvent.setup()
    renderWithProviders(<PlacementsPage />)

    expect(await screen.findByText('SH-00001')).toBeInTheDocument()
    expect(screen.getByText('Overdue')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'No-show' }))
    const record = await screen.findByRole('button', { name: 'Record no-show' })
    expect(record).toBeDisabled()
    await user.type(screen.getByLabelText('Reason'), 'Never came')
    await user.click(record)

    await waitFor(() => expect(performanceApi.noShow).toHaveBeenCalledWith('p1', 'Never came'))
  })

  it('shows a vendor only the report action, not the staff ones', async () => {
    auth.permissions = new Set(['transporters.performance.self'])
    auth.transporterId = 't1'
    vi.mocked(performanceApi.placements).mockResolvedValue(page([placement()]))
    vi.mocked(performanceApi.reportPlacement).mockResolvedValue(placement({ status: 'Reported' }))
    const user = userEvent.setup()
    renderWithProviders(<PlacementsPage />)

    await user.click(await screen.findByRole('button', { name: 'Vehicle reported' }))
    expect(performanceApi.reportPlacement).toHaveBeenCalledWith('p1')
    expect(screen.queryByRole('button', { name: 'Mark placed' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'No-show' })).not.toBeInTheDocument()
  })
})

describe('AlertsPage', () => {
  it('lists what needs attention and lets staff acknowledge and resolve it', async () => {
    vi.mocked(performanceApi.alerts).mockResolvedValue(page([alert()]))
    vi.mocked(performanceApi.acknowledgeAlert).mockResolvedValue(alert({ status: 'Acknowledged' }))
    vi.mocked(performanceApi.resolveAlert).mockResolvedValue(alert({ status: 'Resolved' }))
    const user = userEvent.setup()
    renderWithProviders(<AlertsPage />)

    expect(await screen.findByText('Pickup overdue')).toBeInTheDocument()
    expect(screen.getByText('Shree Roadlines')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Acknowledge' }))
    expect(performanceApi.acknowledgeAlert).toHaveBeenCalledWith('a1')

    await user.click(screen.getByRole('button', { name: 'Resolve' }))
    await user.type(await screen.findByLabelText('What was done'), 'Truck arrived')
    await user.click(screen.getAllByRole('button', { name: 'Resolve' }).at(-1)!)
    await waitFor(() => expect(performanceApi.resolveAlert).toHaveBeenCalledWith('a1', 'Truck arrived'))
  })

  it('says so when nothing needs attention', async () => {
    vi.mocked(performanceApi.alerts).mockResolvedValue(page([]))
    renderWithProviders(<AlertsPage />)

    expect(await screen.findByText('Nothing needs attention.')).toBeInTheDocument()
  })
})

describe('RecordsPanel', () => {
  beforeEach(() => {
    vi.mocked(performanceApi.claims).mockResolvedValue([])
    vi.mocked(performanceApi.costs).mockResolvedValue([])
    vi.mocked(performanceApi.capacity).mockResolvedValue([{ id: 'c1', transporterId: 't1', date: '2026-10-06', vehiclesCommitted: 10, vehiclesAvailable: 9 }])
    vi.mocked(performanceApi.executions).mockResolvedValue([])
  })

  it('shows availability per day and lets staff record a claim', async () => {
    vi.mocked(performanceApi.recordClaim).mockResolvedValue({} as never)
    const user = userEvent.setup()
    renderWithProviders(<RecordsPanel transporterId="t1" from="2026-07-01" to="2026-10-31" />)

    expect(await screen.findByText('90%')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Record a claim/ }))
    await user.type(await screen.findByRole('spinbutton', { name: /Value/ }), '2500')
    await user.click(screen.getByRole('button', { name: 'Record' }))
    await waitFor(() => expect(performanceApi.recordClaim).toHaveBeenCalledWith('t1', expect.objectContaining({ claimType: 'Damage', claimValue: 2500, shipmentId: null })))
  })

  it('shows a vendor capacity and claims but never costs', async () => {
    auth.permissions = new Set(['transporters.performance.self'])
    auth.transporterId = 't1'
    renderWithProviders(<RecordsPanel transporterId="t1" from="2026-07-01" to="2026-10-31" />)

    expect(await screen.findByText('Vehicle capacity')).toBeInTheDocument()
    expect(screen.getByText('Claims')).toBeInTheDocument()
    expect(screen.queryByText('Invoiced cost')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save day' })).toBeInTheDocument()
    expect(performanceApi.costs).not.toHaveBeenCalled()
  })
})
