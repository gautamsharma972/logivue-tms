import type { AuthResponse } from './types'

/** Non-sensitive hint that this browser had a session, so we only try a silent refresh when it is worth it. */
const HINT_KEY = 'tms.session'

let accessToken: string | null = null
/** Why a session ended: it ran out, or the browser's sign-in now belongs to a different user (another tab signed in). */
export type ExpiryReason = 'expired' | 'other-user'

let userId: string | null = null
let onExpired: ((reason: ExpiryReason) => void) | null = null

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
  /** The user this tab signed in as; a refresh must never hand the tab to anyone else. */
  getUserId: () => userId,
  hasSession: readHint,

  set(auth: Pick<AuthResponse, 'accessToken'> & Partial<Pick<AuthResponse, 'user'>>) {
    accessToken = auth.accessToken
    if (auth.user) userId = auth.user.id
    try {
      localStorage.setItem(HINT_KEY, '1')
    } catch {
      /* storage unavailable (private mode): the session just won't survive a reload */
    }
  },

  clear() {
    accessToken = null
    userId = null
    try {
      localStorage.removeItem(HINT_KEY)
    } catch {
      /* ignore */
    }
  },

  /** Registered by the auth provider; called when a refresh fails and the user must sign in again. */
  onExpired(handler: ((reason: ExpiryReason) => void) | null) {
    onExpired = handler
  },

  notifyExpired(reason: ExpiryReason = 'expired') {
    onExpired?.(reason)
  },
}
