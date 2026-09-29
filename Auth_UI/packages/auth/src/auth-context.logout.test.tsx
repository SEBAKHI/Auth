import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor } from "@testing-library/react"
import * as React from "react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * A sign-out that fails on the network (S01, target state 14). In cookie mode
 * clearing local state no longer ends the session — the HttpOnly cookie outlives
 * it — so the failure is remembered and the next load finishes the sign-out
 * before it can show anything signed in.
 *
 * The real provider over the real API client, with the network stubbed: the
 * retry lives in the client and runs from the provider, and a mock of either
 * would hide the ordering this test is about. Loaded fresh per test (no static
 * import of the token stack), because the client starts tab sync at import.
 */

const LOGOUT_PENDING_KEY = "auth.logoutPending"
const SENTINEL = "__cookie__"

function accessToken(): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 900 })).replace(/=+$/, "")
  return `h.${payload}.s`
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } })
}

let calls: string[]

function urlOf(input: unknown): string {
  return typeof input === "string" ? input : String((input as { url?: string }).url)
}

async function renderProvider(onStatus: (status: string) => void) {
  vi.resetModules()
  await import("@authsystem/i18n")
  const { AuthProvider, useAuth } = await import("./auth-context")

  let logout: (() => Promise<void>) | null = null
  function Probe() {
    const auth = useAuth()
    logout = auth.logout
    React.useEffect(() => {
      onStatus(auth.status)
    }, [auth.status])
    return <span data-testid="status">{auth.status}</span>
  }

  render(
    <QueryClientProvider client={new QueryClient()}>
      <AuthProvider>
        <Probe />
      </AuthProvider>
    </QueryClientProvider>
  )
  return { logout: () => (logout as unknown as () => Promise<void>)() }
}

beforeEach(() => {
  window.localStorage.clear()
  calls = []
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetModules()
})

describe("a sign-out that fails on the network", () => {
  it("leaves the logout-pending marker and still shows the user signed out", async () => {
    window.localStorage.setItem("auth.refreshToken", SENTINEL)
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: unknown) => {
        calls.push(urlOf(input))
        throw new TypeError("Failed to fetch")
      })
    )
    const { logout } = await renderProvider(() => undefined)

    await act(async () => {
      await logout()
    })

    expect(window.localStorage.getItem(LOGOUT_PENDING_KEY)).not.toBeNull()
    expect(window.localStorage.getItem("auth.refreshToken")).toBeNull()
    expect(screen.getByTestId("status")).toHaveTextContent("unauthenticated")
  })

  it("leaves no marker when the server answered", async () => {
    window.localStorage.setItem("auth.refreshToken", SENTINEL)
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: unknown) => {
        const url = urlOf(input)
        calls.push(url)
        if (url.includes("/Auth/refresh")) return json(200, { accessToken: accessToken(), refreshToken: SENTINEL })
        if (url.includes("/Auth/logout")) return new Response(null, { status: 204 })
        return json(200, {})
      })
    )
    const { logout } = await renderProvider(() => undefined)

    await act(async () => {
      await logout()
    })

    expect(calls.some((url) => url.includes("/Auth/logout"))).toBe(true)
    expect(window.localStorage.getItem(LOGOUT_PENDING_KEY)).toBeNull()
  })

  it("is finished on the next load: refresh, then sign-out, before anything is signed in", async () => {
    window.localStorage.setItem(LOGOUT_PENDING_KEY, String(Date.now()))
    const authorizations: (string | null)[] = []
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: unknown, init?: RequestInit) => {
        const url = urlOf(input)
        calls.push(url)
        if (url.includes("/Auth/refresh")) return json(200, { accessToken: accessToken(), refreshToken: SENTINEL })
        if (url.includes("/Auth/logout")) {
          authorizations.push(new Headers(init?.headers as HeadersInit).get("Authorization"))
          return new Response(null, { status: 204 })
        }
        return json(200, {})
      })
    )
    const statuses: string[] = []

    await renderProvider((status) => statuses.push(status))
    await waitFor(() => expect(window.localStorage.getItem(LOGOUT_PENDING_KEY)).toBeNull())

    expect(calls.map((url) => new URL(url).pathname)).toEqual(["/api/v1/Auth/refresh", "/api/v1/Auth/logout"])
    expect(authorizations).toEqual([`Bearer ${accessToken()}`])
    expect(statuses).not.toContain("authenticated")
    // The access token minted for the retry was never adopted.
    expect(window.localStorage.getItem("auth.refreshToken")).toBeNull()
  })

  it("drops the marker without signing out when the refresh is refused for good", async () => {
    window.localStorage.setItem(LOGOUT_PENDING_KEY, String(Date.now()))
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: unknown) => {
        calls.push(urlOf(input))
        return json(404, { code: "Auth.RefreshTokenNotFound" })
      })
    )

    await renderProvider(() => undefined)
    await waitFor(() => expect(window.localStorage.getItem(LOGOUT_PENDING_KEY)).toBeNull())

    expect(calls.map((url) => new URL(url).pathname)).toEqual(["/api/v1/Auth/refresh"])
  })
})
