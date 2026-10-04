import { screen } from '@testing-library/react'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { transportersApi } from '@/lib/api/endpoints'
import type { TransporterDto, UserProfile } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { TransporterDetailPage } from './TransporterDetailPage'

const auth = vi.hoisted(() => ({ user: null as unknown, permissions: new Set<string>() }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: auth.user, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
  Can: ({ permission, children }: { permission: string; children: React.ReactNode }) => (auth.permissions.has(permission) ? children : null),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  transportersApi: {
    get: vi.fn(),
    vehicles: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }),
    drivers: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }),
    documents: vi.fn().mockResolvedValue([]),
    vehicleTypes: vi.fn().mockResolvedValue([]),
  },
  approvalsApi: { requests: vi.fn() },
}))

const base: TransporterDto = {
  id: 't1', code: 'TR-00007', status: 'Draft', legalName: 'Shree Roadlines Pvt Ltd', tradeName: null, pan: 'AAPFU0939F', gstin: '27AAPFU0939F1ZV',
  contactPerson: 'R. Patil', phone: '9876543210', email: 'ops@shree.example',
  address: { line1: 'Plot 4', line2: null, city: 'Pune', state: 'Maharashtra', pincode: '411019' },
  serviceModes: ['Ftl'], bank: null, approvalRequestId: null, suspensionReason: null, activatedAt: null, createdAt: '2026-05-01T09:00:00Z', version: 0,
  missingForSubmission: ['Bank details', 'Cancelled cheque'],
}

function internalUser(): UserProfile {
  return { id: 'u1', email: 'a@b.c', fullName: 'Staff', type: 'Internal', transporterId: null, tenantCode: 'DEMO', tenantName: 'Demo', roles: [], permissions: [], mustChangePassword: false }
}

function renderPage(transporter: TransporterDto) {
  vi.mocked(transportersApi.get).mockResolvedValue(transporter)
  return renderWithProviders(
    <Routes><Route path="/transporters/:id" element={<TransporterDetailPage />} /></Routes>,
    { route: `/transporters/${transporter.id}` },
  )
}

describe('TransporterDetailPage', () => {
  beforeEach(() => {
    auth.user = internalUser()
    auth.permissions = new Set(['transporters.read', 'transporters.manage', 'transporters.bank.manage'])
  })

  it('lists what is missing and will not let an incomplete draft be submitted', async () => {
    renderPage(base)

    expect(await screen.findByText('Before this can be submitted for approval')).toBeInTheDocument()
    expect(screen.getByText('Cancelled cheque')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Submit for approval' })).toBeDisabled()
  })

  it('allows submission once everything is on file', async () => {
    renderPage({ ...base, missingForSubmission: [] })

    expect(await screen.findByRole('button', { name: 'Submit for approval' })).toBeEnabled()
  })

  it('locks a pending record: no edit, no submit', async () => {
    renderPage({ ...base, status: 'PendingApproval', missingForSubmission: [] })

    expect(await screen.findByText('Awaiting approval')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Edit/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit for approval' })).not.toBeInTheDocument()
  })

  it('shows a vendor-portal user their own record without staff controls or bank details', async () => {
    auth.user = { ...internalUser(), type: 'Transporter', transporterId: 't1' }
    auth.permissions = new Set(['transporters.self.manage'])
    renderPage({ ...base, status: 'Active', missingForSubmission: [], bank: { accountHolder: 'Shree', accountNumberMasked: '••••9012', ifsc: 'HDFC0001234', bankName: 'HDFC' } })

    expect(await screen.findByRole('heading', { name: 'Shree Roadlines Pvt Ltd' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Edit/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Suspend' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit for approval' })).not.toBeInTheDocument()
    expect(screen.queryByText('Bank account')).not.toBeInTheDocument()
    expect(screen.queryByText('All transporters')).not.toBeInTheDocument()
  })

  it('lets staff suspend an active transporter, asking for a reason', async () => {
    renderPage({ ...base, status: 'Active', missingForSubmission: [] })

    expect(await screen.findByRole('button', { name: 'Suspend' })).toBeInTheDocument()
  })
})
