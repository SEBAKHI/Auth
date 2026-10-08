import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * The switch turned on while an administrator has the console open (S08): the
 * next background refresh mints a token without the platform authority, and the
 * user info this tab read at sign-in still says nothing is owed. The first
 * refusal with `TwoFactor.RequiredByPolicy` makes the provider read the account
 * again, so the requirement reaches RequireMfaSatisfied instead of every page
 * failing with a bare 403 until a reload.
 *
 * The real provider over the real API client, with the network stubbed: the
 * signal travels from the client's middleware to the provider, and a mock of
 * either would hide whether it arrives. Loaded fresh per test, because the
 * client starts tab sync at import.
 */

const SENTINEL = "__cookie__"

function accessToken(): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 900 })).replace(/=+$/, "")
  return `h.${payload}.s`
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } })
}

function urlOf(input: unknown): string {
  return typeof input === "string" ? input : String((input as { url?: string }).url)
}

const USER = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "admin@one.example",
  firstName: "Ada",
  lastName: "Admin",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

/** What the server answers: /me echoes the token in hand; /users is refused with `refusal`. */
interface Server {
  requirement: string
  refusal: string
  meCalls: number
}

function installServer(server: Server) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: unknown) => {
      const url = urlOf(input)
      if (url.includes("/Auth/refresh")) return json(200, { accessToken: accessToken(), refreshToken: SENTINEL })
      if (url.includes("/Auth/me")) {
        server.meCalls += 1
        return json(200, { ...USER, mfaRequirement: server.requirement })
      }
      if (url.includes("/users")) return json(403, { status: 403, code: server.refusal })
      return json(200, {})
    })
  )
}

async function renderProvider() {
  vi.resetModules()
  await import("@authsystem/i18n")
  const { AuthProvider, useAuth } = await import("./auth-context")
  const { api } = await import("@authsystem/api/client")

  function Probe() {
    const auth = useAuth()
    return (
      <span data-testid="probe">
        {auth.status}:{auth.mfaRequirement}
      </span>
    )
  }

  render(
    <QueryClientProvider client={new QueryClient()}>
      <AuthProvider>
        <Probe />
      </AuthProvider>
    </QueryClientProvider>
  )
  await waitFor(() => expect(screen.getByTestId("probe")).toHaveTextContent("authenticated:none"))

  return { readUsers: () => api.GET("/api/v1/users" as never, {} as never) }
}

beforeEach(() => {
  window.localStorage.clear()
  window.localStorage.setItem("auth.refreshToken", SENTINEL)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetModules()
})

describe("a page refused for the platform authority withheld mid-session", () => {
  it("reads the account again — once for a burst — and the requirement shows", async () => {
    const server: Server = { requirement: "none", refusal: "TwoFactor.RequiredByPolicy", meCalls: 0 }
    installServer(server)
    const { readUsers } = await renderProvider()
    expect(server.meCalls).toBe(1)

    // The background refresh withheld the authority: the token in hand now says so.
    server.requirement = "step_up"
    const failures = await Promise.all([readUsers(), readUsers(), readUsers()])

    expect(failures.map((failure) => failure.response.status)).toEqual([403, 403, 403])
    await waitFor(() => expect(screen.getByTestId("probe")).toHaveTextContent("authenticated:step_up"))
    expect(server.meCalls).toBe(2)
  })

  it("reads nothing once the requirement is known", async () => {
    const server: Server = { requirement: "none", refusal: "TwoFactor.RequiredByPolicy", meCalls: 0 }
    installServer(server)
    const { readUsers } = await renderProvider()
    server.requirement = "step_up"
    await readUsers()
    await waitFor(() => expect(screen.getByTestId("probe")).toHaveTextContent("authenticated:step_up"))

    await readUsers()
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(server.meCalls).toBe(2)
  })

  it("reads nothing for an ordinary refusal", async () => {
    const server: Server = { requirement: "none", refusal: "Auth.ReauthenticationRequired", meCalls: 0 }
    installServer(server)
    const { readUsers } = await renderProvider()

    await readUsers()
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(server.meCalls).toBe(1)
    expect(screen.getByTestId("probe")).toHaveTextContent("authenticated:none")
  })
})
