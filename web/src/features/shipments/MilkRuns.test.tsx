import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { milkRunsApi } from '@/lib/api/endpoints'
import type { MilkRunDto, MilkRunPlan, MilkRunTrip } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { MilkRunFormDrawer } from './MilkRunFormDrawer'
import { MilkRunPlanView } from './MilkRunPlanView'

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  milkRunsApi: { create: vi.fn(), update: vi.fn(), list: vi.fn(), preview: vi.fn() },
  planningApi: { vehicleTypes: vi.fn().mockResolvedValue([{ id: 'vt1', name: '14ft', code: 'T14', payloadKg: 4000, volumeCbm: 18 }]) },
  locationsApi: {
    list: vi.fn().mockResolvedValue({
      items: [
        { id: 'l-depot', code: 'DC', name: 'Pune DC', type: 'Depot', city: 'Pune', state: 'Maharashtra', isActive: true },
        { id: 'l-a', code: 'SA', name: 'Supplier A', type: 'Supplier', city: 'Chakan', state: 'Maharashtra', isActive: true },
      ],
      page: 1, pageSize: 200, totalCount: 2, totalPages: 1,
    }),
  },
}))

const milkRun: MilkRunDto = {
  id: 'm1', code: 'MR-1', name: 'Pune supplier run', depotLocationId: 'l-depot', depotName: 'Pune DC', vehicleTypeId: 'vt1', maxStops: 8, maxDurationMinutes: 600,
  departureTime: '07:00:00', days: ['Monday', 'Wednesday'], isActive: true, version: 3,
  stops: [{ sequence: 1, locationId: 'l-a', locationName: 'Supplier A', city: 'Chakan', state: 'Maharashtra', type: 'Pickup', serviceMinutes: 20, windowFrom: '09:00:00', windowTo: '12:00:00' }],
}

const trip = (overrides: Partial<MilkRunTrip> = {}): MilkRunTrip => ({
  number: 1, vehicleTypeId: 'vt1', vehicleTypeName: '14ft', payloadKg: 4000, volumeCapacityCbm: 18, contractReference: 'CN-1', transporterName: 'Shree', cost: 10000, distanceKm: 120.5,
  drivingMinutes: 160, totalMinutes: 230, departure: '2026-10-04T01:30:00Z', returnAt: '2026-10-04T05:20:00Z', peakWeightKg: 3000, peakVolumeCbm: 10, weightUtilisation: 0.75,
  volumeUtilisation: 0.55, costPerTonneKm: 27.6, sequenceMethod: 'Exact', templateOrderKm: 150, shortestOrderKm: 120.5, alternatives: [], warnings: [], reason: '14ft chosen for a peak load of 3,000 kg.', source: 'Estimate',
  transporter: { id: 'tr1', code: 'SHR', name: 'Shree Roadlines', contactPerson: 'Anil', phone: '9800000001', email: null, city: 'Pune' },
  vehicle: { id: 'v1', registration: 'MH12AB1234', typeName: '14ft', payloadKg: 4000, compliance: 'Compliant', issues: [] },
  driver: { id: 'd1', name: 'Ramesh', phone: '9800000002', licenseNumber: 'MH1220200001', compliance: 'Compliant', issues: [] },
  stops: [
    { sequence: 1, kind: 'Pickup', label: 'Supplier A', orders: [{ orderId: 'o1', number: 'ORD-00001', weightKg: 3000, volumeCbm: 10 }], weightKg: 3000, volumeCbm: 10, arrival: '2026-10-04T02:30:00Z', departure: '2026-10-04T02:50:00Z', waitMinutes: 35, onboardKg: 3000 },
    { sequence: 2, kind: 'Depot', label: 'Pune DC', orders: [], weightKg: 0, volumeCbm: null, arrival: '2026-10-04T05:20:00Z', departure: null, waitMinutes: null, onboardKg: 0 },
  ], ...overrides,
})

