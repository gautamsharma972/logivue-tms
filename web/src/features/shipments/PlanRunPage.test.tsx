import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { planningApi } from '@/lib/api/endpoints'
import type { PlanStatus, PlannedVehicle, RunDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { PlanRunPage } from './PlanRunPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: { id: 'u1', transporterId: null }, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  planningApi: { run: vi.fn(), versions: vi.fn(), lockVehicle: vi.fn(), approve: vi.fn(), commit: vi.fn(), reoptimize: vi.fn(), cancelRun: vi.fn(), edit: vi.fn(), vehicleTypes: vi.fn().mockResolvedValue([{ id: 't1', name: '14ft' }, { id: 't2', name: '32ft' }]) },
}))

const vehicle: PlannedVehicle = {
  key: 'v1', pickupCity: 'Pune', pickupState: 'Maharashtra', mode: 'Ftl', vehicleTypeId: 't1', vehicleTypeName: '14ft', payloadKg: 4000, volumeCapacityCbm: 18,
  contractId: 'c1', contractReference: 'CN-00001', transporterId: 'tr1', transporterName: 'Shree Roadlines', estimatedCost: 12000, costLines: [],
  weightKg: 3000, volumeCbm: 10, weightUtilisation: 0.75, volumeUtilisation: 0.5556,
  orders: [{ orderId: 'o1', number: 'ORD-00001', sequence: 1, dropCity: 'Surat', dropState: 'Gujarat', weightKg: 3000, volumeCbm: 10, kind: 'Delivery' }],
  alternatives: [
    { mode: 'Ftl', vehicleTypeId: 't1', vehicleTypeName: '14ft', contractId: 'c1', contractReference: 'CN-00001', transporterId: 'tr1', transporterName: 'Shree Roadlines', total: 12000, lines: [], chosen: true, verdict: 'Chosen', transitHours: 6.5, meetsDeadline: true,
      transporter: { id: 'tr1', code: 'SRL', name: 'Shree Roadlines', contactPerson: 'Anil', phone: '9800000001', email: null, city: 'Pune' },
      vehicle: { id: 'v1', registration: 'MH12AB1234', typeName: '14ft', payloadKg: 4000, compliance: 'Compliant', issues: [] },
      driver: { id: 'd1', name: 'Ramesh', phone: '9800000002', licenseNumber: 'MH1220200001', compliance: 'Compliant', issues: [] } },
    { mode: 'Ftl', vehicleTypeId: 't0', vehicleTypeName: 'Ace', contractId: null, contractReference: null, transporterId: null, transporterName: null, total: null, lines: [], chosen: false, verdict: 'Payload exceeded by 2250 kg (load 3000 kg, capacity 750 kg).', transitHours: null, meetsDeadline: null, transporter: null, vehicle: null, driver: null },
    { mode: 'Ptl', vehicleTypeId: null, vehicleTypeName: null, contractId: 'c9', contractReference: 'CN-00009', transporterId: 'tr2', transporterName: 'Cheap Carriers', total: 4000, lines: [], chosen: false, verdict: 'Would arrive 03 Oct 20:00, after the 02 Oct deadline.', transitHours: 30.5, meetsDeadline: false, transporter: null, vehicle: null, driver: null },
  ],
  reason: '14ft full truck (75% full) at ₹12,000.00 was chosen for 1 order(s): of 2 priced option(s) it gives the lowest cost.',
  isLocked: false, consolidationSaving: null, shipmentId: null, shipmentNumber: null,
  distanceKm: 300, durationMinutes: 360, routeSource: 'Estimate', transitHours: 6.5, costPerTonneKm: 13.33, plannedDeparture: '2026-10-04T02:30:00Z',
  sequenceMethod: null, additionalKm: null, additionalMinutes: null, separateCost: null, savingPercent: null, backhaulSaving: null, routeNote: null,
  transporter: null, assignedVehicle: null, assignedDriver: null, lengthUtilisation: null, loadedKm: null, emptyKm: null, sequenceLocked: false, assignmentLocked: false,
  stops: [
    { sequence: 0, kind: 'Pickup', label: 'Pune, Maharashtra', latitude: 18.5, longitude: 73.8, plannedArrival: null, plannedDeparture: '2026-10-04T02:30:00Z', waitMinutes: null },
    { sequence: 1, kind: 'Drop', label: 'Surat, Gujarat', latitude: 21.1, longitude: 72.8, plannedArrival: '2026-10-04T08:30:00Z', plannedDeparture: '2026-10-04T09:00:00Z', waitMinutes: 45 },
  ],
}

