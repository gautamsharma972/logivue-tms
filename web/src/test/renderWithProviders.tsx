import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { App as AntApp } from 'antd'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { AppThemeProvider } from '@/app/theme'
import { AuthProvider } from '@/features/auth/AuthContext'

export function renderWithProviders(ui: ReactElement, { route = '/' } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <AppThemeProvider>
        <AntApp>
          <MemoryRouter initialEntries={[route]}>
            <AuthProvider>{ui}</AuthProvider>
          </MemoryRouter>
        </AntApp>
      </AppThemeProvider>
    </QueryClientProvider>,
  )
}
