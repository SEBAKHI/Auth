import createClient, { type Middleware } from "openapi-fetch"

import { API_BASE_URL } from "@/lib/env"
import { decodeJwt, isTokenExpired } from "@/lib/auth/jwt"
import {
  clearTokens,
  getAccessToken,
  getRefreshToken,
  setTokens,
} from "@/lib/auth/token-store"
import type { paths, Schemas } from "./types"

const REFRESH_PATH = "/api/v1/Auth/refresh"
const LOGIN_PATH = "/api/v1/Auth/login"

/** Event dispatched when the session can no longer be refreshed. */
export const SESSION_EXPIRED_EVENT = "auth:session-expired"

/** De-duplicates concurrent refreshes into a single in-flight request. */
let refreshPromise: Promise<boolean> | null = null

function emitSessionExpired(): void {
  clearTokens()
  window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT))
}

/** Exchange the refresh token for a new token pair. Returns success. */
export async function refreshAccessToken(): Promise<boolean> {
  const refreshToken = getRefreshToken()
  if (!refreshToken) return false

  try {
    const res = await fetch(`${API_BASE_URL}${REFRESH_PATH}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    })
    if (!res.ok) return false

    const data = (await res.json()) as Schemas["TokenResponse"]
    if (!data?.accessToken || !data?.refreshToken) return false

    setTokens(data.accessToken, data.refreshToken)
    return true
  } catch {
    return false
  }
}

function sharedRefresh(): Promise<boolean> {
  if (!refreshPromise) {
    refreshPromise = refreshAccessToken().finally(() => {
      refreshPromise = null
    })
  }
  return refreshPromise
}

function isAuthFlow(url: string): boolean {
  return url.includes(REFRESH_PATH) || url.includes(LOGIN_PATH)
}

const authMiddleware: Middleware = {
  async onRequest({ request }) {
    if (isAuthFlow(request.url)) return request

    let token = getAccessToken()
    const claims = token ? decodeJwt(token) : null

    // Proactively refresh an expired/missing access token so requests rarely 401.
    const needsRefresh = !token || (claims !== null && isTokenExpired(claims))
    if (needsRefresh && getRefreshToken()) {
      await sharedRefresh()
      token = getAccessToken()
    }

    if (token) {
      request.headers.set("Authorization", `Bearer ${token}`)
    }
    return request
  },

  async onResponse({ request, response }) {
    if (response.status !== 401 || isAuthFlow(request.url)) return response

    // Token was rejected (e.g. revoked). Try one refresh so a query retry
    // succeeds; if refresh is impossible, end the session.
    if (getRefreshToken()) {
      const ok = await sharedRefresh()
      if (!ok) emitSessionExpired()
    } else {
      emitSessionExpired()
    }
    return response
  },
}

/** The single, fully typed API client used across the app. */
export const api = createClient<paths>({ baseUrl: API_BASE_URL })
api.use(authMiddleware)