function run(status: PlanStatus, overrides: Partial<RunDto> = {}): RunDto {
  return {
    id: 'r1', runGroupId: 'g1', number: 'PLN-20261004-001', planVersion: 1, isLatest: true, planningDate: '2026-10-04', status,
    options: { objective: 'MinimizeTotalCost', allowFtl: true, allowPtl: true, allowConsolidation: true, maxStops: 8, timeBudgetSeconds: 20, enforceDeadlines: true, ptlExtraTransitHours: 24, stopServiceMinutes: 30, departureHour: 8, allowBackhaul: true, backhaulChargePercent: 50, maxBackhaulExtraKm: 100, returnsAfterDeliveries: true }, orderIds: ['o1'], reason: null,
    plan: {
      solverStatus: 'Feasible', solverMessage: 'Rule-based plan: it is feasible, not proven optimal.', vehicles: [vehicle],
      unplanned: [{ orderId: 'o2', number: 'ORD-00002', code: 'NO_VALID_RATE', reason: 'No active contract has a rate for this lane.', suggestions: ['Add or extend a contract rate'] }],
      summary: { ordersPlanned: 1, ordersUnplanned: 1, vehiclesUsed: 1, totalCost: 12000, averageWeightUtilisation: 0.75, averageVolumeUtilisation: 0.5556, consolidationSaving: 0, ftlCount: 1, ptlCount: 0, totalDistanceKm: 300, costPerTonneKm: 13.33, backhaulSaving: 0, returnPickups: 0 },
    },
    createdAt: '2026-10-04T09:00:00Z', approvedAt: null, committedAt: null, cancelReason: null, version: 1, ...overrides,
  }
}

function renderRun(r: RunDto) {
  vi.mocked(planningApi.run).mockResolvedValue(r)
  vi.mocked(planningApi.versions).mockResolvedValue([{ id: 'r1', planVersion: r.planVersion, status: r.status, reason: null, createdAt: r.createdAt, summary: r.plan.summary }])
  return renderWithProviders(<Routes><Route path="/planning/runs/:id" element={<PlanRunPage />} /></Routes>, { route: '/planning/runs/r1' })
}

