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

const marker = (sid: string | null) => JSON.stringify({ at: Date.now(), sid })
const pathOf = (url: string) => new URL(url).pathname

/**
 * A server for the next-load cases: refresh by cookie, /me, and the cookie
 * sign-out, which ends the cookie only for the session it belongs to.
 */
function installServer(options: { cookieSession: string }) {
  const cookieSignOuts: unknown[] = []
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: unknown, init?: RequestInit) => {
      const url = urlOf(input)
      calls.push(url)
      if (url.includes("/Auth/logout/cookie")) {
        const body = JSON.parse(String(init?.body ?? "{}")) as { sessionId?: string }
        cookieSignOuts.push(body)
        return json(200, { ended: !body.sessionId || body.sessionId === options.cookieSession })
      }
      if (url.includes("/Auth/refresh")) return json(200, { accessToken: accessToken(), refreshToken: SENTINEL })
      if (url.includes("/Auth/me")) return json(200, USER)
      return json(200, {})
    })
  )
  return { cookieSignOuts }
}

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
})

describe("a sign-out refused with 401 (the bearer expired or was refused)", () => {
  it("ends the session with the cookie, and keeps the marker until that is settled", async () => {
    window.localStorage.setItem("auth.refreshToken", SENTINEL)
    let cookieAttempts = 0
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: unknown) => {
        const url = urlOf(input)
        calls.push(url)
        if (url.includes("/Auth/refresh")) return json(200, { accessToken: accessToken(), refreshToken: SENTINEL })
        if (url.includes("/Auth/logout/cookie")) {
          cookieAttempts += 1
          // The first cookie sign-out is lost on the network too.
          if (cookieAttempts === 1) throw new TypeError("Failed to fetch")
          return json(200, { ended: true })
        }
        if (url.includes("/Auth/logout")) return json(401, { code: "Http.Unauthenticated" })
        if (url.includes("/Auth/me")) return json(200, USER)
        return json(200, {})
      })
    )
    const { logout } = await renderProvider(() => undefined)
    // Signed in first, as a real sign-out is: the bootstrap has settled.
    await waitFor(() => expect(screen.getByTestId("status")).toHaveTextContent("authenticated"))

    await act(async () => {
      await logout()
    })

    expect(calls.map(pathOf)).toContain("/api/v1/Auth/logout/cookie")
    expect(window.localStorage.getItem(LOGOUT_PENDING_KEY)).not.toBeNull()

    // The next load settles it.
    await renderProvider(() => undefined)
    await waitFor(() => expect(window.localStorage.getItem(LOGOUT_PENDING_KEY)).toBeNull())
    expect(cookieAttempts).toBe(2)
  })
})

describe("the next load after an unfinished sign-out", () => {
  it("is finished first: the cookie sign-out, never a signed-in render, never the profile", async () => {
    // A tab closed mid sign-out: the session key AND the marker are still here.
    window.localStorage.setItem("auth.refreshToken", SENTINEL)
    window.localStorage.setItem(LOGOUT_PENDING_KEY, marker("S1"))
    const server = installServer({ cookieSession: "S1" })
    const statuses: string[] = []

    await renderProvider((status) => statuses.push(status))
    await waitFor(() => expect(statuses.at(-1)).toBe("unauthenticated"))

    expect(server.cookieSignOuts).toEqual([{ sessionId: "S1" }])
    expect(calls.map(pathOf)).not.toContain("/api/v1/Auth/me")
    expect(statuses).not.toContain("authenticated")
    expect(window.localStorage.getItem(LOGOUT_PENDING_KEY)).toBeNull()
    expect(window.localStorage.getItem("auth.refreshToken")).toBeNull()
  })

  it("resumes a newer sign-in whose cookie the server kept", async () => {
    window.localStorage.setItem("auth.refreshToken", SENTINEL)
    window.localStorage.setItem(LOGOUT_PENDING_KEY, marker("OLD"))
    const server = installServer({ cookieSession: "NEW" })
    const statuses: string[] = []

    await renderProvider((status) => statuses.push(status))
    await waitFor(() => expect(statuses.at(-1)).toBe("authenticated"))

    expect(server.cookieSignOuts).toEqual([{ sessionId: "OLD" }])
    expect(window.localStorage.getItem(LOGOUT_PENDING_KEY)).toBeNull()
  })
})
