import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * The sign-in page's account list from the session's side (owner, 2026-11-22,
 * reversing D-215-4): the profile puts an entry right when the account's
 * address or name changed, and never adds one; a sign-out leaves the list,
 * which belongs to the browser.
 *
 * The real provider over the real API client, with the network stubbed, loaded
 * fresh per test as auth-context.logout.test.tsx does (the client starts tab
 * sync at import).
 */

const ACCOUNTS_KEY = "auth.signIn.accounts"
const SENTINEL = "__cookie__"
const ID = "11111111-1111-1111-1111-111111111111"

const PROFILE = {
  id: ID,
  email: "jane.new@one.example",
  firstName: "Jane",
  lastName: "Smith",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

function accessToken(): string {
  const payload = btoa(
    JSON.stringify({ sub: ID, exp: Math.floor(Date.now() / 1000) + 900 })
  ).replace(/=+$/, "")
  return `h.${payload}.s`
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  })
}

function urlOf(input: unknown): string {
  return typeof input === "string"
    ? input
    : String((input as { url?: string }).url)
}

function installServer() {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: unknown) => {
      const url = urlOf(input)
      if (url.includes("/Auth/refresh")) {
        return json(200, { accessToken: accessToken(), refreshToken: SENTINEL })
      }
      if (url.includes("/Auth/me")) return json(200, PROFILE)
      if (url.includes("/Auth/logout"))
        return new Response(null, { status: 204 })
      return json(200, {})
    })
  )
}

async function renderProvider() {
  vi.resetModules()
  await import("@authsystem/i18n")
  const { AuthProvider, useAuth } = await import("./auth-context")

  let logout: (() => Promise<void>) | null = null
  function Probe() {
    const auth = useAuth()
    logout = auth.logout
    return <span data-testid="status">{auth.status}</span>
  }

  render(
    <QueryClientProvider client={new QueryClient()}>
      <AuthProvider>
        <Probe />
      </AuthProvider>
    </QueryClientProvider>
  )
  await waitFor(() =>
    expect(screen.getByTestId("status")).toHaveTextContent(/^authenticated$/)
  )
  return { logout: () => (logout as unknown as () => Promise<void>)() }
}

function stored(): unknown {
  return JSON.parse(window.localStorage.getItem(ACCOUNTS_KEY) ?? "null")
}

beforeEach(() => {
  window.localStorage.clear()
  window.localStorage.setItem("auth.refreshToken", SENTINEL)
  installServer()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetModules()
})

describe("the account list, from the session", () => {
  it("takes the address and the name the profile has today, where the entry stands", async () => {
    window.localStorage.setItem(
      ACCOUNTS_KEY,
      JSON.stringify([
        { id: "other", email: "someone@example.com", name: null },
        { id: ID, email: "jane@one.example", name: "Jane Doe" },
      ])
    )

    await renderProvider()

    await waitFor(() =>
      expect(stored()).toEqual([
        { id: "other", email: "someone@example.com", name: null },
        { id: ID, email: "jane.new@one.example", name: "Jane Smith" },
      ])
    )
  })

  it("never adds the account: only a sign-in does", async () => {
    await renderProvider()

    expect(stored()).toBeNull()
  })

  it("is left by a sign-out", async () => {
    const list = [{ id: ID, email: "jane.new@one.example", name: "Jane Smith" }]
    window.localStorage.setItem(ACCOUNTS_KEY, JSON.stringify(list))
    const { logout } = await renderProvider()

    await act(async () => {
      await logout()
    })

    expect(screen.getByTestId("status")).toHaveTextContent("unauthenticated")
    expect(stored()).toEqual(list)
  })
})
