import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { deliveriesApi } from '@/lib/api/endpoints'
import type { DeliveryDto, MobileBundleDto, OcrResultDto, PodDto, UserProfile } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { DeliveriesListPage } from './DeliveriesListPage'
import { ExceptionsPage } from './ExceptionsPage'
import { MobileDeliveriesPage } from './MobileDeliveriesPage'
import { MobileDeliveryPage } from './MobileDeliveryPage'
import { OfflineProvider } from './offline/OfflineProvider'
import { memoryStore, type QueuedCommand } from './offline/store'
import type { SyncApi } from './offline/sync'
import { PodDetailPage } from './PodDetailPage'

const auth = vi.hoisted(() => ({ user: null as unknown, permissions: new Set<string>() }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: auth.user, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
  Can: ({ permission, children }: { permission: string; children: React.ReactNode }) => (auth.permissions.has(permission) ? children : null),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  transportersApi: { list: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 }) },
  deliveriesApi: {
    list: vi.fn(), get: vi.fn(), exceptions: vi.fn(), exception: vi.fn(), pod: vi.fn(), podForReview: vi.fn(), reviewPod: vi.fn(), reviewOcrField: vi.fn(), requestCorrection: vi.fn(),
    raiseException: vi.fn(), fetchFile: vi.fn(), evidenceUrl: (id: string) => `/e/${id}`, signatureUrl: (id: string) => `/s/${id}`, verifyOtp: vi.fn(), acknowledgeException: vi.fn(),
    sync: vi.fn(), mobileDeliveries: vi.fn(), addEvidence: vi.fn(), addSignature: vi.fn(), submitPod: vi.fn(),
    attachToException: vi.fn(), downloadExceptionAttachment: vi.fn(), createClaims: vi.fn(), billing: vi.fn(),
  },
}))

const profile = (transporterId: string | null): UserProfile => ({ id: 'u1', email: 'a@b.c', fullName: 'User', type: transporterId ? 'Transporter' : 'Internal', transporterId, tenantCode: 'DEMO', tenantName: 'Demo', roles: [], permissions: [], mustChangePassword: false })

const delivery = (over: Partial<DeliveryDto['summary']> = {}): DeliveryDto => ({
  summary: {
    id: 'd1', number: 'DLV-10025', shipmentReference: 'SH10025', customerName: 'ABC Distributors', destinationReference: 'Surat', transporterId: 't1', transporterReference: 'Shree Roadlines', vehicleReference: 'MH12AB1234',
    plannedDeliveryAt: '2026-10-05T10:00:00Z', actualDeliveryAt: null, status: 'Assigned', outcome: null, podStatus: 'Pending', podId: null, hasDiscrepancy: false, openExceptions: 0, ...over,
  },
  orderReference: 'INV9001', loadReference: null, tripReference: null, lrNumber: 'LR-1', sequence: 1, driverName: 'Ramesh', customerReference: null, customerPhone: null, customerEmail: null, originReference: 'Pune',
  destinationAddress: 'Plot 1, Surat', customerLatitude: null, customerLongitude: null, geofenceRadiusM: null, windowStart: '2026-10-05T09:00:00Z', windowEnd: '2026-10-05T12:00:00Z', actualArrivalAt: null,
  remainingDisposition: null, hasQuantityMismatch: false, otpIssued: false, otpVerified: false,
  items: [{ id: 'i1', sku: 'SKU-001', description: 'Widgets', orderedQuantity: 100, dispatchedQuantity: 100, deliveredQuantity: null, shortQuantity: 0, damagedQuantity: 0, rejectedQuantity: 0, unitOfMeasure: 'PKG', remarks: null, shortageReasonCode: null, damageType: null, damageReason: null, damageDescription: null, unaccounted: null }],
  attempts: [], events: [], discrepancies: [], reconciliation: [], version: 1,
})

