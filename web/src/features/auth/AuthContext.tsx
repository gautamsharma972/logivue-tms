import { App } from 'antd'
import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { refreshSession } from '@/lib/api/client'
import { authApi } from '@/lib/api/endpoints'
import { session } from '@/lib/api/session'
import type { AuthResponse, UserProfile } from '@/lib/api/types'

type Status = 'loading' | 'authenticated' | 'anonymous'

interface AuthContextValue {
  status: Status
  user: UserProfile | null
  login: (credentials: { tenantCode: string; email: string; password: string }) => Promise<void>
  /** Adopt a freshly issued session (after a password change). */
  acceptAuth: (auth: AuthResponse) => void
  logout: () => Promise<void>
  can: (permission: string) => boolean
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const [state, setState] = useState<{ status: Status; user: UserProfile | null }>({
    status: session.hasSession() ? 'loading' : 'anonymous',
    user: null,
  })

  // Resume a previous session (page reload / new tab) from the persisted refresh token.
  useEffect(() => {
    if (!session.hasSession()) return
    let cancelled = false
    refreshSession()
      .then((auth) => !cancelled && setState({ status: 'authenticated', user: auth.user }))
      .catch(() => {
        session.clear()
        if (!cancelled) setState({ status: 'anonymous', user: null })
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    session.onExpired(() => {
      queryClient.clear()
      setState({ status: 'anonymous', user: null })
      void message.warning('Your session has expired. Please sign in again.')
    })
    return () => session.onExpired(null)
  }, [message, queryClient])

  const login = useCallback<AuthContextValue['login']>(async (credentials) => {
    const auth = await authApi.login(credentials)
    session.set(auth)
    setState({ status: 'authenticated', user: auth.user })
  }, [])

  const acceptAuth = useCallback((auth: AuthResponse) => {
    session.set(auth)
    setState({ status: 'authenticated', user: auth.user })
  }, [])

  const logout = useCallback(async () => {
    await authApi.logout().catch(() => undefined) // best effort: we sign out locally regardless
    session.clear()
    queryClient.clear()
    setState({ status: 'anonymous', user: null })
  }, [queryClient])

  const value = useMemo<AuthContextValue>(() => {
    const granted = new Set(state.user?.permissions ?? [])
    return { ...state, login, acceptAuth, logout, can: (permission) => granted.has(permission) }
  }, [state, login, acceptAuth, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

/** Hides UI the user is not permitted to use. The API enforces the same rule; this is purely UX. */
export function Can({ permission, children }: { permission: string; children: ReactNode }) {
  return useAuth().can(permission) ? <>{children}</> : null
}
