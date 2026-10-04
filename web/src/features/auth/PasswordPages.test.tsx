import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { authApi } from '@/lib/api/endpoints'
import { ApiError } from '@/lib/api/errors'
import { renderWithProviders } from '@/test/renderWithProviders'
import { ForgotPasswordPage, ResetPasswordPage } from './PasswordPages'

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn(), forgotPassword: vi.fn(), resetPassword: vi.fn(), changePassword: vi.fn() },
}))

function renderReset(route: string) {
  return renderWithProviders(
    <Routes>
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/forgot-password" element={<div>forgot page</div>} />
    </Routes>,
    { route },
  )
}

describe('password pages', () => {
  beforeEach(() => vi.clearAllMocks())

  it('forgot-password gives the same confirmation whatever the server does with the address', async () => {
    vi.mocked(authApi.forgotPassword).mockResolvedValue({} as never)
    const user = userEvent.setup()
    renderWithProviders(<ForgotPasswordPage />)

    await user.type(screen.getByLabelText('Organisation code'), 'demo')
    await user.type(screen.getByLabelText('Email'), 'someone@example.com')
    await user.click(screen.getByRole('button', { name: 'Send reset link' }))

    expect(await screen.findByText(/If that account exists/)).toBeInTheDocument()
    expect(authApi.forgotPassword).toHaveBeenCalledWith({ tenantCode: 'demo', email: 'someone@example.com' })
  })

  it('reset page without a token explains and offers a new link', () => {
    renderReset('/reset-password')

    expect(screen.getByText('This link is incomplete')).toBeInTheDocument()
    expect(authApi.resetPassword).not.toHaveBeenCalled()
  })

  it('will not submit weak or mismatched passwords', async () => {
    const user = userEvent.setup()
    renderReset('/reset-password?token=abc')

    await user.type(screen.getByLabelText('New password'), 'short')
    await user.type(screen.getByLabelText('Confirm new password'), 'different')
    await user.click(screen.getByRole('button', { name: 'Set password' }))

    expect(await screen.findByText('At least 12 characters')).toBeInTheDocument()
    expect(authApi.resetPassword).not.toHaveBeenCalled()
  })

  it('sends the token and new password, then confirms', async () => {
    vi.mocked(authApi.resetPassword).mockResolvedValue({} as never)
    const user = userEvent.setup()
    renderReset('/reset-password?token=abc123')

    await user.type(screen.getByLabelText('New password'), 'Str0ng#Passw0rd!')
    await user.type(screen.getByLabelText('Confirm new password'), 'Str0ng#Passw0rd!')
    await user.click(screen.getByRole('button', { name: 'Set password' }))

    expect(await screen.findByText('You can now sign in')).toBeInTheDocument()
    expect(authApi.resetPassword).toHaveBeenCalledWith({ token: 'abc123', newPassword: 'Str0ng#Passw0rd!' })
  })

  it('shows an expired or used link as an error with a way to get a new one', async () => {
    vi.mocked(authApi.resetPassword).mockRejectedValue(new ApiError('This link is invalid or has expired. Request a new one.', 400, 'auth.reset_token_invalid'))
    const user = userEvent.setup()
    renderReset('/reset-password?token=old')

    await user.type(screen.getByLabelText('New password'), 'Str0ng#Passw0rd!')
    await user.type(screen.getByLabelText('Confirm new password'), 'Str0ng#Passw0rd!')
    await user.click(screen.getByRole('button', { name: 'Set password' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('invalid or has expired')
    await waitFor(() => expect(screen.getByText('New link')).toBeInTheDocument())
  })
})
