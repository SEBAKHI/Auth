import { act, renderHook } from "@testing-library/react"
import type { ReactNode } from "react"
import { afterEach, beforeEach, describe, expect, it } from "vitest"

import { ThemeProvider } from "@authsystem/ui/theme-provider"

import {
  PREVIEW_SCHEME_STORAGE_KEY,
  usePreviewScheme,
} from "./use-preview-scheme"

function withTheme(theme: "light" | "dark") {
  localStorage.setItem("theme", theme)
  return ({ children }: { children: ReactNode }) => (
    <ThemeProvider>{children}</ThemeProvider>
  )
}

describe("usePreviewScheme", () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => localStorage.clear())

  it.each(["light", "dark"] as const)(
    "opens matching a %s console the first time, before anyone has chosen",
    (theme) => {
      const { result } = renderHook(() => usePreviewScheme(), {
        wrapper: withTheme(theme),
      })

      expect(result.current[0]).toBe(theme)
    }
  )

  it("remembers an explicit choice instead of the console theme", () => {
    const { result, unmount } = renderHook(() => usePreviewScheme(), {
      wrapper: withTheme("dark"),
    })

    act(() => result.current[1]("light"))
    expect(result.current[0]).toBe("light")
    unmount()

    const reopened = renderHook(() => usePreviewScheme(), {
      wrapper: withTheme("dark"),
    })
    expect(reopened.result.current[0]).toBe("light")
  })

  it("does not follow the console theme once a choice exists", () => {
    localStorage.setItem(PREVIEW_SCHEME_STORAGE_KEY, "light")

    const { result, rerender } = renderHook(() => usePreviewScheme(), {
      wrapper: withTheme("dark"),
    })

    // A re-render is where a value bound to the theme would snap back.
    rerender()
    expect(result.current[0]).toBe("light")
  })

  it("ignores a stored value that is not a scheme", () => {
    localStorage.setItem(PREVIEW_SCHEME_STORAGE_KEY, "sepia")

    const { result } = renderHook(() => usePreviewScheme(), {
      wrapper: withTheme("dark"),
    })

    expect(result.current[0]).toBe("dark")
  })
})
