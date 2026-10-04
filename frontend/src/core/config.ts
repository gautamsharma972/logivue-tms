/**
 * Session token for calls to the API. It lives in sessionStorage, so it is per browser tab and cleared when the tab closes.
 * In production the identity provider supplies the token; the session box in the layout is for development and testing.
 */
const TOKEN_KEY = 'logivue.tms.token';

export function getToken(): string | null {
  try {
    return sessionStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

export function setToken(token: string | null): void {
  try {
    if (token) {
      sessionStorage.setItem(TOKEN_KEY, token);
    } else {
      sessionStorage.removeItem(TOKEN_KEY);
    }
  } catch {
    // Storage can be unavailable (private windows, blocked site data). The token then lasts only for this page load.
  }
}
