import { render, screen } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { captureEntryHints } from "@authsystem/i18n/flow-hints"
import { ThemeProvider, useTheme } from "@authsystem/ui/theme-provider"

const { auth } = vi.hoisted(() => ({
  auth: { status: "authenticated" as string, user: { theme: "dark" } as { theme?: string } | null },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => auth,
}))

import { ThemeSync } from "./theme-sync"

function ThemeProbe() {
  const { theme } = useTheme()
  return <output aria-label="theme">{theme}</output>
}

function renderSynced() {
  render(
    <ThemeProvider defaultTheme="system" disableTransitionOnChange={false}>
      <ThemeSync />
      <ThemeProbe />
    </ThemeProvider>
  )
}

/**
 * The profile's theme is adopted once a session signs in, except while an
 * application's colour mode is in force: then the pages keep the application's,
 * and the profile is not written onto the browser.
 */
describe("ThemeSync", () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    window.history.replaceState(null, "", "/")
    auth.status = "authenticated"
    auth.user = { theme: "dark" }
  })

  afterEach(() => {
    sessionStorage.clear()
    window.history.replaceState(null, "", "/")
  })

  it("adopts the profile's theme when no application asked for one", () => {
    renderSynced()

    expect(screen.getByLabelText("theme")).toHaveTextContent("dark")
    expect(localStorage.getItem("theme")).toBe("dark")
  })

  it("keeps the application's colour mode, and writes nothing", () => {
    window.history.replaceState(null, "", "/login?ui_theme=light")
    captureEntryHints(null)

    renderSynced()

    expect(screen.getByLabelText("theme")).toHaveTextContent("light")
    expect(localStorage.getItem("theme")).toBeNull()
  })

  it("adopts nothing before sign-in", () => {
    auth.status = "unauthenticated"
    auth.user = null

    renderSynced()

    expect(screen.getByLabelText("theme")).toHaveTextContent("system")
  })
})