const config: MobileBundleDto['config'] = {
  pod: { signatureRequired: false, otpRequired: false, gpsRequired: true, photoRequired: true, minPhotos: 1, geofenceRequired: false, contactlessAllowed: true, galleryAllowed: false, maxGpsAccuracyM: 100, otpValidityMinutes: 30, otpMaxAttempts: 5 },
  attemptReasons: [{ code: 'CUSTOMER_UNAVAILABLE', name: 'Customer unavailable', evidenceRequired: false }],
  shortageReasons: [{ code: 'SHORT_LOADED', name: 'Short loaded', evidenceRequired: false }],
  damageTypes: [{ code: 'BROKEN', name: 'Broken', evidenceRequired: true }],
  refusalReasons: [{ code: 'WRONG_ITEM', name: 'Wrong item', evidenceRequired: false }],
  quantity: { overDeliveryPct: 0, blockUnreconciledCompletion: false },
  discrepancy: { shortageAcknowledgementRequired: false, damageAcknowledgementRequired: false, refusalAcknowledgementRequired: false, autoCreateClaim: false },
  images: { maxBytes: 10485760, minWidth: 320, minHeight: 240, rejectLowResolution: false },
}

const networkDown = () => Object.assign(new Error('Network Error'), { code: 'ERR_NETWORK' })

function ocr(): OcrResultDto {
  return {
    id: 'o1', evidenceId: 'ev-doc', provider: 'text-layer', status: 'Completed', overallConfidence: 0.88, queuedAt: '2026-10-05T10:00:00Z', processedAt: '2026-10-05T10:00:02Z', error: null,
    fields: [
      { name: 'Shipment Number', rawValue: 'SH-10025', normalizedValue: 'SH10025', confidence: 0.98, status: 'Matched', message: null, reviewedValue: null, effectiveValue: 'SH10025', expected: 'SH10025', threshold: 0.95 },
      { name: 'Delivered Quantity', rawValue: '92', normalizedValue: '92', confidence: 0.64, status: 'LowConfidence', message: 'Read with 64% confidence, below the 85% needed.', reviewedValue: null, effectiveValue: '92', expected: '95', threshold: 0.85 },
    ],
  }
}

function pod(over: Partial<PodDto['summary']> = {}, extra: Partial<PodDto> = {}): PodDto {
  return {
    summary: { id: 'p1', podNumber: 'POD-10025', version: 1, isCurrent: true, deliveryId: 'd1', deliveryNumber: 'DLV-10025', customerName: 'ABC Distributors', transporterReference: 'Shree Roadlines', status: 'UnderReview', deliveredAt: '2026-10-05T10:40:00Z', submittedAt: '2026-10-05T11:00:00Z', approvedAt: null, hoursSinceSubmitted: 2, validation: 'RequiresReview', hasDiscrepancy: true, ocr: 'Completed', ...over },
    method: 'Signature', recipientName: 'Anil Kumar', recipientDesignation: 'Manager', recipientPhone: null, arrivalAt: null, capturedAt: '2026-10-05T10:40:00Z', reviewedAt: null, latitude: 18.52, longitude: 73.85, gpsAccuracy: 10,
    geofence: 'NotApplicable', driverRemarks: null, recipientRemarks: null, driverConfirmed: false, otpVerified: false, customerAcknowledged: true, rejectionReason: null, rejectionCount: 0, autoAccepted: false,
    items: [{ deliveryItemId: 'i1', sku: 'SKU-001', orderedQuantity: 100, dispatchedQuantity: 100, deliveredQuantity: 95, shortQuantity: 3, damagedQuantity: 2, rejectedQuantity: 0, remarks: null }],
    evidence: [{ id: 'ev-doc', type: 'PodDocument', fileName: 'pod.pdf', contentType: 'application/pdf', sizeBytes: 1000, fileHash: 'h', capturedAt: '2026-10-05T10:40:00Z', latitude: null, longitude: null, deviceReference: null, width: null, height: null, warnings: null, removed: false, removedReason: null }],
    signatures: [], validations: [
      { type: 'Evidence', check: 'Signature', status: 'Valid', message: 'A signature was captured.', validatedAt: '2026-10-05T11:00:00Z' },
      { type: 'Business', check: 'Discrepancy', status: 'RequiresReview', message: 'A shortage, damage or rejected quantity was recorded: a reviewer confirms it.', validatedAt: '2026-10-05T11:00:00Z' },
    ],
    reviews: [], ocr: [ocr()], missing: [], rowVersion: 1, ...extra,
  }
}

