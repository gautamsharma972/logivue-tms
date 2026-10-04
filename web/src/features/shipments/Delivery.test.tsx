import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { deliveryApi } from '@/lib/api/endpoints'
import type { AgeingDto, PodLineDto, ShipmentDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { DeliveriesPage } from './DeliveriesPage'
import { PodDrawer, RecordDeliveryModal } from './DeliveryPanel'

const auth = vi.hoisted(() => ({ transporterId: null as string | null, permissions: new Set<string>() }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: { id: 'u1', transporterId: auth.transporterId }, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  deliveryApi: { record: vi.fn(), documents: vi.fn(), upload: vi.fn(), download: vi.fn(), remove: vi.fn(), verify: vi.fn(), reject: vi.fn(), queue: vi.fn(), ageing: vi.fn() },
}))

type Line = ShipmentDto['orders'][number]
const party = { name: 'Acme', line1: 'x', city: 'Surat', state: 'Gujarat', pincode: '395003', contactName: null, contactPhone: null }
const line = (overrides: Partial<Line> = {}): Line => ({
  orderId: 'o1', orderNumber: 'ORD-00001', dropSequence: 1, lrNumber: 'LR-000001', pickup: party, drop: party, weightKg: 1000, volumeCbm: null, description: 'Goods',
  packagesShipped: 40, deliveredAt: '2026-10-04T09:00:00Z', receiverName: 'Store', deliveredPackages: 40, damagedPackages: 0, shortagePackages: 0, podStatus: 'Awaiting', podDocuments: 0, ...overrides,
})

describe('RecordDeliveryModal', () => {
  beforeEach(() => vi.clearAllMocks())

  it('sends the receiver and full quantities by default', async () => {
    vi.mocked(deliveryApi.record).mockResolvedValue({} as ShipmentDto)
    const user = userEvent.setup()
    renderWithProviders(<RecordDeliveryModal shipmentId="s1" line={line({ deliveredAt: null })} onClose={vi.fn()} onDone={vi.fn()} />)

    await user.type(await screen.findByLabelText('Received by'), 'Store manager')
    await user.click(screen.getByRole('button', { name: 'Confirm delivery' }))

    await waitFor(() => expect(deliveryApi.record).toHaveBeenCalled())
    expect(deliveryApi.record).toHaveBeenCalledWith('s1', 'o1', expect.objectContaining({ receiverName: 'Store manager', deliveredPackages: 40, damagedPackages: 0, remarks: null }))
  })

  it('asks what happened as soon as goods are short, and will not send without it', async () => {
    const user = userEvent.setup()
    renderWithProviders(<RecordDeliveryModal shipmentId="s1" line={line({ deliveredAt: null })} onClose={vi.fn()} onDone={vi.fn()} />)

    await user.type(await screen.findByLabelText('Received by'), 'Store manager')
    const received = screen.getByLabelText('Packages received')
    await user.clear(received)
    await user.type(received, '38')

    expect(await screen.findByText(/short or damaged/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Confirm delivery' }))
    expect(await screen.findByText('Say what happened')).toBeInTheDocument()
    expect(deliveryApi.record).not.toHaveBeenCalled()
  })

  it('does not ask for quantities on an order with no package count', async () => {
    renderWithProviders(<RecordDeliveryModal shipmentId="s1" line={line({ deliveredAt: null, packagesShipped: null })} onClose={vi.fn()} onDone={vi.fn()} />)

    expect(await screen.findByText(/no package count/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Packages received')).not.toBeInTheDocument()
  })
})

describe('PodDrawer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(deliveryApi.documents).mockResolvedValue([{ id: 'd1', shipmentId: 's1', orderId: 'o1', fileName: 'signed-lr.pdf', contentType: 'application/pdf', sizeBytes: 2048, uploadedAt: '2026-10-04T10:00:00Z' }])
  })

  const open = (podStatus: Line['podStatus'], canUpload: boolean, canReview: boolean) =>
    renderWithProviders(<PodDrawer shipmentId="s1" line={line({ podStatus, podDocuments: 1 })} canUpload={canUpload} canReview={canReview} onClose={vi.fn()} onChanged={vi.fn()} />)

  it('lets a reviewer verify or reject proof that is waiting, and shows the files', async () => {
    vi.mocked(deliveryApi.verify).mockResolvedValue({} as ShipmentDto)
    const user = userEvent.setup()
    open('Uploaded', false, true)

    expect(await screen.findByText('signed-lr.pdf')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Verify proof' }))

    expect(deliveryApi.verify).toHaveBeenCalledWith('s1', 'o1')
    expect(screen.getByRole('button', { name: /Reject/ })).toBeInTheDocument()
  })

  it('lets the transporter upload and remove files but never review its own proof', async () => {
    open('Uploaded', true, false)

    expect(await screen.findByRole('button', { name: /Upload signed copy/ })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Remove signed-lr.pdf' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verify proof' })).not.toBeInTheDocument()
  })

  it('freezes verified proof: no upload, no removal, no review', async () => {
    open('Verified', true, true)

    await screen.findByText('signed-lr.pdf')
    expect(screen.queryByRole('button', { name: /Upload signed copy/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove signed-lr.pdf' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Verify proof' })).not.toBeInTheDocument()
  })

  it('shows why proof was rejected so the transporter can fix it', async () => {
    renderWithProviders(<PodDrawer shipmentId="s1" line={line({ podStatus: 'Rejected', podRejectionReason: 'Signature not legible' })} canUpload canReview={false} onClose={vi.fn()} onChanged={vi.fn()} />)

    expect(await screen.findByText('Signature not legible')).toBeInTheDocument()
  })
})

