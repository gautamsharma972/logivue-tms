import type { AuthResponse } from './types'

/** Non-sensitive hint that this browser had a session, so we only try a silent refresh when it is worth it. */
const HINT_KEY = 'tms.session'

let accessToken: string | null = null
let onExpired: (() => void) | null = null

function readHint(): boolean {
  try {
    return localStorage.getItem(HINT_KEY) === '1'
  } catch {
    return false
  }
}

/**
 * Credentials for the current browser session. The short-lived access token lives in memory only. The refresh token is
 * an HttpOnly cookie that scripts cannot read, so a stolen-by-XSS token is no longer possible; all we keep in storage
 * is a boolean hint.
 */
export const session = {
  getAccessToken: () => accessToken,
  hasSession: readHint,

  set(auth: Pick<AuthResponse, 'accessToken'>) {
    accessToken = auth.accessToken
    try {
      localStorage.setItem(HINT_KEY, '1')
    } catch {
      /* storage unavailable (private mode): the session just won't survive a reload */
    }
  },

  clear() {
    accessToken = null
    try {
      localStorage.removeItem(HINT_KEY)
    } catch {
      /* ignore */
    }
  },

  /** Registered by the auth provider; called when a refresh fails and the user must sign in again. */
  onExpired(handler: (() => void) | null) {
    onExpired = handler
  },

  notifyExpired() {
    onExpired?.()
  },
}
