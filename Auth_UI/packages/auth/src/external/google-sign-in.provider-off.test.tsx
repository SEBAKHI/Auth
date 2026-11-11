import { act, fireEvent, render, screen, waitFor } from "@testing-library/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { webcrypto } from "node:crypto"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
  GOOGLE_CLIENT_ID: "",
  APPLE_SERVICES_ID: "",
}))

const { get } = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => get(...args), POST: vi.fn() },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ loginExternal: vi.fn() }),
}))

vi.mock("./request-nonce", () => ({
  requestExternalNonce: async () => "server-nonce",
}))

import { ExternalProviders } from "./external-providers"

/**
 * S02 PR D (review of #162): with the REAL providers hook. This browser saw Google offered
 * last time, so the button and its "or" divider draw with the page; the person clicks while the
 * live answer is still on its way; the answer says Google is off. The popup closes, nothing is
 * sent to Google, and the button and the divider leave with the answer.
 *
 * Deliberate break, verified red: the popup left open when the answer says Google is off.
 */

let releaseProviders!: (providers: unknown[]) => void

beforeEach(() => {
  vi.stubGlobal("crypto", webcrypto)
  // The providers hint (use-external-providers.ts); jsdom's storage is not relied on here.
  const storage = new Map([["auth.ui.externalProviders", JSON.stringify(["google"])]])
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => void storage.set(key, value),
  })
  get.mockReset().mockImplementation(
    () =>
      new Promise((resolve) => {
        releaseProviders = (providers) => resolve({ data: providers, response: new Response("[]") })
      })
  )
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe("Google switched off between two visits", () => {
  it("closes the popup and takes the button and its divider away when the live answer says so", async () => {
    const popup = { closed: false, close: vi.fn(), location: { replace: vi.fn() } }
    vi.spyOn(window, "open").mockImplementation(() => popup as unknown as Window)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={["/login"]}>
          <ExternalProviders />
        </MemoryRouter>
      </QueryClientProvider>
    )

    // From the hint, before the answer.
    fireEvent.click(screen.getByRole("button", { name: "Continue with Google" }))
    expect(screen.getByText("or")).toBeInTheDocument()

    await act(async () => releaseProviders([]))

    await waitFor(() => expect(screen.queryByRole("button", { name: "Continue with Google" })).toBeNull())
    expect(screen.queryByText("or")).toBeNull()
    // The click's own continuation, a microtask after the cache update that took the button away.
    await waitFor(() => expect(popup.close).toHaveBeenCalled())
    expect(popup.location.replace).not.toHaveBeenCalled()
    // One request for the providers: the click waited for the page's own, it did not ask again.
    expect(get).toHaveBeenCalledTimes(1)
  })
})
