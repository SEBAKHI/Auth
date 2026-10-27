import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render } from "@testing-library/react"
import * as React from "react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { captureEntryHints } from "@authsystem/i18n/flow-hints"

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
import { getValidReturnTo } from "./return-to"
import { clearPendingTwoFactorChallenge } from "./pending-challenge"

/**
 * Signing in adopts the profile's language once, so a fresh browser follows
 * the account. While an application's language is in force for the flow
 * (ui_locales), its pages stay in that language and the profile's is not
 * written onto this browser: the hint is the application's, the profile the
 * user's, and neither overwrites the other.
 */

const USER = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "jane@one.example",
  firstName: "Jane",
  lastName: "Doe",
  preferredLanguage: "fr",
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

describe("the profile's language at sign-in, against an application's (ui_locales)", () => {
  beforeEach(async () => {
    post.mockReset()
    get.mockReset()
    get.mockResolvedValue({ data: USER })
    post.mockResolvedValue(SIGNED_IN)
    clearTokens()
    clearPendingTwoFactorChallenge()
    window.localStorage.clear()
    window.sessionStorage.clear()
    window.history.replaceState(null, "", "/")
    await i18n.changeLanguage("en")
  })

  afterEach(() => {
    window.sessionStorage.clear()
    window.history.replaceState(null, "", "/")
    vi.restoreAllMocks()
  })

  it("adopts and stores the profile's language when no application asked for one", async () => {
    const { login } = renderProvider()

    await act(() => login("jane@one.example", "pw").then(() => undefined))

    expect(window.localStorage.getItem("auth.language")).toBe("fr")
    // The switch fetches the bundle first; let it land here, not in the next case.
    await vi.waitFor(() => expect(i18n.language).toBe("fr"))
  })

  it("keeps the application's language and stores nothing while its flow is in force", async () => {
    window.history.replaceState(null, "", `/login?client_id=CRM&auth_request=${"a".repeat(64)}&ui_locales=ar`)
    captureEntryHints({
      pendingRequest: getValidReturnTo(window.location.search),
      clientId: "CRM",
      continuation: "never",
    })
    await i18n.changeLanguage("ar")
    const { login } = renderProvider()

    await act(() => login("jane@one.example", "pw").then(() => undefined))

    expect(i18n.language).toBe("ar")
    expect(window.localStorage.getItem("auth.language")).toBeNull()
  })
})
