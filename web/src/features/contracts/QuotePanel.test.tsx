import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { contractsApi } from '@/lib/api/endpoints'
import type { QuoteResultDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { QuotePanel } from './QuotePanel'

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  contractsApi: { quote: vi.fn(), vehicleTypes: vi.fn().mockResolvedValue([]) },
}))

const result: QuoteResultDto = {
  date: '2026-10-04',
  message: null,
  quotes: [
    {
      contractId: 'c1', contractReference: 'CN-00001', transporterId: 't1', transporterName: 'Shree Roadlines', type: 'Ftl',
      lane: 'PUNE, MAHARASHTRA → SURAT, GUJARAT', chargeableWeightKg: null, notes: ['Fuel adjustment +2.5% (diesel ₹95.00 vs base ₹90.00)'],
      lines: [
        { code: 'FREIGHT', description: 'Flat rate for the trip', amount: 41500 },
        { code: 'FUEL', description: 'Fuel adjustment +2.5%', amount: 1037.5 },
      ],
      total: 42537.5,
    },
  ],
}

async function choose(user: ReturnType<typeof userEvent.setup>, label: string, option: string) {
  await user.click(screen.getByLabelText(label))
  // Closed dropdowns stay in the DOM; the one just opened is the last.
  const matches = await screen.findAllByText(option, { selector: '.ant-select-item-option-content' })
  await user.click(matches[matches.length - 1]!)
}

describe('QuotePanel', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows exact paise: a quote of ₹42,537.50 must not be displayed as ₹42,538', async () => {
    vi.mocked(contractsApi.quote).mockResolvedValue(result)
    const user = userEvent.setup()
    renderWithProviders(<QuotePanel contractId="c1" contractType="Ftl" />)

    await choose(user, 'Pickup state', 'Maharashtra')
    await choose(user, 'Delivery state', 'Gujarat')
    await user.click(screen.getByRole('button', { name: /Get quotes/ }))

    expect(await screen.findAllByText('₹42,537.50')).not.toHaveLength(0)
    expect(screen.queryByText('₹42,538')).not.toBeInTheDocument()
    expect(screen.getByText('₹1,037.50')).toBeInTheDocument()
  })

  it('does not call the API until a pickup and delivery state are chosen', async () => {
    const user = userEvent.setup()
    renderWithProviders(<QuotePanel />)

    await user.click(screen.getByRole('button', { name: /Get quotes/ }))

    expect(await screen.findAllByText('Choose the state')).toHaveLength(2)
    expect(contractsApi.quote).not.toHaveBeenCalled()
  })

  it('tests a single contract in preview mode so drafts can be checked', async () => {
    vi.mocked(contractsApi.quote).mockResolvedValue(result)
    const user = userEvent.setup()
    renderWithProviders(<QuotePanel contractId="c1" contractType="Ftl" />)

    await choose(user, 'Pickup state', 'Maharashtra')
    await choose(user, 'Delivery state', 'Gujarat')
    await user.click(screen.getByRole('button', { name: /Get quotes/ }))
    await screen.findAllByText('₹42,537.50')

    expect(contractsApi.quote).toHaveBeenCalledWith(expect.objectContaining({ contractId: 'c1', preview: true, type: 'Ftl' }))
  })
})