describe('PodDetailPage (the review workbench)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.user = profile(null)
    auth.permissions = new Set(['deliveries.read', 'deliveries.pod.review'])
    vi.mocked(deliveriesApi.fetchFile).mockResolvedValue(new Blob(['x']))
    URL.createObjectURL = vi.fn(() => 'blob:x')
    URL.revokeObjectURL = vi.fn()
  })

  const renderWorkbench = (p = pod()) => {
    vi.mocked(deliveriesApi.podForReview).mockResolvedValue({ pod: p, delivery: delivery(), documentEvidenceId: 'ev-doc', ocr: p.ocr[0] ?? null })
    return renderWithProviders(<Routes><Route path="/delivery/pods/:id" element={<PodDetailPage />} /></Routes>, { route: '/delivery/pods/p1' })
  }

  it('shows the paper beside what was read, with confidence and a clear mark for each field', async () => {
    renderWorkbench()

    expect(await screen.findByText('POD-10025')).toBeInTheDocument()
    expect(await screen.findByText('Delivered Quantity')).toBeInTheDocument()
    expect(screen.getByText('98%')).toBeInTheDocument()
    expect(screen.getByText('64%')).toBeInTheDocument()
    expect(screen.getByText('Matches')).toBeInTheDocument()
    expect(screen.getByText('Unsure')).toBeInTheDocument()
    expect(screen.getByText('A shortage, damage or rejected quantity was recorded: a reviewer confirms it.')).toBeInTheDocument()
    expect(screen.getByTestId('original')).toBeInTheDocument()
  })

  it('records a reviewer\'s correction only with a reason, then lets the proof be accepted', async () => {
    vi.mocked(deliveriesApi.reviewOcrField).mockResolvedValue(pod())
    vi.mocked(deliveriesApi.reviewPod).mockResolvedValue(pod({ status: 'Accepted' }))
    const user = userEvent.setup()
    renderWorkbench()

    await user.click(await screen.findByRole('button', { name: 'Edit Delivered Quantity' }))
    const dialog = await screen.findByRole('dialog')
    const value = within(dialog).getByLabelText('Corrected value')
    await user.clear(value)
    await user.type(value, '95')
    expect(within(dialog).getByRole('button', { name: 'Save correction' })).toBeDisabled()
    await user.type(within(dialog).getByLabelText('Why is it being changed'), 'Paper 5 read as 2')
    await user.click(within(dialog).getByRole('button', { name: 'Save correction' }))
    expect(deliveriesApi.reviewOcrField).toHaveBeenCalledWith('p1', 'Delivered Quantity', '95', 'Paper 5 read as 2')

    await user.click(screen.getByRole('button', { name: 'Accept' }))
    expect(deliveriesApi.reviewPod).toHaveBeenCalledWith('p1', 'accept', null)
  })

  it('cannot accept a proof that fails a mandatory check, but can reject it with a reason', async () => {
    vi.mocked(deliveriesApi.reviewPod).mockResolvedValue(pod({ status: 'Rejected' }))
    const user = userEvent.setup()
    const failing = pod({}, { validations: [{ type: 'Evidence', check: 'Signature', status: 'Invalid', message: 'A signature is required and was not captured.', validatedAt: '2026-10-05T11:00:00Z' }] })
    renderWorkbench(failing)

    expect(await screen.findByRole('button', { name: 'Accept' })).toBeDisabled()
    expect(screen.getByText(/cannot be accepted/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Reject' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Reject' })).toBeDisabled()
    await user.type(within(dialog).getByLabelText('Reason'), 'No signature')
    await user.click(within(dialog).getByRole('button', { name: 'Reject' }))
    expect(deliveriesApi.reviewPod).toHaveBeenCalledWith('p1', 'reject', 'No signature')
  })

  it('does not offer review actions to someone who cannot review, and a vendor sees the proof read-only', async () => {
    auth.user = profile('t1')
    auth.permissions = new Set(['deliveries.execute'])
    vi.mocked(deliveriesApi.pod).mockResolvedValue(pod({ status: 'Rejected' }, { rejectionReason: 'Photo is of the wrong site' }))
    renderWithProviders(<Routes><Route path="/delivery/pods/:id" element={<PodDetailPage />} /></Routes>, { route: '/delivery/pods/p1' })

    expect(await screen.findByText(/Photo is of the wrong site/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Accept' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Add evidence and submit/ })).toBeInTheDocument()
  })

  it('starts a new version to correct an accepted proof', async () => {
    vi.mocked(deliveriesApi.requestCorrection).mockResolvedValue(pod({ version: 2, status: 'Draft' }))
    const user = userEvent.setup()
    renderWorkbench(pod({ status: 'Accepted', approvedAt: '2026-10-05T12:00:00Z' }))

    await user.click(await screen.findByRole('button', { name: 'Request correction' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Reason'), 'Misspelt name')
    await user.click(within(dialog).getByRole('button', { name: 'Start a new version' }))
    expect(deliveriesApi.requestCorrection).toHaveBeenCalledWith('p1', 'Misspelt name')
  })
})

describe('DeliveriesListPage and ExceptionsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.user = profile(null)
    auth.permissions = new Set(['deliveries.read', 'deliveries.manage', 'deliveries.exceptions.manage'])
  })

  it('lists deliveries with their proof status and issues, and filters on the server', async () => {
    vi.mocked(deliveriesApi.list).mockResolvedValue({ items: [delivery({ status: 'PartiallyDelivered', podStatus: 'UnderReview', hasDiscrepancy: true, openExceptions: 2 }).summary], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 })
    const user = userEvent.setup()
    renderWithProviders(<DeliveriesListPage />)

    expect(await screen.findByText('DLV-10025')).toBeInTheDocument()
    expect(screen.getByText('Partially delivered')).toBeInTheDocument()
    expect(screen.getByText('Under review')).toBeInTheDocument()
    expect(screen.getByText('2 open')).toBeInTheDocument()

    await user.click(screen.getByLabelText('Has discrepancy'))
    await waitFor(() => expect(deliveriesApi.list).toHaveBeenLastCalledWith(expect.objectContaining({ hasDiscrepancy: true })))

    await user.type(screen.getByPlaceholderText('Vehicle'), 'MH12')
    await user.type(screen.getByPlaceholderText('Lane (from or to)'), 'Surat')
    await waitFor(() => expect(deliveriesApi.list).toHaveBeenLastCalledWith(expect.objectContaining({ vehicle: 'MH12', lane: 'Surat' })))
  })

  it('keeps a file with an exception and offers a claim for a shortage', async () => {
    const summary = { id: 'x1', number: 'EXC-00007', deliveryId: 'd1', deliveryNumber: 'DLV-10025', customerName: 'ABC Distributors', transporterReference: 'Shree Roadlines', vehicleReference: null, podId: null, type: 'Shortage' as const, severity: 'High' as const, status: 'Open' as const, ownerUserId: null, department: null, raisedAt: '2026-10-03T10:00:00Z', dueAt: '2026-10-09T10:00:00Z', overdue: false, ageHours: 6, claimReference: null }
    const detail = { summary, description: 'Short by 3', rootCause: null, responsibleParty: 'Unknown' as const, actionTaken: null, resolution: null, financialImpact: null, resolvedAt: null, escalatedAt: null, notes: [], version: 1,
      attachments: [{ id: 'a1', fileName: 'gate.jpg', contentType: 'image/jpeg', sizeBytes: 20480, note: 'Locked gate', at: '2026-10-03T11:00:00Z', by: null }] }
    vi.mocked(deliveriesApi.exceptions).mockResolvedValue({ items: [summary], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 })
    vi.mocked(deliveriesApi.exception).mockResolvedValue(detail)
    vi.mocked(deliveriesApi.attachToException).mockResolvedValue(detail)
    vi.mocked(deliveriesApi.createClaims).mockResolvedValue([{ discrepancyId: 'q1', sku: 'SKU-001', type: 'Shortage', quantity: 3, reference: 'CLM-1', system: 'local' }])
    const user = userEvent.setup()
    renderWithProviders(<ExceptionsPage />)

    await user.click(await screen.findByText('EXC-00007'))
    expect(await screen.findByText('gate.jpg')).toBeInTheDocument()
    expect(screen.getByText(/Locked gate/)).toBeInTheDocument()

    await user.upload(screen.getByLabelText('Choose a file'), new File(['x'], 'bay.png', { type: 'image/png' }))
    await waitFor(() => expect(deliveriesApi.attachToException).toHaveBeenCalledWith('x1', expect.any(File), undefined))

    await user.click(screen.getByRole('button', { name: 'Create claim' }))
    await waitFor(() => expect(deliveriesApi.createClaims).toHaveBeenCalledWith('d1', null))
  })

  it('shows exceptions with age and overdue marks and lets an owner resolve one only with a resolution', async () => {
    const summary = { id: 'x1', number: 'EXC-00007', deliveryId: 'd1', deliveryNumber: 'DLV-10025', customerName: 'ABC Distributors', transporterReference: 'Shree Roadlines', vehicleReference: null, podId: null, type: 'Shortage' as const, severity: 'High' as const, status: 'Open' as const, ownerUserId: null, department: null, raisedAt: '2026-10-03T10:00:00Z', dueAt: '2026-10-04T10:00:00Z', overdue: true, ageHours: 60, claimReference: null }
    vi.mocked(deliveriesApi.exceptions).mockResolvedValue({ items: [summary], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 })
    vi.mocked(deliveriesApi.exception).mockResolvedValue({ summary, description: 'Short by 3 on DLV-10025', rootCause: null, responsibleParty: 'Unknown', actionTaken: null, resolution: null, financialImpact: null, resolvedAt: null, escalatedAt: null, notes: [], version: 1 })
    const user = userEvent.setup()
    renderWithProviders(<ExceptionsPage />)

    expect(await screen.findByText('EXC-00007')).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /EXC-00007/ })
    expect(within(row).getByText('Overdue')).toBeInTheDocument()
    expect(within(row).getByText('3 d')).toBeInTheDocument()

    await user.click(screen.getByText('EXC-00007'))
    expect(await screen.findByText('Short by 3 on DLV-10025')).toBeInTheDocument()
    expect(screen.getByText('Not yet established')).toBeInTheDocument() // blame is a finding, never a default
    expect(screen.getByRole('button', { name: 'Resolve' })).toBeDisabled()
    await user.type(screen.getByLabelText('Resolution'), 'Credit note issued')
    expect(screen.getByRole('button', { name: 'Resolve' })).toBeEnabled()
  })
})

