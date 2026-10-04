import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { performanceApi, transportersApi } from '@/lib/api/endpoints'
import type { RecommendationResultDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { CoverageTab } from './performance/CoverageTab'
import { SelectionPage } from './SelectionPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['transporters.performance.read', 'transporters.performance.manage', 'transporters.select']) }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  Can: ({ permission, children }: { permission: string; children: React.ReactNode }) => (auth.permissions.has(permission) ? children : null),
  useAuth: () => ({ user: { id: 'u1', transporterId: null }, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  transportersApi: { vehicleTypes: vi.fn() },
  performanceApi: {
    recommend: vi.fn(), capabilityCatalog: vi.fn(), capabilities: vi.fn(), addCapability: vi.fn(), endCapability: vi.fn(), lanes: vi.fn(), createLane: vi.fn(), updateLane: vi.fn(),
    planningRules: vi.fn(), addPlanningRule: vi.fn(), endPlanningRule: vi.fn(),
  },
}))

const candidate = (over: object) => ({
  transporterId: 'a', code: 'TR-A', name: 'Alpha Roadlines', status: 'Active', eligible: true, reasons: [], warnings: [], laneId: null, preferred: false,
  restrictedForPlanning: false, rate: { contractId: 'c', contractReference: 'CN-1', total: 30000, note: null }, availableVehicles: 2, kpis: {}, ...over,
})

beforeEach(() => {
  vi.clearAllMocks()
  auth.permissions = new Set(['transporters.performance.read', 'transporters.performance.manage', 'transporters.select'])
  vi.mocked(transportersApi.vehicleTypes).mockResolvedValue([{ id: 'vt1', code: 'T14', name: '14 ft', payloadKg: 4000, volumeCbm: 18, isActive: true, version: 1 }])
  vi.mocked(performanceApi.capabilityCatalog).mockResolvedValue([{ code: 'HAZARDOUS', name: 'Hazardous goods' }])
  vi.mocked(performanceApi.capabilities).mockResolvedValue([])
  vi.mocked(performanceApi.lanes).mockResolvedValue([])
  vi.mocked(performanceApi.planningRules).mockResolvedValue([])
})

describe('SelectionPage', () => {
  it('shows the recommended carrier with its reasons and why the others cannot take the load', async () => {
    const result: RecommendationResultDto = {
      recommended: null,
      ranked: [{ rank: 1, candidate: candidate({}) as never, recommendationScore: 81.5, components: [], explanations: ['Rate: Estimated ₹30,000'], comparisons: [] }],
      candidates: [candidate({}) as never, candidate({ transporterId: 'b', code: 'TR-B', name: 'Beta Carriers', eligible: false, reasons: ['Transporter is suspended.'] }) as never],
    }
    result.recommended = result.ranked[0]!
    vi.mocked(performanceApi.recommend).mockResolvedValue(result)
    const user = userEvent.setup()
    renderWithProviders(<SelectionPage />)

    await user.click(await screen.findByLabelText('From state'))
    await user.click((await screen.findAllByText('Maharashtra', { selector: '.ant-select-item-option-content' })).at(-1)!)
    await user.click(screen.getByLabelText('To state'))
    await user.click((await screen.findAllByText('Gujarat', { selector: '.ant-select-item-option-content' })).at(-1)!)
    await user.click(screen.getByLabelText('Vehicle type'))
    await user.click(await screen.findByText('14 ft', { selector: '.ant-select-item-option-content' }))
    await user.type(screen.getByRole('spinbutton', { name: /Weight/ }), '5000')
    await user.click(screen.getByRole('button', { name: /Find transporters/ }))

    expect(await screen.findByText('Recommended: Alpha Roadlines')).toBeInTheDocument()
    expect(screen.getByText('Rate: Estimated ₹30,000')).toBeInTheDocument()
    expect(screen.getByText('Transporter is suspended.')).toBeInTheDocument()
    expect(performanceApi.recommend).toHaveBeenCalledWith(expect.objectContaining({ originState: 'Maharashtra', destinationState: 'Gujarat', mode: 'Ftl', vehicleTypeId: 'vt1', weightKg: 5000 }))
  })
})

describe('CoverageTab', () => {
  it('needs a reason to add a planning rule and shows ended rules with theirs', async () => {
    vi.mocked(performanceApi.planningRules).mockResolvedValue([
      { id: 'r1', transporterId: 't1', ruleType: 'Restricted', laneId: null, reason: 'No-shows', effectiveFrom: '2026-09-01', effectiveTo: '2026-09-20', isActive: false, endedBecause: 'Resolved' },
    ])
    vi.mocked(performanceApi.addPlanningRule).mockResolvedValue({ id: 'r2' } as never)
    const user = userEvent.setup()
    renderWithProviders(<CoverageTab transporterId="t1" />)

    expect(await screen.findByText(/Ended 2026-09-20: Resolved/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Add rule/ }))
    await user.click(await screen.findByLabelText('Rule'))
    await user.click(await screen.findByText('Do not allocate', { selector: '.ant-select-item-option-content' }))
    await user.click(screen.getByRole('button', { name: 'Add rule' }))
    expect(await screen.findByText('Say why')).toBeInTheDocument() // refused without a reason
    expect(performanceApi.addPlanningRule).not.toHaveBeenCalled()

    await user.type(screen.getByLabelText('Why'), 'Under investigation')
    await user.click(screen.getByRole('button', { name: 'Add rule' }))
    await waitFor(() => expect(performanceApi.addPlanningRule).toHaveBeenCalledWith('t1', expect.objectContaining({ ruleType: 'DoNotAllocate', reason: 'Under investigation', laneId: null })))
  })

  it('hides the internal planning rules from anyone who may not see performance', async () => {
    auth.permissions = new Set(['transporters.performance.self'])
    renderWithProviders(<CoverageTab transporterId="t1" />)

    expect(await screen.findByText('Capabilities')).toBeInTheDocument()
    expect(screen.queryByText('Planning rules')).not.toBeInTheDocument()
  })
})
