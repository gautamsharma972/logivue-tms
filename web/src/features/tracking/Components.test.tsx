import { screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { trackingApi } from '@/lib/api/endpoints'
import type { ControlTowerSummaryDto, TrackingComplianceDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { DwellIndicator, EtaCard, RouteDeviationBanner, TrackingGapIndicator } from './components'
import { TrackingDashboardPage } from './DashboardPage'
import { networkLabel } from './driverTracking'

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  trackingApi: { summary: vi.fn(), compliance: vi.fn() },
}))
vi.mock('recharts', async () => {
  const actual = await vi.importActual<typeof import('recharts')>('recharts')
  return { ...actual, ResponsiveContainer: ({ children }: { children: React.ReactNode }) => <div>{children}</div> }
})

const deviation = (status: 'Open' | 'Resolved') => ({ id: 'd', detectedAt: '', latitude: 0, longitude: 0, distanceFromRouteKm: 3.84, durationMinutes: 17, severity: 'High' as const, status, reason: null, reasonNote: null, resolvedAt: null })

describe('operational indicators', () => {
  it('the deviation banner shows only while the vehicle is off route, with how far and how long', () => {
    const { unmount } = renderWithProviders(<RouteDeviationBanner route={{ deviations: [deviation('Open')] }} />)
    expect(screen.getByText(/Off route: 3.8 km from the planned road for 17 min/)).toBeInTheDocument()
    expect(screen.getByText('No reason recorded yet.')).toBeInTheDocument()
    unmount()
    const again = renderWithProviders(<RouteDeviationBanner route={{ deviations: [deviation('Resolved')] }} />)
    expect(again.container.textContent).toBe('')
  })

  it('a lost-tracking message says tracking stopped reporting, not that the truck stopped', () => {
    renderWithProviders(<TrackingGapIndicator health={{ health: 'Lost', ageMinutes: 36, gaps: [{ id: 'g', gapStart: '', gapEnd: null, durationMinutes: 36, lastKnownLatitude: 0, lastKnownLongitude: 0, severity: 'High' }] }} />)
    expect(screen.getByText('No location for 36 min')).toBeInTheDocument()
    expect(screen.getByText(/tracking, not the truck, has stopped reporting/)).toBeInTheDocument()
  })

  it('the dwell indicator names the excess over what was expected', () => {
    renderWithProviders(<DwellIndicator analytics={{ plannedKm: null, actualKm: 0, kmVariance: null, plannedMinutes: null, actualMinutes: null, minutesVariance: null, plannedStops: 0, stopsReached: 0, unplannedStops: 0, totalDwellMinutes: 0, deviationMinutes: 0, deviationCount: 0,
      dwells: [{ id: 'w', place: 'Pune Hub', kind: 'PlannedStop', startAt: '', endAt: null, durationMinutes: 80, expectedDurationMinutes: 30, excessDurationMinutes: 50, status: 'Ongoing' }] }} />)
    expect(screen.getByText('At Pune Hub: 80 min')).toBeInTheDocument()
    expect(screen.getByText('50 min longer than the 30 min expected.')).toBeInTheDocument()
  })

  it('the ETA card keeps the calculated time visible beside an operator correction', () => {
    renderWithProviders(<EtaCard eta={{ plannedAt: '2026-10-06T08:00:00Z', systemEtaAt: '2026-10-06T09:00:00Z', etaAt: '2026-10-06T09:20:00Z', overridden: true, overrideReason: 'Road closure reported by driver', risk: 'Delayed', delayMinutes: 80, confidence: 0.7, calculationVersion: 'rules-1' }} />)
    expect(screen.getByText('Calculated')).toBeInTheDocument()
    expect(screen.getByText(/Road closure reported by driver/)).toBeInTheDocument()
    expect(screen.getByText('Set by an operator')).toBeInTheDocument()
  })
})

describe('networkLabel', () => {
  it('names what the phone can tell', () => {
    expect(networkLabel(false, undefined)).toBe('Offline')
    expect(networkLabel(true, { effectiveType: '2g' })).toBe('Poor connection')
    expect(networkLabel(true, { type: 'wifi' })).toBe('Wi-Fi')
    expect(networkLabel(true, { type: 'cellular' })).toBe('Mobile data')
    expect(networkLabel(true, undefined)).toBe('Connected')
  })
})

describe('Tracking overview', () => {
  const summary: ControlTowerSummaryDto = { active: 5, onTime: 3, atRisk: 1, delayed: 1, trackingStale: 0, trackingLost: 1, routeDeviations: 0, excessDwell: 0, openExceptions: 1, completedToday: 0, notStarted: 2, openAlerts: 2, asOf: '2026-10-06T10:00:00Z' }
  const row = (over = {}) => ({ key: 'a', name: 'Shree Roadlines', trips: 4, expectedMinutes: 400, actualMinutes: 360, coveragePct: 90, gaps: 2, staleTrips: 1, lostTrips: 0, startedOnTime: 75, keptActive: 100, stoppedProperly: null, tripsWithRepeatedGaps: 0, ...over })
  const compliance = (): TrackingComplianceDto => ({ groupBy: 'transporter', from: '2026-09-06', to: '2026-10-06', overall: row({ name: 'All trips' }), rows: [row()], rules: { startToleranceMinutes: 30, minCoveragePct: 90, repeatedGapCount: 3 }, note: 'Operational figures only.' })

  beforeEach(() => {
    vi.mocked(trackingApi.summary).mockResolvedValue(summary)
    vi.mocked(trackingApi.compliance).mockResolvedValue(compliance())
  })

  it('shows a rate that cannot be measured as such, never as zero', async () => {
    renderWithProviders(<TrackingDashboardPage />)
    expect(await screen.findByText('Shree Roadlines')).toBeInTheDocument()
    expect(screen.getAllByText('Not measurable').length).toBeGreaterThan(0)
    expect(screen.getByText('Operational figures only.')).toBeInTheDocument()
  })
})
