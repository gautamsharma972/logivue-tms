import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { planningApi } from '@/lib/api/endpoints'
import type { ComparisonDto, PlanAlternative } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { defaultPlanOptions } from './shared'
import { ComparisonModal } from './ComparisonModal'

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  planningApi: { compare: vi.fn() },
}))

const option = (over: Partial<PlanAlternative>): PlanAlternative => ({
  mode: 'Ftl', vehicleTypeId: 't1', vehicleTypeName: '14ft', contractId: 'c1', contractReference: 'CN-1', transporterId: 'tr1', transporterName: 'Shree', total: 12000,
  lines: [{ code: 'FREIGHT', description: 'Base freight', amount: 11000 }, { code: 'LOADING', description: 'Loading charge', amount: 1000 }],
  chosen: false, verdict: 'Chosen', transitHours: null, meetsDeadline: null, transporter: null, vehicle: null, driver: null, ...over,
})

const comparison: ComparisonDto = {
  alternatives: [
    option({ chosen: true }),
    option({ mode: 'Ptl', vehicleTypeId: null, vehicleTypeName: null, total: 14000, lines: [{ code: 'PTL', description: 'Part load rate', amount: 14000 }], verdict: 'Costs ₹2,000.00 more.' }),
  ],
  recommended: option({ chosen: true }), reason: 'The full truck is cheaper.', sizing: [], ftlTotal: 12000, ptlTotal: 14000, saving: 2000, recommendedMode: 'Ftl',
}

describe('ComparisonModal', () => {
  beforeEach(() => {
    vi.mocked(planningApi.compare).mockResolvedValue(comparison)
  })

  it('shows what each price is made of', async () => {
    const user = userEvent.setup()
    renderWithProviders(<ComparisonModal orderIds={['o1']} options={defaultPlanOptions} onClose={vi.fn()} />)

    expect(await screen.findByText(/Recommended: Full truck/)).toBeInTheDocument()
    await user.click((await screen.findAllByRole('button', { name: /expand row/i }))[0]!)

    expect(await screen.findByText('Base freight')).toBeInTheDocument()
    expect(screen.getByText('Loading charge')).toBeInTheDocument()
  })

  it('accepts the recommendation with one click', async () => {
    const onDecide = vi.fn()
    const user = userEvent.setup()
    renderWithProviders(<ComparisonModal orderIds={['o1']} options={defaultPlanOptions} onClose={vi.fn()} onDecide={onDecide} />)

    await user.click(await screen.findByRole('button', { name: 'Accept recommendation' }))

    expect(onDecide).toHaveBeenCalledWith({ mode: 'Ftl', override: false })
  })

  it('overrides only with another option and a reason', async () => {
    const onDecide = vi.fn()
    const user = userEvent.setup()
    renderWithProviders(<ComparisonModal orderIds={['o1']} options={defaultPlanOptions} onClose={vi.fn()} onDecide={onDecide} />)

    await user.click(await screen.findByRole('button', { name: 'Override…' }))
    const go = screen.getByRole('button', { name: 'Override and plan' })
    expect(go).toBeDisabled()

    await user.click(screen.getByRole('radio', { name: /Part load/ }))
    expect(go).toBeDisabled() // still no reason
    await user.type(screen.getByLabelText('Reason for overriding'), 'Customer wants it shared')
    await user.click(go)

    expect(onDecide).toHaveBeenCalledWith({ mode: 'Ptl', override: true, reason: 'Customer wants it shared' })
  })

  it('is read-only when no decision is expected', async () => {
    renderWithProviders(<ComparisonModal orderIds={['o1']} options={defaultPlanOptions} onClose={vi.fn()} />)

    await screen.findByText(/Recommended: Full truck/)
    expect(screen.queryByRole('button', { name: 'Accept recommendation' })).not.toBeInTheDocument()
  })
})
