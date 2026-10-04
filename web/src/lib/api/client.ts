import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { toApiError } from './errors'
import { session } from './session'
import type { AuthResponse } from './types'

const baseURL = import.meta.env.VITE_API_URL ?? ''

// X-TMS-Client is the CSRF defence for the cookie-authenticated refresh/logout endpoints (see the API's ClientHeaderFilter).
const clientHeaders = { Accept: 'application/json', 'X-TMS-Client': 'web' }

export const http = axios.create({ baseURL, headers: clientHeaders })

// A bare client for the refresh call itself, so it never recurses through the interceptors below.
const bare = axios.create({ baseURL, headers: clientHeaders })

let inflight: Promise<AuthResponse> | null = null

async function rotateRefreshToken(): Promise<AuthResponse> {
  // The refresh token is single-use and lives in an HttpOnly cookie the browser attaches itself. Serialising refreshes
  // across tabs means each one presents the cookie the previous one just received, instead of two tabs presenting the
  // same token (which the server treats as theft and answers by revoking the session).
  const run = async () => {
    const { data } = await bare.post<AuthResponse>('/api/v1/auth/refresh')
    session.set(data)
    return data
  }
  return navigator.locks ? navigator.locks.request('tms-refresh', run) : run()
}

/** Single-flight: concurrent 401s share one refresh. */
export function refreshSession(): Promise<AuthResponse> {
  inflight ??= rotateRefreshToken().finally(() => {
    inflight = null
  })
  return inflight
}

http.interceptors.request.use((config) => {
  const token = session.getAccessToken()
  if (token) config.headers.set('Authorization', `Bearer ${token}`)
  return config
})

type Retriable = InternalAxiosRequestConfig & { _retried?: boolean }

http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as Retriable | undefined
    const isAuthCall = original?.url?.includes('/auth/') ?? false

    if (error.response?.status === 401 && original && !original._retried && !isAuthCall && session.hasSession()) {
      original._retried = true
      try {
        await refreshSession()
        return http(original)
      } catch {
        session.clear()
        session.notifyExpired()
      }
    }
    throw toApiError(error)
  },
)
