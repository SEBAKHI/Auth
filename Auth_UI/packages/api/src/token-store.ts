/**
 * Token storage.
 *
 * Security posture (the Auth API returns tokens in the response body, not in an
 * HttpOnly cookie, so the SPA must hold them):
 *   - Access token  -> kept in memory only. It is short-lived (~15 min) and is
 *     never persisted, so it cannot be stolen from disk/storage between sessions.
 *   - Refresh token -> persisted in localStorage so a page reload can silently
 *     re-establish a session. This is the standard trade-off for a token-based
 *     SPA; it is mitigated by a strict CSP and never rendering untrusted HTML.
 */

const REFRESH_TOKEN_KEY = "auth.refreshToken"

let accessToken: string | null = null

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null): void {
  accessToken = token
}

export function getRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_TOKEN_KEY)
  } catch {
    return null
  }
}

export function setTokens(access: string, refresh: string): void {
  accessToken = access
  try {
    localStorage.setItem(REFRESH_TOKEN_KEY, refresh)
  } catch {
    /* storage unavailable (private mode) — session stays in-memory only */
  }
}

export function clearTokens(): void {
  accessToken = null
  try {
    localStorage.removeItem(REFRESH_TOKEN_KEY)
  } catch {
    /* ignore */
  }
}

export function hasRefreshToken(): boolean {
  return getRefreshToken() !== null
}
