import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shipmentsApi } from '@/lib/api/endpoints'
import type { ShipmentDto, TenderDto, TenderInviteeDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { TenderPanel } from './TenderPanel'

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  shipmentsApi: { tenders: vi.fn(), awardTender: vi.fn(), decideCounter: vi.fn(), cancelTender: vi.fn(), bid: vi.fn(), counterOffer: vi.fn(), declineTender: vi.fn(), fleetOptions: vi.fn() },
}))

const invitee = (o: Partial<TenderInviteeDto>): TenderInviteeDto => ({
  id: 'i1', transporterId: 't1', transporterName: 'Shree Roadlines', sequence: 1, status: 'Sent', sentAt: '2026-10-04T09:00:00Z', deadline: '2026-10-04T13:00:00Z', respondedAt: null,
  reason: null, contractReference: 'CN-00001', quotedTotal: 30000, counterRate: null, counterComment: null, counterStatus: 'None', agreedRate: null,
  bidVehicleId: null, bidVehicleRegistration: null, bidDriverId: null, bidDriverName: null, ...o,
})

const tender = (o: Partial<TenderDto>): TenderDto => ({
  id: 'td1', number: 'TND-00001', shipmentId: 's1', shipmentNumber: 'SH-00001', mode: 'Broadcast', status: 'Open', responseMinutes: 240, notes: null, awardedTransporterId: null,
  closedAt: null, closeReason: null, createdAt: '2026-10-04T09:00:00Z', invitees: [invitee({})], events: [], ...o,
})

const shipment = { summary: { id: 's1', number: 'SH-00001' } } as ShipmentDto

describe('TenderPanel', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lets a planner award a bid and agree a counter-offer but only after the counter is decided', async () => {
    vi.mocked(shipmentsApi.tenders).mockResolvedValue([tender({
      invitees: [
        invitee({ id: 'a', transporterName: 'Alpha Carriers', status: 'Bid', bidVehicleRegistration: 'MH12AB1234', bidDriverName: 'Ramesh' }),
        invitee({ id: 'b', transporterName: 'Beta Freight', sequence: 2, status: 'Bid', counterRate: 28000, counterStatus: 'Pending' }),
      ],
    })])
    vi.mocked(shipmentsApi.awardTender).mockResolvedValue(tender({ status: 'Awarded' }))
    const user = userEvent.setup()
    renderWithProviders(<TenderPanel shipment={shipment} canPlan canRespond isVendor={false} />)

    expect(await screen.findByText('Tender TND-00001')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Award' })).toHaveLength(1) // Beta's counter-offer must be decided first
    expect(screen.getByRole('button', { name: 'Agree rate' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Award' }))
    expect(shipmentsApi.awardTender).toHaveBeenCalledWith('s1', 'a')
  })

  it('shows a vendor only its own invitation, with bidding and counter-offer actions and no price', async () => {
    vi.mocked(shipmentsApi.tenders).mockResolvedValue([tender({ invitees: [invitee({ contractReference: null, quotedTotal: null })] })])
    renderWithProviders(<TenderPanel shipment={shipment} canPlan={false} canRespond isVendor />)

    expect(await screen.findByRole('button', { name: 'Bid…' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Propose a different rate…' })).toBeInTheDocument()
    expect(screen.queryByText('Contract price')).not.toBeInTheDocument()
    expect(screen.queryByText('Cancel tender')).not.toBeInTheDocument()
  })

  it('renders nothing for a shipment that was never put to tender', async () => {
    vi.mocked(shipmentsApi.tenders).mockResolvedValue([])
    const { container } = renderWithProviders(<TenderPanel shipment={shipment} canPlan canRespond isVendor={false} />)
    await vi.waitFor(() => expect(shipmentsApi.tenders).toHaveBeenCalled())
    expect(container.textContent).toBe('')
  })
})
