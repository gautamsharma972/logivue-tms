import { screen } from '@testing-library/react'
import { Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { renderWithProviders } from '@/test/renderWithProviders'
import { RequireAuth, RequirePermission } from './guards'

vi.mock('@/lib/api/endpoints', () => ({ authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() } }))

describe('route guards', () => {
  it('redirects anonymous visitors to the login page', () => {
    renderWithProviders(
      <Routes>
        <Route path="/login" element={<div>login screen</div>} />
        <Route element={<RequireAuth />}>
          <Route path="/secret" element={<div>secret</div>} />
        </Route>
      </Routes>,
      { route: '/secret' },
    )

    expect(screen.getByText('login screen')).toBeInTheDocument()
    expect(screen.queryByText('secret')).not.toBeInTheDocument()
  })

  it('shows access denied when the permission is missing', () => {
    renderWithProviders(
      <Routes>
        <Route element={<RequirePermission permission="users.read" />}>
          <Route path="/users" element={<div>users page</div>} />
        </Route>
      </Routes>,
      { route: '/users' },
    )

    expect(screen.getByText('Access denied')).toBeInTheDocument()
    expect(screen.queryByText('users page')).not.toBeInTheDocument()
  })
})
