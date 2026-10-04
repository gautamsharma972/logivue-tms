import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { performanceApi, transportersApi } from '@/lib/api/endpoints'
import type { ExecutionDto, PerformanceDto, RankingResultDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { PerformanceTab } from './performance/PerformanceTab'
import { RankingsPage } from './RankingsPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['transporters.performance.read', 'transporters.performance.manage']) }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  Can: ({ permission, children }: { permission: string; children: React.ReactNode }) => (auth.permissions.has(permission) ? children : null),
  useAuth: () => ({ user: { id: 'u1', transporterId: null }, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  transportersApi: { vehicleTypes: vi.fn().mockResolvedValue([{ id: 'vt1', name: '14 ft' }]) },
  performanceApi: {
    get: vi.fn(), recalculate: vi.fn(), scorecards: vi.fn(), generateScorecard: vi.fn(), rankings: vi.fn(), benchmark: vi.fn(), executions: vi.fn(),
    recordEvent: vi.fn(), attributeDelay: vi.fn(), lanes: vi.fn(), createLane: vi.fn(), updateLane: vi.fn(), settings: vi.fn(),
  },
}))

const metrics = {
  duePlacements: 0, placedOnTime: 0, placed: 0, noShows: 0, replacements: 0, averagePlacementDelayMinutes: null, measuredPickups: 4, notMeasurablePickups: 1,
  latePickupsCarrier: 1, latePickupsNonCarrier: 1, latePickupsUnattributed: 2, averagePickupDelayMinutes: 42, measuredDeliveries: 4, notMeasurableDeliveries: 0,
  lateDeliveriesCarrier: 0, lateDeliveriesNonCarrier: 0, lateDeliveriesUnattributed: 0, averageDeliveryDelayMinutes: null, pendingPod: 0, averagePodSubmissionHours: null,
}

const performance: PerformanceDto = {
  transporterId: 't1', from: '2026-07-01', to: '2026-09-30', metrics,
  kpis: [
    { kpi: 'OnTimePickup', numerator: 2, denominator: 3, value: 66.67 },
    { kpi: 'OnTimeDelivery', numerator: 0, denominator: 0, value: null },
    { kpi: 'ClaimsRate', numerator: 1, denominator: 100, value: 1 },
  ],
  months: [{ month: '2026-09-01', kpis: [{ kpi: 'OnTimePickup', numerator: 2, denominator: 3, value: 66.67 }] }],
}

const late: ExecutionDto = {
  id: 'e1', shipmentId: 's1', shipmentNumber: 'SH-00001', transporterId: 't1', plannedPickupAt: '2026-09-10T14:30:00Z', actualPickupAt: '2026-09-10T16:30:00Z',
  pickupDelayMinutes: 120, pickupDelayReasonCode: null, pickupAttribution: 'Unattributed', plannedDeliveryAt: null, actualDeliveryAt: null, deliveryDelayMinutes: null,
  deliveryDelayReasonCode: null, deliveryAttribution: 'None', status: 'PickedUp', events: [{ eventType: 'VehicleDeparture', eventAt: '2026-09-10T16:30:00Z', delayReasonCode: null, remarks: null }],
}

beforeEach(() => {
  vi.clearAllMocks()
  auth.permissions = new Set(['transporters.performance.read', 'transporters.performance.manage'])
  vi.mocked(performanceApi.get).mockResolvedValue(performance)
  vi.mocked(performanceApi.scorecards).mockResolvedValue([])
  vi.mocked(performanceApi.benchmark).mockResolvedValue({ transporterId: 't1', scopeLabel: 'All lanes', from: '2026-07-01', to: '2026-09-30', rows: [] })
  vi.mocked(performanceApi.executions).mockResolvedValue([late])
  vi.mocked(performanceApi.lanes).mockResolvedValue([])
  vi.mocked(performanceApi.settings).mockResolvedValue([])
})

describe('PerformanceTab', () => {
  it('shows each KPI with the counts behind it, and "not measurable" instead of a zero', async () => {
    renderWithProviders(<PerformanceTab transporterId="t1" />)

    expect((await screen.findAllByText('On-time pickup')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('66.7%').length).toBeGreaterThan(0) // the card and the month table
    expect(screen.getByText('2 of 3')).toBeInTheDocument()
    expect(screen.getAllByText('Not measurable').length).toBeGreaterThan(0)
    expect(screen.getByText(/Claims rate \(lower is better\)/)).toBeInTheDocument()
  })

  it('points out late loads that still need a reason, and lets a planner give one', async () => {
    vi.mocked(performanceApi.attributeDelay).mockResolvedValue({ ...late, pickupAttribution: 'NonCarrier' })
    const user = userEvent.setup()
    renderWithProviders(<PerformanceTab transporterId="t1" />)

    expect(await screen.findByText(/Some late loads have no reason yet/)).toBeInTheDocument()
    await user.click(await screen.findByRole('button', { name: 'Say why' }))
    await user.click(await screen.findByLabelText('Reason'))
    await user.click(await screen.findByText(/Traffic — does not count against the carrier/))
    await user.click(screen.getByRole('button', { name: 'Save reason' }))

    expect(performanceApi.attributeDelay).toHaveBeenCalledWith('e1', false, 'TRAFFIC')
  })

  it('offers recalculation and scorecards only to people who may manage performance', async () => {
    auth.permissions = new Set(['transporters.performance.read'])
    renderWithProviders(<PerformanceTab transporterId="t1" />)

    await screen.findAllByText('On-time pickup')
    expect(screen.queryByRole('button', { name: /Recalculate/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Generate for this period/ })).not.toBeInTheDocument()
  })
})

describe('RankingsPage', () => {
  it('lists ranked transporters first and says why others are not ranked', async () => {
    const result: RankingResultDto = {
      metric: 'OverallScore', scopeLabel: 'All lanes', from: '2026-07-01', to: '2026-09-30',
      rows: [
        { rank: 1, transporterId: 'a', transporterCode: 'TR-1', transporterName: 'Alpha Roadlines', region: 'MAHARASHTRA', status: 'Active', metricValue: 91.2, overallScore: 91.2, ranked: true, note: null, kpis: [] },
        { rank: null, transporterId: 'b', transporterCode: 'TR-2', transporterName: 'Beta Carriers', region: null, status: 'Active', metricValue: null, overallScore: null, ranked: false, note: 'Sample below the minimum of 20 for this metric.', kpis: [] },
      ],
    }
    vi.mocked(performanceApi.rankings).mockResolvedValue(result)
    vi.mocked(transportersApi.vehicleTypes).mockResolvedValue([])
    renderWithProviders(<RankingsPage />)

    expect(await screen.findByText('Alpha Roadlines')).toBeInTheDocument()
    expect(screen.getAllByText('91.2').length).toBeGreaterThan(0)
    expect(screen.getByText('Beta Carriers')).toBeInTheDocument()
    expect(screen.getByText('Not ranked')).toBeInTheDocument()
  })
})
