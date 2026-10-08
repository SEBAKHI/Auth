import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * AM-S08-1: a sign-in completed with a recovery code leaves one notice for the
 * security page — keyed by the account that signed in — and a sign-in with an
 * authenticator code leaves none. The real provider over the real API client,
 * with the network stubbed; loaded fresh per test, because the client starts tab
 * sync at import.
 */

const USER = {
  id: "22222222-2222-2222-2222-222222222222",
  email: "owner@one.example",
  firstName: "Owen",
  lastName: "Owner",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

function accessToken(): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 900 })).replace(/=+$/, "")
  return `h.${payload}.s`
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } })
}

async function verifyWith(useRecoveryCode: boolean) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: unknown) => {
      const url = typeof input === "string" ? input : String((input as { url?: string }).url)
      if (url.includes("/2fa/verify")) {
        return json(200, { token: { accessToken: accessToken(), refreshToken: "refresh-token" }, user: USER })
      }
      return json(200, {})
    })
  )

  vi.resetModules()
  await import("@authsystem/i18n")
  const { AuthProvider, useAuth } = await import("./auth-context")

  let complete: (() => Promise<unknown>) | undefined
  function Probe() {
    const auth = useAuth()
    complete = () => auth.completeTwoFactor("challenge", useRecoveryCode ? "ABCD-1234" : "123456", useRecoveryCode)
    return <span data-testid="probe">{auth.status}</span>
  }

  render(
    <QueryClientProvider client={new QueryClient()}>
      <AuthProvider>
        <Probe />
      </AuthProvider>
    </QueryClientProvider>
  )
  await waitFor(() => expect(screen.getByTestId("probe")).toHaveTextContent("unauthenticated"))
  await act(async () => {
    await complete!()
  })
  await waitFor(() => expect(screen.getByTestId("probe")).toHaveTextContent("authenticated"))
}

beforeEach(() => {
  window.localStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetModules()
})

describe("a sign-in completed with a second factor", () => {
  it("leaves the security page's notice when it spent a recovery code", async () => {
    await verifyWith(true)

    expect(window.localStorage.getItem(`auth.recoveryCodeSignIn:${USER.id}`)).toBe("1")
  })

  it("leaves nothing with an authenticator code", async () => {
    await verifyWith(false)

    expect(window.localStorage.getItem(`auth.recoveryCodeSignIn:${USER.id}`)).toBeNull()
  })
})
