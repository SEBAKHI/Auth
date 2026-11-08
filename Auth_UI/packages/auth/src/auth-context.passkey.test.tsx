import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render } from "@testing-library/react"
import * as React from "react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const { post, get } = vi.hoisted(() => ({ post: vi.fn(), get: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  SESSION_EXPIRED_EVENT: "auth:session-expired",
  completePendingLogout: async () => undefined,
  api: {
    POST: (...args: unknown[]) => post(...args),
    GET: (...args: unknown[]) => get(...args),
  },
}))

import { clearTokens, getAccessToken } from "@authsystem/api/token-store"

import { AuthProvider, useAuth } from "./auth-context"
import { clearPendingTwoFactorChallenge } from "./pending-challenge"
import type { AuthenticationResponseJSON } from "./passkey/webauthn"

/**
 * S20 PR C, IN 2: the two context methods a passkey page calls. Each sends the
 * browser's answer and adopts the session it returns like every other sign-in
 * (`adoptLoginResponse`), so the page leaves through the completion rule and
 * never through a refresh, which would renew the session that lacks the factor
 * (TS10). A 429 keeps the wait its header named (ARR-0668).
 */

const USER = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "admin@example.com",
  firstName: "Ada",
  lastName: "Admin",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

const CREDENTIAL = { id: "cred-1", rawId: "cred-1", type: "public-key" } as unknown as AuthenticationResponseJSON

function token() {
  const enc = (v: object) => Buffer.from(JSON.stringify(v)).toString("base64url")
  return `${enc({ alg: "none" })}.${enc({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`
}

type Auth = ReturnType<typeof useAuth>

function Capture({ onAuth }: { onAuth: (auth: Auth) => void }) {
  const auth = useAuth()
  React.useEffect(() => onAuth(auth), [auth, onAuth])
  return null
}

function renderProvider(): Auth {
  let captured: Auth | null = null
  render(
    <QueryClientProvider client={new QueryClient()}>
      <AuthProvider>
        <Capture
          onAuth={(auth) => {
            captured = auth
          }}
        />
      </AuthProvider>
    </QueryClientProvider>
  )
  expect(captured).not.toBeNull()
  return captured as unknown as Auth
}

function signedIn(accessToken: string) {
  return {
    data: { token: { accessToken, refreshToken: "R1" }, user: USER, requiresPasswordChange: false },
    error: undefined,
    response: new Response(null, { status: 200 }),
  }
}

describe("the passkey sign-in and recovery, on the wire (S20 PR C)", () => {
  beforeEach(() => {
    post.mockReset()
    get.mockReset()
    get.mockResolvedValue({ data: USER })
    clearTokens()
    clearPendingTwoFactorChallenge()
    window.localStorage.clear()
    window.sessionStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("loginWithPasskey sends the answer and the box, and adopts the session it returns", async () => {
    const accessToken = token()
    post.mockResolvedValue(signedIn(accessToken))
    const { loginWithPasskey } = renderProvider()

    let result: Awaited<ReturnType<Auth["loginWithPasskey"]>> | undefined
    await act(async () => {
      result = await loginWithPasskey(CREDENTIAL, true)
    })

    expect(post).toHaveBeenCalledWith("/api/v1/auth/passkeys/assertion/verify", {
      body: { credential: CREDENTIAL, deviceId: null, staySignedIn: true },
    })
    expect(result).toEqual({ status: "authenticated", requiresPasswordChange: false })
    expect(getAccessToken()).toBe(accessToken)
  })

  it("loginWithPasskey sends false for a box not ticked", async () => {
    post.mockResolvedValue(signedIn(token()))
    const { loginWithPasskey } = renderProvider()

    await act(() => loginWithPasskey(CREDENTIAL).then(() => undefined))

    expect(post.mock.calls[0][1]).toEqual({
      body: { credential: CREDENTIAL, deviceId: null, staySignedIn: false },
    })
  })

  it("recoverWithPasskey sends the answer to the recovery, and adopts the session it returns", async () => {
    const accessToken = token()
    post.mockResolvedValue(signedIn(accessToken))
    const { recoverWithPasskey } = renderProvider()

    await act(() => recoverWithPasskey(CREDENTIAL).then(() => undefined))

    expect(post).toHaveBeenCalledWith("/api/v1/auth/passkeys/assertion/recover", {
      body: { credential: CREDENTIAL, deviceId: null },
    })
    expect(getAccessToken()).toBe(accessToken)
  })

  it("keeps the wait a 429 named on the failure it throws, and adopts nothing", async () => {
    post.mockResolvedValue({
      data: undefined,
      error: { status: 429, code: "General.RateLimited" },
      response: new Response(null, { status: 429, headers: { "Retry-After": "42" } }),
    })
    const { loginWithPasskey } = renderProvider()

    let failure: unknown
    await act(async () => {
      failure = await loginWithPasskey(CREDENTIAL).catch((error: unknown) => error)
    })

    expect(failure).toMatchObject({ status: 429, retryAfterSeconds: 42 })
    expect(getAccessToken()).toBeNull()
  })
})
