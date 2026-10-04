import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router-dom'
import { authApi } from '@/lib/api/endpoints'
import { ApiError } from '@/lib/api/errors'
import { session } from '@/lib/api/session'
import { renderWithProviders } from '@/test/renderWithProviders'
import { LoginPage } from './LoginPage'

vi.mock('@/lib/api/endpoints', () => ({ authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() } }))

const authResponse = {
  accessToken: 'access',
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  user: {
    id: '1',
    email: 'admin@demo.tms',
    fullName: 'Demo Admin',
    type: 'Internal' as const,
    transporterId: null,
    tenantCode: 'DEMO',
    tenantName: 'Demo Logistics',
    roles: ['Administrator'],
    permissions: ['users.read'],
    mustChangePassword: false,
  },
}

function renderLogin() {
  return renderWithProviders(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<div>dashboard home</div>} />
    </Routes>,
    { route: '/login' },
  )
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>, password = 'Secret#1234') {
  await user.type(screen.getByLabelText('Organisation code'), 'demo')
  await user.type(screen.getByLabelText(/^email/i), 'admin@demo.tms')
  await user.type(screen.getByLabelText(/^password/i), password)
  await user.click(screen.getByRole('button', { name: /sign in/i }))
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    session.clear()
  })

  it('requires every field before calling the API', async () => {
    const user = userEvent.setup()
    renderLogin()

    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByText('Enter your organisation code')).toBeInTheDocument()
    expect(authApi.login).not.toHaveBeenCalled()
  })

  it('signs in, stores the session and navigates to the dashboard', async () => {
    vi.mocked(authApi.login).mockResolvedValue(authResponse)
    const user = userEvent.setup()
    renderLogin()

    await fillAndSubmit(user)

    expect(await screen.findByText('dashboard home')).toBeInTheDocument()
    expect(authApi.login).toHaveBeenCalledWith({ tenantCode: 'demo', email: 'admin@demo.tms', password: 'Secret#1234' })
    expect(session.getAccessToken()).toBe('access')
    expect(session.hasSession()).toBe(true) // only a non-sensitive hint is stored; the refresh token is an HttpOnly cookie
    expect(JSON.stringify({ ...localStorage })).not.toContain('refresh')
  })

  it('shows the server message when credentials are rejected and does not store a session', async () => {
    vi.mocked(authApi.login).mockRejectedValue(new ApiError('The tenant, email or password is incorrect.', 401, 'auth.invalid_credentials'))
    const user = userEvent.setup()
    renderLogin()

    await fillAndSubmit(user, 'wrong-password')

    expect(await screen.findByRole('alert')).toHaveTextContent('The tenant, email or password is incorrect.')
    await waitFor(() => expect(session.getAccessToken()).toBeNull())
  })
})
