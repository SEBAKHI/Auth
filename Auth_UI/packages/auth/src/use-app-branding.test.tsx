import { renderHook, waitFor } from "@testing-library/react"
import { afterEach, describe, expect, it, vi } from "vitest"

import { useAppBranding } from "./use-app-branding"

describe("useAppBranding", () => {
  afterEach(() => vi.restoreAllMocks())

  it("returns null without a client and adopts a successful response", async () => {
    const fetch = vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ name: "Portal", logoUrl: "/logo.svg" }),
    } as Response)
    const { result, rerender } = renderHook(
      ({ clientId }) => useAppBranding(clientId),
      { initialProps: { clientId: null as string | null } }
    )

    expect(result.current).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
    rerender({ clientId: "portal" })
    await waitFor(() =>
      expect(result.current).toEqual({ name: "Portal", logoUrl: "/logo.svg" })
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
      { initialProps: { clientId: "first" as string | null } }
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

  it("falls back to null for HTTP and transport failures", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValueOnce({ ok: false } as Response)
    const http = renderHook(() => useAppBranding("missing"))
    await waitFor(() => expect(http.result.current).toBeNull())
    http.unmount()

    vi.mocked(fetch).mockRejectedValueOnce(new Error("offline"))
    const transport = renderHook(() => useAppBranding("offline"))
    await waitFor(() => expect(transport.result.current).toBeNull())
  })
})
