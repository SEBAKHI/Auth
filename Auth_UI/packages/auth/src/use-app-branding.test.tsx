import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { renderHook, waitFor } from "@testing-library/react"
import type * as React from "react"
import { afterEach, describe, expect, it, vi } from "vitest"

import {
  safeHomeUrl,
  useAppBranding,
  useAppKnown,
  useAppLoginDesign,
  useAppTheme,
} from "./use-app-branding"

// One client per test: the apps share one across screens, which is the point,
// but a test that inherited another test's cached answer would prove nothing.
function withClient(client = new QueryClient()) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
}

const portal = () =>
  ({
    ok: true,
    json: () => Promise.resolve({ name: "Portal", logoUrl: "/logo.svg" }),
  }) as Response

describe("useAppBranding", () => {
  afterEach(() => vi.restoreAllMocks())

  it("returns null without a client and adopts a successful response", async () => {
    const fetch = vi.spyOn(globalThis, "fetch").mockResolvedValue(portal())
    const { result, rerender } = renderHook(
      ({ clientId }) => useAppBranding(clientId),
      {
        initialProps: { clientId: null as string | null },
        wrapper: withClient(),
      }
    )

    expect(result.current).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
    rerender({ clientId: "portal" })
    await waitFor(() =>
      expect(result.current).toEqual({
        name: "Portal",
        logoUrl: "/logo.svg",
        logoUrlDark: null,
        logoHeightPx: null,
        homeUrl: null,
        theme: null,
        loginDesign: null,
      })
    )
    expect(String(fetch.mock.calls[0][0])).toMatch(
      /\/api\/v1\/applications\/portal\/public-branding$/
    )
  })

  it("adopts the height chosen for the logo, which the contract may send as a string", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ name: "Portal", logoUrl: "/logo.svg", logoHeightPx: "96" }),
    } as Response)
    const { result } = renderHook(() => useAppBranding("portal"), { wrapper: withClient() })

    await waitFor(() => expect(result.current?.logoHeightPx).toBe(96))
  })

  it("does not expose an earlier client's branding during a switch", async () => {
    let resolveSecond: ((value: Response) => void) | undefined
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ name: "First" }),
      } as Response)
      .mockReturnValueOnce(
        new Promise<Response>((resolve) => {
          resolveSecond = resolve
        })
      )
    const { result, rerender } = renderHook(
      ({ clientId }) => useAppBranding(clientId),
      {
        initialProps: { clientId: "first" as string | null },
        wrapper: withClient(),
      }
    )
    await waitFor(() => expect(result.current?.name).toBe("First"))

    rerender({ clientId: "second" })
    expect(result.current).toBeNull()
    resolveSecond?.({
      ok: true,
      json: () => Promise.resolve({ name: "Second", logoUrl: null }),
    } as Response)
    await waitFor(() => expect(result.current?.name).toBe("Second"))
  })

  it("gives the next screen of the flow the answer on its first frame, without a second request", async () => {
    const fetch = vi.spyOn(globalThis, "fetch").mockResolvedValue(portal())
    const client = new QueryClient()
    const first = renderHook(() => useAppBranding("portal"), {
      wrapper: withClient(client),
    })
    await waitFor(() => expect(first.result.current?.name).toBe("Portal"))
    first.unmount()

    // The next step mounts fresh. Its first render already has the logo: no
    // frame of the platform mark between two screens of the same flow.
    const next = renderHook(() => useAppBranding("portal"), {
      wrapper: withClient(client),
    })
    expect(next.result.current).toEqual({
      name: "Portal",
      logoUrl: "/logo.svg",
      logoUrlDark: null,
      logoHeightPx: null,
      homeUrl: null,
      theme: null,
      loginDesign: null,
    })
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it("falls back to null for HTTP and transport failures, and asks again on the next screen", async () => {
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce({ ok: false, status: 503 } as Response)
    const client = new QueryClient()
    const http = renderHook(() => useAppBranding("portal"), {
      wrapper: withClient(client),
    })
    await waitFor(() =>
      expect(client.getQueryState(["public-branding", "portal"])?.status).toBe(
        "error"
      )
    )
    expect(http.result.current).toBeNull()
    http.unmount()

    // A failure is not kept as the answer: the next screen fetches again.
    fetch.mockResolvedValueOnce(portal())
    const retried = renderHook(() => useAppBranding("portal"), {
      wrapper: withClient(client),
    })
    await waitFor(() => expect(retried.result.current?.name).toBe("Portal"))
    expect(fetch).toHaveBeenCalledTimes(2)

    fetch.mockRejectedValueOnce(new Error("offline"))
    const transport = renderHook(() => useAppBranding("offline"), {
      wrapper: withClient(client),
    })
    await waitFor(() =>
      expect(client.getQueryState(["public-branding", "offline"])?.status).toBe(
        "error"
      )
    )
    expect(transport.result.current).toBeNull()
  })

  it("keeps an unknown or inactive application's 404 as the answer: no application, not asked again", async () => {
    const fetch = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue({ ok: false, status: 404 } as Response)
    const client = new QueryClient()
    const first = renderHook(() => useAppBranding("gone"), {
      wrapper: withClient(client),
    })
    await waitFor(() =>
      expect(client.getQueryState(["public-branding", "gone"])?.status).toBe("success")
    )
    expect(first.result.current).toBeNull()
    first.unmount()

    const next = renderHook(() => useAppBranding("gone"), {
      wrapper: withClient(client),
    })
    expect(next.result.current).toBeNull()
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})

/**
 * Whether the page's application exists for the sign-in pages: an unknown or
 * inactive one (404) names nothing, and its page is the platform's own; a
 * failure says neither. Break: answer false after a failure.
 */
describe("useAppKnown", () => {
  afterEach(() => vi.restoreAllMocks())

  it("is unknown while asked, then known", async () => {
    let resolve!: (value: Response) => void
    vi.spyOn(globalThis, "fetch").mockReturnValue(new Promise<Response>((r) => (resolve = r)))
    const { result } = renderHook(() => useAppKnown("portal"), { wrapper: withClient() })

    expect(result.current).toBeUndefined()
    resolve(portal())
    await waitFor(() => expect(result.current).toBe(true))
  })

  it("is not known for a 404, and without a client", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({ ok: false, status: 404 } as Response)
    const { result } = renderHook(() => useAppKnown("gone"), { wrapper: withClient() })

    await waitFor(() => expect(result.current).toBe(false))
    expect(renderHook(() => useAppKnown(null), { wrapper: withClient() }).result.current).toBe(false)
  })

  it("stays unknown after a failure, which says neither", async () => {
    const fetch = vi.spyOn(globalThis, "fetch").mockResolvedValue({ ok: false, status: 503 } as Response)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { result } = renderHook(() => useAppKnown("portal"), { wrapper: withClient(client) })

    await waitFor(() =>
      expect(client.getQueryState(["public-branding", "portal"])?.status).toBe("error")
    )
    expect(fetch).toHaveBeenCalled()
    expect(result.current).toBeUndefined()
  })
})

describe("safeHomeUrl (OI-145)", () => {
  it.each([
    ["https://crm.example.com/", "https://crm.example.com/"],
    ["https://crm.example.com/app?x=1", "https://crm.example.com/app?x=1"],
    ["http://localhost:5173/", "http://localhost:5173/"],
    ["http://127.0.0.1:8080/home", "http://127.0.0.1:8080/home"],
  ])("renders %s", (raw, expected) => {
    expect(safeHomeUrl(raw)).toBe(expected)
  })

  it.each([
    "http://crm.example.com/",
    "javascript:alert(1)",
    "data:text/html,x",
    "/relative",
    "crm.example.com",
    "",
    null,
    undefined,
  ])("withholds %s", (raw) => {
    expect(safeHomeUrl(raw)).toBeNull()
  })
})

describe("useAppTheme", () => {
  afterEach(() => vi.restoreAllMocks())

  const answer = (body: object) => ({ ok: true, json: () => Promise.resolve(body) }) as Response
  const violet = {
    base: { preset: "neutral" },
    theme: { preset: "violet" },
    chart: { preset: "cyan" },
    radius: "small",
    menuAccent: "bold",
    controlSize: { preset: "custom", px: 44 },
    tableRowHeight: { preset: "comfortable" },
  }

  it("is unknown while the branding is on its way, then the application's whole appearance", async () => {
    let resolve!: (value: Response) => void
    vi.spyOn(globalThis, "fetch").mockReturnValue(new Promise<Response>((r) => (resolve = r)))
    const { result } = renderHook(() => useAppTheme("crm"), { wrapper: withClient() })

    expect(result.current).toBeUndefined()
    resolve(answer({ name: "CRM", theme: violet }))

    await waitFor(() =>
      expect(result.current).toEqual({
        base: { preset: "neutral", light: null, dark: null },
        theme: { preset: "violet", light: null, dark: null },
        chart: { preset: "cyan", light: null, dark: null },
        radius: "small",
        menuAccent: "bold",
        controlSize: { preset: "custom", px: 44 },
        tableRowHeight: { preset: "comfortable" },
        // An application's appearance has no header to size: always the default.
        headerIconSize: "default",
      })
    )
  })

  it("is none when the server sends no appearance (switched off, or none saved)", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(answer({ name: "CRM", theme: null }))
    const { result } = renderHook(() => useAppTheme("crm"), { wrapper: withClient() })

    await waitFor(() => expect(result.current).toBeNull())
  })

  it("is none for an unknown or inactive application: its pages show the platform's", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({ ok: false, status: 404 } as Response)
    const { result } = renderHook(() => useAppTheme("gone"), { wrapper: withClient() })

    await waitFor(() => expect(result.current).toBeNull())
  })

  it("stays unknown when the branding fails: a failure says nothing of the appearance", async () => {
    const fetch = vi.spyOn(globalThis, "fetch").mockResolvedValue({ ok: false, status: 503 } as Response)
    const { result } = renderHook(() => useAppTheme("crm"), { wrapper: withClient() })

    await waitFor(() => expect(fetch).toHaveBeenCalled())
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(result.current).toBeUndefined()
  })

  it("is none without a client, and asks nothing", () => {
    const fetch = vi.spyOn(globalThis, "fetch")
    const { result } = renderHook(() => useAppTheme(null), { wrapper: withClient() })

    expect(result.current).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })
})

