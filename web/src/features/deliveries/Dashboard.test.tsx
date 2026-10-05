import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { deliveriesApi } from '@/lib/api/endpoints'
import type { ProofComplianceDto, ComplianceMetricsDto, DashboardSummaryDto, DeliveryNotificationDto, ProofAgeingDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { DeliveryDashboardPage } from './DeliveryDashboardPage'
import { DeliveryReportsPage } from './DeliveryReportsPage'
import { NotificationBell } from './NotificationBell'
import { NotificationsPage } from './NotificationsPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: null, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  deliveriesApi: {
    dashboard: vi.fn(), ageing: vi.fn(), ageingItems: vi.fn(), compliance: vi.fn(), downloadReport: vi.fn(),
    notifications: vi.fn(), readNotification: vi.fn(), readAllNotifications: vi.fn(),
  },
}))

const summary = (over: Partial<DashboardSummaryDto> = {}): DashboardSummaryDto => ({
  from: '2026-09-05', to: '2026-10-05', deliveriesToday: 4, delivered: 9, partiallyDelivered: 1, failed: 2, refused: 1, closed: 3, podPending: 5, podInPreparation: 0, podSubmitted: 2,
  podUnderReview: 3, podRejected: 1, podResubmissionRequired: 0, podAccepted: 7, shortageCases: 2, damageCases: 1, openExceptions: 6, overdueExceptions: 1, openClaims: 2, unreadNotifications: 0, ...over,
})

const metrics = (over: Partial<ComplianceMetricsDto> = {}): ComplianceMetricsDto => ({
  delivered: 10, podSubmitted: 8, podPending: 2, podRejected: 1, podAccepted: 7, submissionCompliance: 0.8, acceptanceRate: 0.875, rejectionRate: 0.125, averageSubmissionHours: 6, averageReviewHours: null,
  averageResubmissionHours: null, onTimeRate: null, ...over,
})

const compliance = (): ProofComplianceDto => ({ from: '2026-09-05', to: '2026-10-05', groupBy: 'transporter', overall: metrics(), rows: [{ key: 't1', name: 'Shree Roadlines', metrics: metrics() }] })

const ageing = (): ProofAgeingDto => ({
  bucketLabels: ['0-1 days', '2-3 days', '4-7 days', '8-15 days', '16-30 days', '>30 days'],
  stages: [
    { stage: 'PendingSubmission', label: 'Waiting for a proof', count: 5, overdue: 2, targetHours: 24, buckets: [3, 0, 2, 0, 0, 0] },
    { stage: 'PendingReview', label: 'Waiting for review', count: 3, overdue: 0, targetHours: 4, buckets: [3, 0, 0, 0, 0, 0] },
    { stage: 'Rejected', label: 'Rejected', count: 0, overdue: 0, targetHours: 12, buckets: [0, 0, 0, 0, 0, 0] },
    { stage: 'ResubmissionRequired', label: 'Sent back', count: 0, overdue: 0, targetHours: 12, buckets: [0, 0, 0, 0, 0, 0] },
  ],
  bucketTotals: [6, 0, 2, 0, 0, 0],
  topTransporters: [{ name: 'Shree Roadlines', overdue: 2, total: 5 }],
  topCustomers: [], topLocations: [],
})

const notice = (over: Partial<DeliveryNotificationDto> = {}): DeliveryNotificationDto => ({
  id: 'n1', kind: 'PodOverdue', title: 'Proof overdue: DLV-10025', body: 'No proof 30 hours after delivery.', deliveryId: 'd1', podId: null, exceptionId: null, createdAt: '2026-10-05T10:00:00Z', read: false, ...over,
})

const paged = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 })

beforeEach(() => {
  vi.clearAllMocks()
  auth.permissions = new Set(['deliveries.read'])
})

describe('DeliveryDashboardPage', () => {
  it('shows the counts, the ageing stages and who is behind, and says "Not applicable" where nothing can be measured', async () => {
    vi.mocked(deliveriesApi.dashboard).mockResolvedValue(summary())
    vi.mocked(deliveriesApi.compliance).mockResolvedValue(compliance())
    vi.mocked(deliveriesApi.ageing).mockResolvedValue(ageing())

    renderWithProviders(<DeliveryDashboardPage />)

    expect(await screen.findByText('Open exceptions')).toBeInTheDocument()
    expect(screen.getByText('Waiting for a proof')).toBeInTheDocument()
    expect(screen.getByText('2 past the 24 h target')).toBeInTheDocument()
    expect(screen.getByText(/Shree Roadlines/)).toBeInTheDocument()
    expect(screen.getByText('80%')).toBeInTheDocument()
    expect(screen.getAllByText('Not applicable').length).toBeGreaterThan(0)
  })

  it('opens the deliveries behind an ageing stage', async () => {
    vi.mocked(deliveriesApi.dashboard).mockResolvedValue(summary())
    vi.mocked(deliveriesApi.compliance).mockResolvedValue(compliance())
    vi.mocked(deliveriesApi.ageing).mockResolvedValue(ageing())
    vi.mocked(deliveriesApi.ageingItems).mockResolvedValue(paged([
      { stage: 'PendingSubmission', deliveryId: 'd1', deliveryNumber: 'DLV-10025', podId: null, customerName: 'ABC Distributors', transporterReference: 'Shree Roadlines', destination: 'Surat', ageHours: 30, bucket: '2-3 days', bucketIndex: 1, overdue: true, targetHours: 24 },
    ]))

    renderWithProviders(<DeliveryDashboardPage />)
    await userEvent.click(await screen.findByLabelText('Waiting for a proof'))

    const drawer = await screen.findByRole('dialog')
    expect(await within(drawer).findByText('DLV-10025')).toBeInTheDocument()
    expect(within(drawer).getByText('Past 24 h')).toBeInTheDocument()
    expect(deliveriesApi.ageingItems).toHaveBeenCalledWith(expect.objectContaining({ stage: 'PendingSubmission' }))
  })
})

describe('DeliveryReportsPage', () => {
  it('downloads the chosen report in the chosen format and lists compliance by transporter', async () => {
    vi.mocked(deliveriesApi.compliance).mockResolvedValue(compliance())
    vi.mocked(deliveriesApi.downloadReport).mockResolvedValue(undefined as never)

    renderWithProviders(<DeliveryReportsPage />)
    expect(await screen.findByText('Shree Roadlines')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /Excel/ }))

    await waitFor(() => expect(deliveriesApi.downloadReport).toHaveBeenCalledWith('deliveries', 'xlsx', expect.any(Object)))
  })
})

describe('Notifications', () => {
  it('shows the unread count on the bell', async () => {
    vi.mocked(deliveriesApi.notifications).mockResolvedValue({ ...paged([notice()]), totalCount: 3 })

    renderWithProviders(<NotificationBell />)

    expect(await screen.findByText('3')).toBeInTheDocument()
  })

  it('lists notices and marks one as read', async () => {
    vi.mocked(deliveriesApi.notifications).mockResolvedValue(paged([notice()]))
    vi.mocked(deliveriesApi.readNotification).mockResolvedValue(undefined as never)

    renderWithProviders(<NotificationsPage />)
    expect(await screen.findByText('Proof overdue: DLV-10025')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Mark as read' }))

    await waitFor(() => expect(deliveriesApi.readNotification).toHaveBeenCalledWith('n1'))
  })
})