describe('PlanRunPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.permissions = new Set(['shipments.plan', 'shipments.read', 'shipments.approve'])
  })

  it('explains the choice, shows weight and volume fill, and why other options lost', async () => {
    renderRun(run('PartiallyPlanned'))

    expect(await screen.findByText(/was chosen for 1 order/)).toBeInTheDocument()
    expect(screen.getByText('Feasible')).toBeInTheDocument()
    expect(screen.queryByText(/optimal$/i)).not.toBeInTheDocument()
    await userEvent.click(screen.getByText(/Alternatives considered/))
    expect(await screen.findByText(/Payload exceeded by 2250 kg/)).toBeInTheDocument()
  })

  it('shows distance, transit and cost per tonne-km, and labels an estimate as an estimate', async () => {
    renderRun(run('Completed'))

    expect((await screen.findAllByText(/300 km/)).length).toBeGreaterThan(0)
    expect(screen.getByText('Estimate')).toBeInTheDocument()
    expect(screen.queryByText('Road distance')).not.toBeInTheDocument()
    expect(screen.getByText('₹13.33/tonne-km')).toBeInTheDocument()
  })

  it('flags an alternative that would arrive after the deadline', async () => {
    renderRun(run('Completed'))

    await userEvent.click(await screen.findByText(/Alternatives considered/))
    expect(await screen.findByText('Late')).toBeInTheDocument()
    expect(screen.getByText(/after the 02 Oct deadline/)).toBeInTheDocument()
  })

  it('lists every unplanned order with its reason and suggested actions', async () => {
    renderRun(run('PartiallyPlanned'))

    expect(await screen.findByText('ORD-00002')).toBeInTheDocument()
    expect(screen.getByText('No active contract has a rate for this lane.')).toBeInTheDocument()
    expect(screen.getByText(/Add or extend a contract rate/)).toBeInTheDocument()
  })

  it('locks a vehicle on request', async () => {
    vi.mocked(planningApi.lockVehicle).mockResolvedValue(run('PartiallyPlanned'))
    renderRun(run('PartiallyPlanned'))

    await userEvent.click(await screen.findByRole('button', { name: /Lock 14ft/ }))

    expect(planningApi.lockVehicle).toHaveBeenCalledWith('r1', 'v1', true, undefined, undefined)
  })

  it('locks the stop order and the vehicle and driver separately from the whole vehicle', async () => {
    vi.mocked(planningApi.lockVehicle).mockResolvedValue(run('PartiallyPlanned'))
    renderRun(run('PartiallyPlanned'))

    await userEvent.click(await screen.findByRole('button', { name: /Lock stop order of 14ft/ }))
    expect(planningApi.lockVehicle).toHaveBeenLastCalledWith('r1', 'v1', true, 'Sequence', undefined)

    // This vehicle has no assigned vehicle yet, so there is nothing to lock there.
    expect(screen.getByRole('button', { name: /Lock vehicle and driver of 14ft/ })).toBeDisabled()
  })

  it('offers approval only to people with the approve permission', async () => {
    auth.permissions = new Set(['shipments.plan', 'shipments.read'])
    renderRun(run('Completed'))

    await screen.findByText(/was chosen for 1 order/)
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Re-plan/ })).toBeInTheDocument()
  })

  it('shows Commit once approved, and nothing editable on an older version', async () => {
    const { unmount } = renderRun(run('Approved'))
    expect(await screen.findByRole('button', { name: 'Commit to shipments' })).toBeInTheDocument()
    unmount()

    renderRun(run('Completed', { isLatest: false, planVersion: 1 }))
    expect(await screen.findByText(/older version/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Re-plan/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Lock 14ft/ })).not.toBeInTheDocument()
  })

  it('shows what consolidation saved, the extra distance, and the saving from a return load', async () => {
    const consolidated: PlannedVehicle = {
      ...vehicle, separateCost: 20000, savingPercent: 50, additionalKm: 12.5, sequenceMethod: 'Exact', backhaulSaving: 5000,
      routeNote: 'Includes the return to the depot.',
      orders: [
        { orderId: 'o1', number: 'ORD-00001', sequence: 1, dropCity: 'Surat', dropState: 'Gujarat', weightKg: 3000, volumeCbm: 10, kind: 'Delivery' },
        { orderId: 'o9', number: 'ORD-00009', sequence: 2, dropCity: 'Nashik', dropState: 'Maharashtra', weightKg: 1000, volumeCbm: 2, kind: 'ReturnPickup' },
      ],
    }
    const base = run('Completed')
    renderRun({ ...base, plan: { ...base.plan, vehicles: [consolidated], unplanned: [] } })

    expect(await screen.findByText(/saves 50% for \+12.5 km/)).toBeInTheDocument()
    expect(screen.getByText(/Return load saves/)).toBeInTheDocument()
    expect(screen.getByText(/Stop order: best of all orders/)).toBeInTheDocument()
    expect(screen.getByText('2. ORD-00009 (return)')).toBeInTheDocument()
  })

  it('shows progress while the plan is being calculated, and no plan to edit yet', async () => {
    renderRun(run('Running', {
      plan: { ...run('Running').plan, vehicles: [], unplanned: [] },
      startedAt: '2026-10-04T09:00:00Z',
      log: [{ at: '2026-10-04T09:00:00Z', message: 'Queued for planning.' }, { at: '2026-10-04T09:00:02Z', message: 'Planned 3 of 8 group(s): 3 vehicle(s), 0 order(s) unplanned so far.' }],
    }))

    expect(await screen.findByText('Calculating the plan')).toBeInTheDocument()
    expect(screen.getAllByText(/Planned 3 of 8 group/).length).toBeGreaterThan(0)
    expect(screen.getByText('Running')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByText('Edit this vehicle')).not.toBeInTheDocument()
  })

  describe('manual editing', () => {
    const second: PlannedVehicle = {
      ...vehicle, key: 'v2', vehicleTypeName: '32ft',
      orders: [{ orderId: 'o2', number: 'ORD-00002', sequence: 1, dropCity: 'Vapi', dropState: 'Gujarat', weightKg: 9000, volumeCbm: 20, kind: 'Delivery' }],
    }
    const twoVehicles = () => {
      const base = run('Completed')
      return { ...base, plan: { ...base.plan, vehicles: [vehicle, second], unplanned: [] } }
    }

    async function openEditor(user: ReturnType<typeof userEvent.setup>) {
      const headers = await screen.findAllByText('Edit this vehicle')
      await user.click(headers[0]!)
    }

    it('sends a move to the server and opens the new version', async () => {
      vi.mocked(planningApi.edit).mockResolvedValue({ ...run('Completed'), id: 'r2', planVersion: 2 })
      const user = userEvent.setup()
      renderRun(twoVehicles())

      await openEditor(user)
      await user.click(await screen.findByLabelText('Move ORD-00001 to'))
      const options = await screen.findAllByText(/Vehicle 2/, { selector: '.ant-select-item-option-content' })
      await user.click(options[options.length - 1]!)

      const save = await screen.findByRole('button', { name: 'Save change' })
      expect(save).toBeDisabled() // an override needs a reason
      await user.type(screen.getByLabelText('Reason for the change'), 'Same lane')
      await user.click(save)

      expect(planningApi.edit).toHaveBeenCalledWith('r1', { kind: 'MoveOrder', orderId: 'o1', toVehicleKey: 'v2', comment: 'Same lane' })
    })

    it('shows the exact reason when the server refuses a change', async () => {
      vi.mocked(planningApi.edit).mockRejectedValue({ isAxiosError: true, response: { status: 409, data: { code: 'planning.edit_invalid', title: 'Payload exceeded by 3000 kg', detail: '32ft cannot carry this load: Payload exceeded by 3000 kg (load 19000 kg, capacity 16000 kg).' } } })
      const user = userEvent.setup()
      renderRun(twoVehicles())

      await openEditor(user)
      await user.click(await screen.findByRole('button', { name: 'Remove' }))
      await user.type(await screen.findByLabelText('Reason for the change'), 'Not needed')
      await user.click(screen.getByRole('button', { name: 'Save change' }))

      expect(await screen.findByText('That change is not allowed')).toBeInTheDocument()
      expect(screen.getByText(/Payload exceeded by 3000 kg/)).toBeInTheDocument()
    })

    it('offers no editing controls on an older version or to someone who may not plan', async () => {
      auth.permissions = new Set(['shipments.read'])
      renderRun(twoVehicles())

      await screen.findAllByText(/was chosen for 1 order/)
      expect(screen.queryByText('Edit this vehicle')).not.toBeInTheDocument()
    })
  })
})
