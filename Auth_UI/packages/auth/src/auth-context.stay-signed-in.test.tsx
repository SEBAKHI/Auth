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

import { clearTokens } from "@authsystem/api/token-store"

import { AuthProvider, useAuth } from "./auth-context"
import { clearPendingTwoFactorChallenge } from "./pending-challenge"

/**
 * OI-154: what the real provider puts on the wire for "Stay signed in". The page
 * hands the box to `login` and to the provider buttons; the provider carries it
 * to the second factor in memory, paired with the challenge it opened, and
 * nowhere else.
 */

const USER = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "jane@one.example",
  firstName: "Jane",
  lastName: "Doe",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

function token() {
  const enc = (v: object) => Buffer.from(JSON.stringify(v)).toString("base64url")
  return `${enc({ alg: "none" })}.${enc({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`
}

const SIGNED_IN = {
  data: { token: { accessToken: token(), refreshToken: "R1" }, user: USER, requiresPasswordChange: false },
}
const CHALLENGED = { data: { requiresTwoFactor: true, twoFactorChallengeToken: "challenge-1" } }

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

/** The body of the POST to `path`, the last one made. */
function bodyOf(path: string): Record<string, unknown> {
  const call = [...post.mock.calls].reverse().find(([url]) => url === path)
  expect(call, `a POST to ${path}`).toBeDefined()
  return (call![1] as { body: Record<string, unknown> }).body
}

describe("Stay signed in, on the wire (OI-154)", () => {
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

  it("the password sign-in sends false by default, and true when ticked", async () => {
    post.mockResolvedValue(SIGNED_IN)
    const { login } = renderProvider()

    await act(() => login("jane@one.example", "pw").then(() => undefined))
    expect(bodyOf("/api/v1/Auth/login")).toEqual({ email: "jane@one.example", password: "pw", staySignedIn: false })

    await act(() => login("jane@one.example", "pw", true).then(() => undefined))
    expect(bodyOf("/api/v1/Auth/login").staySignedIn).toBe(true)
  })

  it("the provider sign-in carries the choice", async () => {
    post.mockResolvedValue(SIGNED_IN)
    const { loginExternal } = renderProvider()

    await act(() => loginExternal("google", "id-token", "nonce", { staySignedIn: true }).then(() => undefined))
    expect(bodyOf("/api/v1/Auth/external-login").staySignedIn).toBe(true)

    await act(() => loginExternal("google", "id-token", "nonce").then(() => undefined))
    expect(bodyOf("/api/v1/Auth/external-login").staySignedIn).toBe(false)
  })

  it.each([
    ["password", true],
    ["password", false],
    ["provider", true],
  ] as const)("the second factor after a %s sign-in sends the sign-in page's choice (%s)", async (kind, stay) => {
    post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/2fa/verify" ? SIGNED_IN : CHALLENGED
    )
    const { login, loginExternal, completeTwoFactor } = renderProvider()

    const result = await act(() =>
      kind === "password"
        ? login("jane@one.example", "pw", stay)
        : loginExternal("google", "id-token", "nonce", { staySignedIn: stay })
    )
    expect(result).toEqual({ status: "twoFactorRequired", challengeToken: "challenge-1" })

    await act(() => completeTwoFactor("challenge-1", "123456", false).then(() => undefined))
    expect(bodyOf("/api/v1/auth/2fa/verify").staySignedIn).toBe(stay)
  })

  it("a second-factor page loaded directly, or for another challenge, sends false", async () => {
    post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/2fa/verify" ? SIGNED_IN : CHALLENGED
    )
    const { login, completeTwoFactor } = renderProvider()

    // Nothing opened a challenge in this document: as after a reload.
    await act(() => completeTwoFactor("challenge-from-a-reload", "123456", false).then(() => undefined))
    expect(bodyOf("/api/v1/auth/2fa/verify").staySignedIn).toBe(false)

    // A ticked sign-in opened challenge-1; a different challenge does not inherit it.
    await act(() => login("jane@one.example", "pw", true).then(() => undefined))
    await act(() => completeTwoFactor("challenge-other", "123456", false).then(() => undefined))
    expect(bodyOf("/api/v1/auth/2fa/verify").staySignedIn).toBe(false)
  })

  it("the choice is never written to localStorage or sessionStorage", async () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem")
    const localSet = vi.spyOn(window.localStorage, "setItem")
    const sessionSet = vi.spyOn(window.sessionStorage, "setItem")
    post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/2fa/verify" ? SIGNED_IN : CHALLENGED
    )
    const { login, completeTwoFactor } = renderProvider()

    await act(() => login("jane@one.example", "pw", true).then(() => undefined))
    await act(() => completeTwoFactor("challenge-1", "123456", false).then(() => undefined))

    const written = [...setItem.mock.calls, ...localSet.mock.calls, ...sessionSet.mock.calls]
    expect(written.length, "the sign-in itself stores its session key").toBeGreaterThan(0)
    for (const [key, value] of written) {
      expect(`${key}=${value}`.toLowerCase()).not.toMatch(/stay|persist/)
    }
  })
})
