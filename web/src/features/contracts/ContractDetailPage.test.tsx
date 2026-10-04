import { screen } from '@testing-library/react'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { contractsApi } from '@/lib/api/endpoints'
import type { ContractDto, ContractStatus, UserProfile } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { ContractDetailPage } from './ContractDetailPage'

const auth = vi.hoisted(() => ({ user: null as unknown, permissions: new Set<string>() }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: auth.user, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
  Can: ({ permission, children }: { permission: string; children: React.ReactNode }) => (auth.permissions.has(permission) ? children : null),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  contractsApi: {
    get: vi.fn(),
    rates: vi.fn().mockResolvedValue([]),
    zones: vi.fn().mockResolvedValue([]),
    vehicleTypes: vi.fn().mockResolvedValue([]),
    documents: vi.fn().mockResolvedValue([]),
    dieselPrices: vi.fn().mockResolvedValue([]),
  },
  transportersApi: { lookup: vi.fn().mockResolvedValue([]) },
}))

function contract(status: ContractStatus, overrides: Partial<ContractDto> = {}, rateCount = 3): ContractDto {
  return {
    summary: {
      id: 'c1', number: 'CN-00007', revision: 1, reference: 'CN-00007', transporterId: 't1', transporterName: 'Shree Roadlines', type: 'Ftl',
      title: 'West–North lanes', status, effectiveFrom: '2026-04-01', effectiveTo: '2027-03-31', daysUntilExpiry: status === 'Active' ? 200 : null,
      rateCount, estimatedAnnualSpend: 5_000_000,
    },
    paymentTermsDays: 30,
    terms: { volumetricKgPerCbm: 250, detentionFreeHours: 24, detentionRatePerHour: 100, loadingCharge: 0, unloadingCharge: 0, multiDropChargePerPoint: 0, minChargePerConsignment: 0, notes: null },
    fuel: null, ownerUserId: null, ownerName: 'Priya', approvalRequestId: null, revisionOfId: null, terminationReason: null, ratesRevision: 1,
    activatedAt: null, createdAt: '2026-03-01T09:00:00Z', version: 2,
    missingForSubmission: rateCount === 0 ? ['At least one rate'] : [],
    ...overrides,
  }
}

function renderPage(c: ContractDto) {
  vi.mocked(contractsApi.get).mockResolvedValue(c)
  return renderWithProviders(<Routes><Route path="/contracts/:id" element={<ContractDetailPage />} /></Routes>, { route: '/contracts/c1' })
}

const user = (): UserProfile => ({ id: 'u1', email: 'a@b.c', fullName: 'Staff', type: 'Internal', transporterId: null, tenantCode: 'DEMO', tenantName: 'Demo', roles: [], permissions: [], mustChangePassword: false })

describe('ContractDetailPage', () => {
  beforeEach(() => {
    auth.user = user()
    auth.permissions = new Set(['contracts.read', 'contracts.manage'])
  })

  it('lets a manager submit a complete draft', async () => {
    renderPage(contract('Draft'))

    expect(await screen.findByRole('button', { name: 'Submit for approval' })).toBeEnabled()
    expect(screen.getByRole('button', { name: /Edit details/ })).toBeInTheDocument()
  })

  it('blocks submission and says why when there are no rates', async () => {
    renderPage(contract('Draft', {}, 0))

    expect(await screen.findByRole('button', { name: 'Submit for approval' })).toBeDisabled()
    expect(screen.getByText('Before this can be submitted')).toBeInTheDocument()
    expect(screen.getByText('At least one rate')).toBeInTheDocument()
  })

  it('treats an approved contract as read-only: revise and terminate, no edit', async () => {
    renderPage(contract('Active'))

    expect(await screen.findByRole('button', { name: /Revise/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Terminate' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Edit details/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit for approval' })).not.toBeInTheDocument()
  })

  it('locks a contract that is awaiting approval', async () => {
    renderPage(contract('PendingApproval'))

    expect(await screen.findByText('Awaiting approval')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Edit details/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Terminate' })).not.toBeInTheDocument()
  })

  it('shows read-only users the contract without any change actions', async () => {
    auth.permissions = new Set(['contracts.read'])
    renderPage(contract('Active'))

    expect(await screen.findByRole('heading', { name: 'CN-00007' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Revise/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Terminate' })).not.toBeInTheDocument()
  })

  it('warns when an active contract is close to its end date', async () => {
    renderPage(contract('Active', { summary: { ...contract('Active').summary, daysUntilExpiry: 12 } }))

    expect(await screen.findByText('ends in 12 days')).toBeInTheDocument()
  })

  it('explains a revision and links to what it replaces', async () => {
    renderPage(contract('Draft', { revisionOfId: 'c0' }))

    expect(await screen.findByText('This is a revision')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'the previous version' })).toHaveAttribute('href', '/contracts/c0')
  })
})
