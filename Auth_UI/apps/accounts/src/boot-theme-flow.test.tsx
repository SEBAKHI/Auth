import { render } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { captureEntryHints, dropFlowHint } from "@authsystem/i18n/flow-hints"
import { bootTheme } from "@authsystem/ui/theme/boot-theme"
import { ThemeProvider } from "@authsystem/ui/theme-provider"

import { entryContextFor } from "./entry-hints"

/**
 * The boot script in <head> against the accounts app's own capture, page by
 * page, the way a tab meets them: the boot script runs first, then
 * `captureEntryHints(entryContextFor(location))`, then React's ThemeProvider.
 * On every page the boot script must paint the class ThemeProvider settles on,
 * or the page flashes between the two.
 *
 * The server appends the application's hints to every accounts page of a flow
 * (AuthorizeCommandHandler.AccountsRedirectAsync), so a later page of the flow
 * still names `ui_theme` after the user picked a mode of their own.
 */

const html = document.documentElement
const HANDLE = "a".repeat(64)
const OTHER = "b".repeat(64)

function arriveAt(path: string) {
  window.history.replaceState(null, "", path)
}

/** What the boot script paints, then what ThemeProvider settles on, for the current page. */
function arrive(path: string): { boot: string; provider: string } {
  arriveAt(path)
  html.classList.remove("light", "dark")
  bootTheme(window)
  const boot = html.classList.contains("dark") ? "dark" : "light"

  captureEntryHints(entryContextFor(window.location))
  html.classList.remove("light", "dark")
  const { unmount } = render(<ThemeProvider disableTransitionOnChange={false}>{null}</ThemeProvider>)
  const provider = html.classList.contains("dark") ? "dark" : "light"
  unmount()
  return { boot, provider }
}

describe("the boot script on the pages of a flow", () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    vi.spyOn(window, "matchMedia").mockImplementation(
      (query: string) =>
        ({ matches: false, media: query, addEventListener: () => {}, removeEventListener: () => {} }) as unknown as MediaQueryList
    )
  })

  afterEach(() => {
    vi.restoreAllMocks()
    html.classList.remove("light", "dark")
    arriveAt("/")
  })

  it("paints the user's choice, not the address, on a later page of the flow they chose it in", () => {
    // The application sends light; on the sign-in page the user picks dark.
    expect(arrive(`/login?client_id=CRM&auth_request=${HANDLE}&ui_locales=ar&ui_theme=light`)).toEqual({
      boot: "light",
      provider: "light",
    })
    dropFlowHint("theme")
    localStorage.setItem("theme", "dark")

    // The flow's next accounts page: the server names light again.
    expect(arrive(`/create-organization?client_id=CRM&auth_request=${HANDLE}&ui_locales=ar&ui_theme=light`)).toEqual({
      boot: "dark",
      provider: "dark",
    })
    // And a reload of it, which no longer names the hints.
    expect(arrive(`/create-organization?client_id=CRM&auth_request=${HANDLE}`)).toEqual({
      boot: "dark",
      provider: "dark",
    })
  })

  it("paints the address of another flow, which replaces the kept one", () => {
    arrive(`/login?client_id=CRM&auth_request=${HANDLE}&ui_theme=light`)
    dropFlowHint("theme")
    localStorage.setItem("theme", "dark")

    expect(arrive(`/login?client_id=SHOP&auth_request=${OTHER}&ui_theme=light`)).toEqual({
      boot: "light",
      provider: "light",
    })
  })

  it("matches the capture for an application code outside ASCII", () => {
    const client = encodeURIComponent("CRM-é")
    arrive(`/login?client_id=${client}&auth_request=${HANDLE}&ui_theme=light`)
    dropFlowHint("theme")
    localStorage.setItem("theme", "dark")

    expect(arrive(`/register?client_id=${client}&auth_request=${HANDLE}&ui_theme=light`)).toEqual({
      boot: "dark",
      provider: "dark",
    })
  })

  it("keeps the application's mode on the flow's pages while the user has not picked one", () => {
    localStorage.setItem("theme", "light")
    arrive(`/login?client_id=CRM&auth_request=${HANDLE}&ui_theme=dark`)

    expect(arrive(`/two-factor`)).toEqual({ boot: "dark", provider: "dark" })
    expect(arrive(`/create-organization?client_id=CRM&auth_request=${HANDLE}&ui_theme=dark`)).toEqual({
      boot: "dark",
      provider: "dark",
    })
  })
})