/**
 * The design of the application's sign-in pages (phase 6), from the same answer: unknown
 * while it is on its way, none for an answer without one or for a failure, so the panel
 * never shows a guess. Break: return null while pending.
 */
describe("useAppLoginDesign", () => {
  afterEach(() => vi.restoreAllMocks())

  it("is unknown while the branding is on its way, then the design it names", async () => {
    const design = { id: "d1", contentHash: "h1", showPlatformLogo: false }
    let resolve: ((value: Response) => void) | undefined
    vi.spyOn(globalThis, "fetch").mockReturnValue(
      new Promise<Response>((done) => {
        resolve = done
      })
    )
    const { result } = renderHook(() => useAppLoginDesign("portal"), { wrapper: withClient() })
    expect(result.current).toBeUndefined()

    resolve?.({ ok: true, json: () => Promise.resolve({ name: "Portal", loginDesign: design }) } as Response)

    await waitFor(() => expect(result.current).toEqual(design))
  })

  it("is none without a client, without a design, and after a failure", async () => {
    expect(renderHook(() => useAppLoginDesign(null), { wrapper: withClient() }).result.current).toBeNull()

    vi.spyOn(globalThis, "fetch").mockResolvedValueOnce(portal())
    const none = renderHook(() => useAppLoginDesign("portal"), { wrapper: withClient() })
    await waitFor(() => expect(none.result.current).toBeNull())

    vi.spyOn(globalThis, "fetch").mockResolvedValueOnce({ ok: false, status: 500 } as Response)
    const failed = renderHook(() => useAppLoginDesign("broken"), { wrapper: withClient(new QueryClient({ defaultOptions: { queries: { retry: false } } })) })
    await waitFor(() => expect(failed.result.current).toBeNull())
  })
})
