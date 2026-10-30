import * as React from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, renderHook, waitFor } from "@testing-library/react"
import { beforeEach, describe, expect, it, vi } from "vitest"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
  GOOGLE_CLIENT_ID: "",
  APPLE_SERVICES_ID: "",
}))

let answer: (providers: { code: string; clientId: string }[]) => void = () => {}
vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: () =>
      new Promise((resolve) => {
        answer = (providers) => resolve({ data: providers, response: new Response(null, { status: 200 }) })
      }),
  },
}))

import { useExternalProviders } from "./use-external-providers"

/**
 * What lets Google's button start loading before the providers' answer: the codes of
 * this browser's last answer. They only ever start a load; the live answer alone
 * enables a button.
 *
 * Deliberate breaks: read the hint into `googleEnabled`; stop writing it from the
 * answer; keep trusting the hint after the answer says otherwise.
 */

const HINT_KEY = "auth.ui.externalProviders"
let store: Map<string, string>

beforeEach(() => {
  // jsdom's localStorage is shadowed by Node's own, which is undefined here.
  store = new Map()
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => void store.set(key, value),
    removeItem: (key: string) => void store.delete(key),
  })
})

function renderProviders() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return renderHook(() => useExternalProviders(), {
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  })
}

describe("useExternalProviders", () => {
  it("expects Google before the answer when this browser heard it last time, without enabling it", () => {
    store.set(HINT_KEY, JSON.stringify(["google"]))
    const { result } = renderProviders()

    expect(result.current.googleExpected).toBe(true)
    expect(result.current.googleEnabled).toBe(false)
  })

  it("expects nothing before the answer on a first visit", () => {
    const { result } = renderProviders()

    expect(result.current.googleExpected).toBe(false)
    expect(result.current.googleEnabled).toBe(false)
  })

  it("lets the answer decide, and keeps it for the next visit", async () => {
    store.set(HINT_KEY, JSON.stringify(["google"]))
    const { result } = renderProviders()

    await act(async () => answer([{ code: "Apple", clientId: "apple-id" }]))

    await waitFor(() => expect(result.current.googleExpected).toBe(false))
    expect(result.current.googleEnabled).toBe(false)
    expect(result.current.appleEnabled).toBe(true)
    expect(JSON.parse(store.get(HINT_KEY)!)).toEqual(["apple"])
  })

  it("enables Google from the answer, and remembers it", async () => {
    const { result } = renderProviders()

    await act(async () => answer([{ code: "Google", clientId: "google-id" }]))

    await waitFor(() => expect(result.current.googleEnabled).toBe(true))
    expect(result.current.googleExpected).toBe(true)
    expect(result.current.googleClientId).toBe("google-id")
    expect(JSON.parse(store.get(HINT_KEY)!)).toEqual(["google"])
  })

  it("ignores a stored value that is not a list of codes", () => {
    store.set(HINT_KEY, JSON.stringify({ google: true }))
    const { result } = renderProviders()

    expect(result.current.googleExpected).toBe(false)
  })
})
