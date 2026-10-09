import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { renderHook, waitFor } from "@testing-library/react"
import type * as React from "react"
import { afterEach, describe, expect, it, vi } from "vitest"

import { useAppBranding } from "./use-app-branding"

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
      })
    )
    expect(String(fetch.mock.calls[0][0])).toMatch(
      /\/api\/v1\/applications\/portal\/public-branding$/
    )
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
})
