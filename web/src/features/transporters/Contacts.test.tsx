import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { performanceApi } from '@/lib/api/endpoints'
import { renderWithProviders } from '@/test/renderWithProviders'
import { ContactsPanel } from './ContactsPanel'

const auth = vi.hoisted(() => ({ permissions: new Set<string>(['transporters.manage']), transporterId: null as string | null }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: { id: 'u1', transporterId: auth.transporterId }, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  performanceApi: { contacts: vi.fn(), addContact: vi.fn(), updateContact: vi.fn(), branches: vi.fn(), addBranch: vi.fn(), updateBranch: vi.fn() },
}))

beforeEach(() => {
  vi.clearAllMocks()
  auth.permissions = new Set(['transporters.manage'])
  auth.transporterId = null
  vi.mocked(performanceApi.contacts).mockResolvedValue([
    { id: 'c1', transporterId: 't1', name: 'Anil Kumar', designation: 'Dispatch head', email: null, phone: '9876543210', contactType: 'Operations', isPrimary: true, isActive: true, version: 1 },
  ])
  vi.mocked(performanceApi.branches).mockResolvedValue([
    { id: 'b1', transporterId: 't1', code: 'PNQ-1', name: 'Pune hub', address: null, city: 'Pune', state: 'Maharashtra', latitude: null, longitude: null, contactName: null, contactPhone: null, isActive: true, version: 1 },
  ])
})

describe('ContactsPanel', () => {
  it('lists contacts and branches and lets staff add a contact', async () => {
    vi.mocked(performanceApi.addContact).mockResolvedValue({} as never)
    const user = userEvent.setup()
    renderWithProviders(<ContactsPanel transporterId="t1" />)

    expect(await screen.findByText('Anil Kumar')).toBeInTheDocument()
    expect(screen.getByText('Primary')).toBeInTheDocument()
    expect(screen.getByText('Pune hub')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Add contact/ }))
    await user.type(await screen.findByLabelText('Name'), 'Meena')
    await user.type(screen.getByLabelText('Email'), 'meena@x.example')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(performanceApi.addContact).toHaveBeenCalledWith('t1', expect.objectContaining({ name: 'Meena', email: 'meena@x.example', contactType: 'Operations', isPrimary: false })))
  })

  it('is read-only for someone who may not edit the transporter', async () => {
    auth.permissions = new Set(['transporters.read'])
    renderWithProviders(<ContactsPanel transporterId="t1" />)

    expect(await screen.findByText('Anil Kumar')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Add contact/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
  })

  it('lets a vendor edit its own company when it holds the self-service permission', async () => {
    auth.permissions = new Set(['transporters.self.manage'])
    auth.transporterId = 't1'
    renderWithProviders(<ContactsPanel transporterId="t1" />)

    expect(await screen.findByRole('button', { name: /Add branch/ })).toBeInTheDocument()
  })
})
