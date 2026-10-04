import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { planningApi } from '@/lib/api/endpoints'
import type { DashboardDto, PlanningKpis } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { KpiTab } from './KpiTab'

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  planningApi: { dashboard: vi.fn(), exportDashboard: vi.fn().mockResolvedValue(undefined) },
}))

// The charts need real layout; they are exercised in the browser run, not in jsdom.
vi.mock('./KpiCharts', () => ({ DailyChart: () => <div>daily chart</div>, ReasonsChart: () => <div>reasons chart</div> }))

const kpis = (overrides: Partial<PlanningKpis> = {}): PlanningKpis => ({
  plans: 3, ordersTotal: 42, ordersPlanned: 39, ordersUnplanned: 3, vehiclesUsed: 8, totalFreightCost: 284500, consolidationSaving: 32000, backhaulSaving: 8000, totalSavings: 40000,
  averageWeightUtilisation: 0.782, averageVolumeUtilisation: 0.735, totalDistanceKm: 1420, costPerTonneKm: 4.25, averageStopsPerVehicle: 2.5, ftlPercent: 75, ptlPercent: 25,
  consolidatedPercent: 37.5, returnPickupPercent: 5.1, daily: [], unplannedReasons: [{ code: 'NO_VALID_RATE', orders: 3 }],
  transporters: [{ name: 'Shree Roadlines', vehicles: 5, cost: 190000 }], vehicleTypes: [{ name: '32 ft', vehicles: 6 }], ...overrides,
})

const dashboard = (k: PlanningKpis): DashboardDto => ({ from: '2026-09-04', to: '2026-10-04', kpis: k })

describe('KpiTab', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the planning KPIs with weight and volume fill kept separate', async () => {
    vi.mocked(planningApi.dashboard).mockResolvedValue(dashboard(kpis()))
    renderWithProviders(<KpiTab />)

    expect(await screen.findByText('Total orders')).toBeInTheDocument()
    expect(screen.getByText('₹2,84,500.00')).toBeInTheDocument()
    expect(screen.getByText('78%')).toBeInTheDocument() // weight fill
    expect(screen.getByText('74%')).toBeInTheDocument() // volume fill, a separate figure
    expect(screen.getByText('₹4.25')).toBeInTheDocument()
    expect(screen.getByText('Shree Roadlines')).toBeInTheDocument()
    expect(await screen.findByText('daily chart')).toBeInTheDocument()
  })

  it('says so, instead of showing zeros, when there are no plans in the period', async () => {
    vi.mocked(planningApi.dashboard).mockResolvedValue(dashboard(kpis({ plans: 0, ordersTotal: 0, ordersPlanned: 0, ordersUnplanned: 0, vehiclesUsed: 0, unplannedReasons: [], transporters: [], vehicleTypes: [] })))
    renderWithProviders(<KpiTab />)

    expect(await screen.findByText(/No plans in this period/)).toBeInTheDocument()
    expect(screen.queryByText('Total orders')).not.toBeInTheDocument()
  })

  it('exports the chosen period as csv or excel', async () => {
    vi.mocked(planningApi.dashboard).mockResolvedValue(dashboard(kpis()))
    const user = userEvent.setup()
    renderWithProviders(<KpiTab />)

    await screen.findByText('Total orders')
    await user.click(screen.getByRole('button', { name: /Excel/ }))
    await user.click(screen.getByRole('button', { name: /CSV/ }))

    expect(planningApi.exportDashboard).toHaveBeenCalledWith('xlsx', expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/), expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/))
    expect(planningApi.exportDashboard).toHaveBeenCalledWith('csv', expect.any(String), expect.any(String))
  })
})
