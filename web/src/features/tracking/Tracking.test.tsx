import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { trackingApi } from '@/lib/api/endpoints'
import type { ControlTowerSummaryDto, CustomerTrackingDto, TrackedSummaryDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { ControlTowerPage } from './ControlTowerPage'
import { CustomerTrackingPage } from './CustomerTrackingPage'
import { clusterPins } from './TrackingMap'

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: null, can: () => true, status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
}))

vi.mock('./useTrackingLive', () => ({ useTrackingLive: () => ({ state: 'polling', pollMs: false }) }))
vi.mock('./TrackingMap', async (original) => ({ ...(await original<typeof import('./TrackingMap')>()), default: () => <div>map</div> }))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  trackingApi: { summary: vi.fn(), list: vi.fn(), customerView: vi.fn() },
}))

const summary: ControlTowerSummaryDto = {
  active: 12, onTime: 7, atRisk: 2, delayed: 1, trackingStale: 1, trackingLost: 1, routeDeviations: 1, excessDwell: 0, openExceptions: 3, completedToday: 2, notStarted: 4, openAlerts: 5, asOf: '2026-10-06T10:00:00Z',
}

const trip = (over: Partial<TrackedSummaryDto> = {}): TrackedSummaryDto => ({
  id: 't1', shipmentId: 's1', shipmentReference: 'SH-10025', tripReference: 'SH-10025', transporterId: null, transporterReference: 'Shree Roadlines', vehicleReference: 'MH12AB1234', driverName: 'Ramesh', driverPhone: null,
  customerName: null, origin: 'Mumbai', destination: 'Pune', execution: 'InTransit', tracking: 'Healthy', risk: 'Delayed', delivery: 'Pending', plannedArrivalAt: null, etaAt: null, systemEtaAt: null, etaOverridden: false,
  etaConfidence: 0.7, delayMinutes: 32, progressPct: 55, remainingKm: 60, latitude: 18.9, longitude: 73.2, lastCapturedAt: null, speedKph: 40, heading: 0, minutesSinceLastLocation: 2, openExceptions: 1, onRoute: false,
  moving: true, startedAt: null, completedAt: null, ...over,
})

const paged = <T,>(items: T[]) => ({ items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 })

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(trackingApi.summary).mockResolvedValue(summary)
  vi.mocked(trackingApi.list).mockResolvedValue(paged([trip(), trip({ id: 't2', tripReference: 'SH-10031', tracking: 'Lost', risk: 'Unknown', vehicleReference: 'GJ05EF9087' })]))
})

describe('Control tower', () => {
  it('shows the tiles and the trips, with tracking health apart from delivery risk', async () => {
    renderWithProviders(<ControlTowerPage />)
    expect(await screen.findByText('SH-10025')).toBeInTheDocument()
    expect(screen.getByText('SH-10031')).toBeInTheDocument()
    expect(screen.getByText('Tracking lost', { selector: '.ant-tag' })).toBeInTheDocument()
    expect(screen.getByText('Delayed', { selector: '.ant-tag' })).toBeInTheDocument()
    expect(await screen.findByLabelText('Tracking lost')).toBeInTheDocument()
  })

  it('narrows the list when a tile is clicked', async () => {
    const user = userEvent.setup()
    renderWithProviders(<ControlTowerPage />)
    await user.click(await screen.findByLabelText('Tracking lost'))
    await waitFor(() => expect(trackingApi.list).toHaveBeenLastCalledWith(expect.objectContaining({ tracking: 'Lost' })))
  })
})

describe('Customer link page', () => {
  const view: CustomerTrackingDto = {
    shipmentReference: 'SH-10025', origin: 'Mumbai', destination: 'Pune', statusLabel: 'On the way to Pune', steps: [{ label: 'Picked up', state: 'done', at: null }, { label: 'On the way', state: 'current', at: null }, { label: 'Delivered', state: 'pending', at: null }],
    latitude: 18.9, longitude: 73.2, locationAsOf: '2026-10-06T10:00:00Z', locationLabel: 'Near Khopoli', etaAt: '2026-10-06T14:00:00Z', deliveryWindowStart: null, deliveryWindowEnd: null, riskLabel: 'On time', delivered: false,
    deliveredAt: null, route: [], asOf: '2026-10-06T10:00:00Z',
  }

  it('shows the status and the steps, and nothing internal', async () => {
    vi.mocked(trackingApi.customerView).mockResolvedValue(view)
    renderWithProviders(<Routes><Route path="/track/:token" element={<CustomerTrackingPage />} /></Routes>, { route: '/track/abc' })
    expect(await screen.findByText('On the way to Pune')).toBeInTheDocument()
    expect(screen.getByText('Picked up')).toBeInTheDocument()
    expect(screen.queryByText(/exception|price|rate/i)).toBeNull()
  })

  it('says only that the link is not available when it fails, whatever the reason', async () => {
    vi.mocked(trackingApi.customerView).mockRejectedValue(new Error('not-found'))
    renderWithProviders(<Routes><Route path="/track/:token" element={<CustomerTrackingPage />} /></Routes>, { route: '/track/bad' })
    expect(await screen.findByText('This link is not available')).toBeInTheDocument()
  })
})

describe('clusterPins', () => {
  const pins = [{ latitude: 19.0, longitude: 73.0 }, { latitude: 19.01, longitude: 73.01 }, { latitude: 28.6, longitude: 77.2 }]
  it('merges nearby pins when zoomed out and keeps distant ones apart', () => {
    const out = clusterPins(pins, 5, 7)
    expect(out.map((c) => c.items.length).sort()).toEqual([1, 2])
  })
  it('shows every pin when zoomed in', () => {
    expect(clusterPins(pins, 9, 7)).toHaveLength(3)
  })
})