describe('DeliveriesPage', () => {
  const row = (overrides: Partial<PodLineDto> = {}): PodLineDto => ({
    shipmentId: 's1', shipmentNumber: 'SH-00001', orderId: 'o1', orderNumber: 'ORD-00001', lrNumber: 'LR-000001', consignee: 'Acme Stores', consigneeCity: 'Surat', transporterId: 't1',
    transporterName: 'Shree Roadlines', stage: 'AwaitingProof', deliveredAt: '2026-09-20T09:00:00Z', receiverName: 'Store', packagesShipped: 40, deliveredPackages: 38, damagedPackages: 0,
    shortagePackages: 2, hasException: true, ageDays: 14, overdue: true, rejectionReason: null, documents: 0, ...overrides,
  })
  const ageing: AgeingDto = {
    overdueDays: 7, outstanding: 12, overdue: 5, withExceptions: 2, buckets: [{ label: '0–3 days', count: 4 }, { label: '4–7 days', count: 3 }, { label: '8–15 days', count: 3 }, { label: '16–30 days', count: 1 }, { label: 'Over 30 days', count: 1 }],
    transporters: [{ transporterId: 't1', name: 'Shree Roadlines', outstanding: 8, overdue: 4, oldestDays: 40 }],
  }

  beforeEach(() => {
    vi.clearAllMocks()
    auth.transporterId = null
    auth.permissions = new Set(['shipments.read', 'shipments.plan'])
    vi.mocked(deliveryApi.ageing).mockResolvedValue(ageing)
    vi.mocked(deliveryApi.queue).mockResolvedValue({ items: [row()], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 })
  })

  it('shows ageing and flags an overdue delivery', async () => {
    renderWithProviders(<DeliveriesPage />)

    expect(await screen.findByText('Proof outstanding')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(await screen.findByText('SH-00001')).toBeInTheDocument()
    expect(screen.getByText('14 d')).toBeInTheDocument()
    expect(screen.getByText('Proof awaited')).toBeInTheDocument() // the shortage tag sits in a column that only shows on wider screens
    expect(screen.getByText('Who is slowest to send proof')).toBeInTheDocument()
  })

  it('shows a vendor only their own worklist, with no staff report and no transporter column', async () => {
    auth.transporterId = 't1'
    auth.permissions = new Set(['shipments.respond'])
    renderWithProviders(<DeliveriesPage />)

    expect(await screen.findByText('SH-00001')).toBeInTheDocument()
    expect(deliveryApi.ageing).not.toHaveBeenCalled()
    expect(screen.queryByText('Proof outstanding')).not.toBeInTheDocument()
    expect(screen.queryByText('Transporter')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Deliveries' })).toBeInTheDocument()
  })

  it('filters to overdue proof', async () => {
    const user = userEvent.setup()
    renderWithProviders(<DeliveriesPage />)

    await screen.findByText('SH-00001')
    await user.click(screen.getByRole('switch', { name: 'Overdue only' }))

    await waitFor(() => expect(deliveryApi.queue).toHaveBeenLastCalledWith(expect.objectContaining({ overdueOnly: true })))
  })
})
