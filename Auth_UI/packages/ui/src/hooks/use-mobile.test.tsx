import { act, renderHook } from "@testing-library/react"
import { afterEach, describe, expect, it, vi } from "vitest"

import { useIsMobile } from "./use-mobile"

describe("useIsMobile", () => {
  afterEach(() => vi.restoreAllMocks())

  it("reads the media-query snapshot and reacts to changes", () => {
    let matches = false
    let listener: (() => void) | undefined
    vi.spyOn(window, "matchMedia").mockImplementation((query) => ({
      media: query,
      get matches() {
        return matches
      },
      onchange: null,
      addEventListener: (
        _type: string,
        callback: EventListenerOrEventListenerObject
      ) => {
        listener =
          typeof callback === "function"
            ? () => callback(new Event("change"))
            : () => callback.handleEvent(new Event("change"))
      },
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: () => false,
    }))
    const { result } = renderHook(() => useIsMobile())
    expect(result.current).toBe(false)

    matches = true
    act(() => listener?.())
    expect(result.current).toBe(true)
  })
})
