import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { milkRunsApi } from '@/lib/api/endpoints'
import type { MilkRunDto, MilkRunPlan } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { MilkRunsTab } from './MilkRunsTab'

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  Can: ({ children }: { children: React.ReactNode }) => children,
  useAuth: () => ({ user: { id: 'u1', transporterId: null }, can: () => true, status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  milkRunsApi: { create: vi.fn(), update: vi.fn(), list: vi.fn(), preview: vi.fn(), commit: vi.fn() },
  planningApi: { vehicleTypes: vi.fn().mockResolvedValue([]) },
  locationsApi: { list: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 1 }) },
}))

const milkRun: MilkRunDto = {
  id: 'm1', code: 'MR-1', name: 'Pune supplier run', depotLocationId: 'l-depot', depotName: 'Pune DC', vehicleTypeId: null, maxStops: 8, maxDurationMinutes: 600,
  departureTime: '07:00:00', days: ['Monday'], isActive: true, version: 1, stops: [],
}

const plan = (trips: number): MilkRunPlan => ({
  code: 'MR-1', name: 'Pune supplier run', date: '2026-10-05', trips: Array.from({ length: trips }, (_, i) => ({ number: i + 1 }) as MilkRunPlan['trips'][number]),
  skipped: [], unplanned: [], totals: { orders: 2, stopsServed: 2, stopsSkipped: 0, trips, inboundKg: 7000, outboundKg: 0, distanceKm: 100, cost: 12000, costPerTonneKm: 1 }, source: 'Estimate', warnings: [],
})

describe('MilkRunsTab commit', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(milkRunsApi.list).mockResolvedValue({ items: [milkRun], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 })
  })

  async function openPlanner(user: ReturnType<typeof userEvent.setup>) {
    renderWithProviders(<Routes><Route path="/" element={<MilkRunsTab />} /></Routes>)
    await user.click(await screen.findByRole('button', { name: 'Plan a day' }))
  }

  it('cannot commit until a day has been calculated, then creates shipments after a confirmation', async () => {
    vi.mocked(milkRunsApi.preview).mockResolvedValue({ ...plan(1), trips: [] } as MilkRunPlan)
    vi.mocked(milkRunsApi.commit).mockResolvedValue({
      code: 'MR-1', date: '2026-10-05', shipments: [{ tripNumber: 1, shipmentId: 's1', shipmentNumber: 'SH-00042', orders: 2, plannedCost: 12000, isCollectionRun: true }],
    })
    const user = userEvent.setup()
    await openPlanner(user)

    expect(await screen.findByRole('button', { name: 'Commit to shipments' })).toBeDisabled()

    vi.mocked(milkRunsApi.preview).mockResolvedValue({ ...plan(1), trips: [{ number: 1, vehicleTypeName: '14ft', alternatives: [], stops: [], warnings: [], cost: 12000, distanceKm: 100, source: 'Estimate' }] } as unknown as MilkRunPlan)
    await user.click(screen.getByRole('button', { name: 'Recalculate for this day' }))
    const commit = await screen.findByRole('button', { name: 'Commit to shipments' })
    await vi.waitFor(() => expect(commit).toBeEnabled())
    await user.click(commit)
    await user.click(await screen.findByRole('button', { name: 'Create shipments' }))

    expect(await screen.findByText(/Created 1 draft shipment/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'SH-00042' })).toHaveAttribute('href', '/shipments/s1')
    expect(screen.getByText(/collection run/)).toBeInTheDocument()
    expect(milkRunsApi.commit).toHaveBeenCalledWith('m1', expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/), true)
  })
})