const plan = (overrides: Partial<MilkRunPlan> = {}): MilkRunPlan => ({
  code: 'MR-1', name: 'Pune supplier run', date: '2026-10-04', trips: [trip()], skipped: [{ templateSequence: 2, label: 'Supplier B', reason: 'Nothing to collect or deliver today.' }],
  unplanned: [], totals: { orders: 1, stopsServed: 1, stopsSkipped: 1, trips: 1, inboundKg: 3000, outboundKg: 0, distanceKm: 120.5, cost: 10000, costPerTonneKm: 27.6 }, source: 'Estimate',
  warnings: ['Sunday is not one of this run\'s scheduled days. Planned anyway.'], ...overrides,
})

describe('MilkRunPlanView', () => {
  it('shows the day\'s trip, the skipped stops, waits, and labels an estimate as an estimate', () => {
    renderWithProviders(<MilkRunPlanView plan={plan()} />)

    expect(screen.getByText(/Trip 1 · 14ft/)).toBeInTheDocument()
    expect(screen.getByText('Estimate')).toBeInTheDocument()
    expect(screen.getByText(/Supplier B/)).toBeInTheDocument()
    expect(screen.getByText(/not one of this run's scheduled days/)).toBeInTheDocument()
    expect(screen.getByText('ORD-00001')).toBeInTheDocument()
    expect(screen.getByText('₹27.6/tonne-km')).toBeInTheDocument()
  })

  it('shows who runs the trip: transporter, vehicle and driver', () => {
    renderWithProviders(<MilkRunPlanView plan={plan()} />)

    expect(screen.getByText('Shree Roadlines')).toBeInTheDocument()
    expect(screen.getByText('MH12AB1234')).toBeInTheDocument()
    expect(screen.getByText('Ramesh')).toBeInTheDocument()
    expect(screen.getByText('Licence MH1220200001')).toBeInTheDocument()
  })

  it('points out when the shortest order would be shorter than the planned one', () => {
    renderWithProviders(<MilkRunPlanView plan={plan()} />)

    expect(screen.getByText(/shortest order would be 120.5 km instead of 150 km, saving 29.5 km/)).toBeInTheDocument()
  })

  it('says nothing to run when no orders are linked, and shows an unknown cost honestly', () => {
    renderWithProviders(<MilkRunPlanView plan={plan({ trips: [], totals: { ...plan().totals, orders: 0, trips: 0, cost: null } })} />)
    expect(screen.getByText(/Nothing to run on this day/)).toBeInTheDocument()
  })

  it('shows a trip with no contract rate as cost unknown, with its warning', () => {
    renderWithProviders(<MilkRunPlanView plan={plan({ trips: [trip({ cost: null, warnings: ['No active contract has a rate for this run, so its cost is unknown.'] })] })} />)

    expect(screen.getByText('Cost unknown')).toBeInTheDocument()
    expect(screen.getByText(/No active contract has a rate/)).toBeInTheDocument()
  })
})

describe('MilkRunFormDrawer', () => {
  beforeEach(() => vi.clearAllMocks())

  it('saves an edit with hours converted to minutes, times as HH:mm:ss and the version it was loaded at', async () => {
    vi.mocked(milkRunsApi.update).mockResolvedValue(milkRun)
    const user = userEvent.setup()
    renderWithProviders(<MilkRunFormDrawer open milkRun={milkRun} onClose={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(milkRunsApi.update).toHaveBeenCalled())
    expect(milkRunsApi.update).toHaveBeenCalledWith('m1', expect.objectContaining({
      code: 'MR-1', depotLocationId: 'l-depot', vehicleTypeId: 'vt1', maxStops: 8, maxDurationMinutes: 600, departureTime: '07:00:00', days: ['Monday', 'Wednesday'], version: 3,
      stops: [{ locationId: 'l-a', type: 'Pickup', serviceMinutes: 20, windowFrom: '09:00:00', windowTo: '12:00:00' }],
    }))
  })

  it('does not let the last stop be removed', async () => {
    renderWithProviders(<MilkRunFormDrawer open milkRun={milkRun} onClose={vi.fn()} />)

    expect(await screen.findByRole('button', { name: 'Remove stop 1' })).toBeDisabled()
  })
})