describe('the driver\'s phone screens', () => {
  const bundle = (d: DeliveryDto = delivery()): MobileBundleDto => ({ deliveries: [{ delivery: d, pod: null }], config, downloadedAt: '2026-10-05T08:00:00Z' })

  function renderDriver(d: DeliveryDto, api: SyncApi, route = '/driver/d1') {
    auth.user = profile('t1')
    auth.permissions = new Set(['deliveries.execute'])
    const store = memoryStore()
    void store.put('bundle', { id: 'bundle', bundle: bundle(d) })
    return {
      store,
      ...renderWithProviders(
        <OfflineProvider store={store} api={api}>
          <Routes>
            <Route path="/driver" element={<MobileDeliveriesPage />} />
            <Route path="/driver/:id" element={<MobileDeliveryPage />} />
          </Routes>
        </OfflineProvider>,
        { route },
      ),
    }
  }

  const offlineApi = (): SyncApi => ({
    sync: vi.fn().mockRejectedValue(networkDown()),
    addEvidence: vi.fn(), addSignature: vi.fn(), submitPod: vi.fn(),
    download: vi.fn().mockRejectedValue(networkDown()),
  })

  beforeEach(() => {
    vi.clearAllMocks()
    URL.createObjectURL = vi.fn(() => 'blob:photo')
    URL.revokeObjectURL = vi.fn()
  })

  it('works with no signal: each step is saved on the phone and the screen moves on without waiting', async () => {
    const api = offlineApi()
    const user = userEvent.setup()
    const { store } = renderDriver(delivery(), api)

    await user.click(await screen.findByRole('button', { name: 'Start delivery' }))
    expect(await screen.findByRole('button', { name: 'I have arrived' })).toBeInTheDocument() // the screen did not wait for the server
    await user.click(screen.getByRole('button', { name: 'I have arrived' }))

    expect(await screen.findByText('What happened?')).toBeInTheDocument()
    expect(screen.getAllByText(/saved on this phone, waiting to be sent|No signal/).length).toBeGreaterThan(0)
    const commands = await store.list<QueuedCommand>('command')
    expect(commands.map((c) => c.type)).toEqual(['start', 'arrive'])
    expect(api.sync).toHaveBeenCalled()
  })

  it('confirms a delivery offline: the quantities, the photo and the submission are all queued', async () => {
    const api = offlineApi()
    const user = userEvent.setup()
    const { store } = renderDriver(delivery({ status: 'Arrived' }), api)

    await user.type(await screen.findByLabelText('Recipient name'), 'Anil Kumar')
    const photo = new File(['jpeg'], 'p.jpg', { type: 'image/jpeg' })
    await user.click(screen.getByRole('button', { name: /Goods/ }))
    await user.upload(screen.getByLabelText('Take a photo'), photo)
    await user.click(screen.getByRole('button', { name: 'Confirm delivery' }))

    await waitFor(async () => expect((await store.list('command')).length).toBe(1))
    const [complete] = await store.list<QueuedCommand>('command')
    const payload = complete!.payload as { outcome: string; items: { deliveredQuantity: number }[]; proof: { recipientName: string } }
    expect(complete!.type).toBe('complete')
    expect(payload.outcome).toBe('Full')
    expect(payload.items[0]!.deliveredQuantity).toBe(100)
    expect(payload.proof.recipientName).toBe('Anil Kumar')
    expect(await store.list('upload')).toHaveLength(1)
    expect(await store.list('submit')).toHaveLength(1)
  })

  it('will not confirm quantities that do not add up, or a delivery with no photo when one is needed', async () => {
    const user = userEvent.setup()
    const { store } = renderDriver(delivery({ status: 'Arrived' }), offlineApi())

    await user.type(await screen.findByLabelText('Recipient name'), 'Anil Kumar')
    await user.click(screen.getByRole('button', { name: 'Confirm delivery' }))
    expect(await screen.findByText('Take a photo of the delivery')).toBeInTheDocument()

    await user.click(screen.getByLabelText('Shortage'))
    const delivered = screen.getByLabelText('Delivered SKU-001')
    await user.clear(delivered)
    await user.type(delivered, '90')
    expect(await screen.findByText('10 not accounted for')).toBeInTheDocument()
    expect(await store.list('command')).toHaveLength(0)
  })

  it('shows a command the server could not accept, with a way to try again', async () => {
    const api = offlineApi()
    const user = userEvent.setup()
    const { store } = renderDriver(delivery({ status: 'Assigned' }), api)
    await store.put('command', { id: 'k1', type: 'start', deliveryId: 'd1', payload: {}, createdAt: '2026-10-05T08:00:00Z', status: 'Conflict', attempts: 1, error: 'Already started on another device', errorCode: 'deliveries.invalid_state', podId: null })

    expect(await screen.findByText(/Already started on another device/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    await waitFor(async () => expect((await store.list<QueuedCommand>('command'))[0]!.status).toBe('Pending'))
  })

  it('lists the deliveries on the phone with their sync state', async () => {
    const { store } = renderDriver(delivery(), offlineApi(), '/driver')
    await store.put('command', { id: 'k2', type: 'start', deliveryId: 'd1', payload: {}, createdAt: '2026-10-05T08:00:00Z', status: 'Pending', attempts: 0, error: null, errorCode: null, podId: null })

    expect(await screen.findByText('DLV-10025')).toBeInTheDocument()
    expect(await screen.findByText('⟳ Pending sync')).toBeInTheDocument()
    expect(screen.getByText('En route')).toBeInTheDocument() // the driver's own unsent start already shows
  })
})
