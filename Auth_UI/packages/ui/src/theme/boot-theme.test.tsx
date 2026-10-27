import { act, render } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { captureEntryHints, FLOW_HINTS_LIFETIME_MS } from "@authsystem/i18n/flow-hints"

import { ThemeProvider, useTheme, type Theme } from "../theme-provider"
import { bootTheme, bootThemeScript } from "./boot-theme"

/**
 * The colour mode a dark user's first frame is painted in. `bootTheme` runs as
 * a classic script before `<body>` is painted, and must pick what
 * `ThemeProvider` will pick once React runs. Its literals (the parameter, the
 * two storage keys) are held here to the modules that write them: the kept
 * hint is written by `captureEntryHints`, the stored choice by `ThemeProvider`.
 */

const html = document.documentElement

function device(dark: boolean) {
  vi.spyOn(window, "matchMedia").mockImplementation(
    (query: string) =>
      ({
        matches: dark && query === "(prefers-color-scheme: dark)",
        media: query,
        addEventListener: () => {},
        removeEventListener: () => {},
      }) as unknown as MediaQueryList
  )
}

function arriveAt(path: string) {
  window.history.replaceState(null, "", path)
}

/** What the served file does: the serialized function, run on its own. */
function runServedScript(hints = true) {
  new Function(bootThemeScript({ hints }))()
}

/** A choice stored the way the theme menu stores it. */
function storeThroughThemeProvider(theme: Theme) {
  let setTheme: ((next: Theme) => void) | null = null
  function Probe() {
    setTheme = useTheme().setTheme
    return null
  }
  const { unmount } = render(
    <ThemeProvider disableTransitionOnChange={false}>
      <Probe />
    </ThemeProvider>
  )
  act(() => setTheme!(theme))
  unmount()
  html.classList.remove("light", "dark")
}

describe("bootTheme", () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    html.classList.remove("light", "dark")
    arriveAt("/")
    device(false)
  })

  afterEach(() => {
    vi.restoreAllMocks()
    html.classList.remove("light", "dark")
    arriveAt("/")
  })

  it("follows a dark device when nothing names a mode", () => {
    device(true)

    bootTheme(window)

    expect(html.classList.contains("dark")).toBe(true)
  })

  it("follows a light device when nothing names a mode", () => {
    bootTheme(window)

    expect(html.classList.contains("light")).toBe(true)
  })

  it("takes the choice the theme menu stored, over the device", () => {
    device(true)
    storeThroughThemeProvider("light")

    bootTheme(window)

    expect(html.classList.contains("light")).toBe(true)
  })

  it("resolves a stored system choice through the device", () => {
    device(true)
    storeThroughThemeProvider("system")

    bootTheme(window)

    expect(html.classList.contains("dark")).toBe(true)
  })

  it("takes an application's ui_theme in the address over the stored choice", () => {
    storeThroughThemeProvider("light")
    arriveAt("/login?ui_theme=dark")

    bootTheme(window)

    expect(html.classList.contains("dark")).toBe(true)
  })

  it("takes the colour mode kept for the flow over the stored choice", () => {
    storeThroughThemeProvider("light")
    arriveAt("/profile?ui_theme=dark")
    captureEntryHints({ pendingRequest: null, clientId: null, continuation: "never" })
    // A reload: the address no longer names it, the tab still keeps it.
    arriveAt("/profile")

    bootTheme(window)

    expect(html.classList.contains("dark")).toBe(true)
  })

  it("ignores a kept mode past its lifetime", () => {
    arriveAt("/profile?ui_theme=dark")
    captureEntryHints(
      { pendingRequest: null, clientId: null, continuation: "never" },
      Date.now() - FLOW_HINTS_LIFETIME_MS - 1
    )
    arriveAt("/profile")

    bootTheme(window)

    expect(html.classList.contains("light")).toBe(true)
  })

  it("ignores values it does not know, and storage that refuses", () => {
    device(true)
    arriveAt("/login?ui_theme=Dark")
    localStorage.setItem("theme", "blue")
    sessionStorage.setItem("auth.flow.hints", "{damaged")
    vi.spyOn(Object.getPrototypeOf(window.localStorage) as Storage, "getItem").mockImplementation(() => {
      throw new Error("blocked")
    })

    expect(() => bootTheme(window)).not.toThrow()
    expect(html.classList.contains("dark")).toBe(true)
  })

  it("survives a browser without media queries", () => {
    vi.spyOn(window, "matchMedia").mockImplementation(() => {
      throw new Error("unsupported")
    })

    bootTheme(window)

    expect(html.classList.contains("light")).toBe(true)
  })

  it("works as the served file: the serialized function, on its own", () => {
    arriveAt("/login?ui_theme=dark")

    runServedScript()

    expect(html.classList.contains("dark")).toBe(true)
    expect(bootThemeScript({ hints: true })).toMatch(/^\(function bootTheme\(win, options/)
    expect(bootThemeScript({ hints: true }).trimEnd()).toMatch(/\}\)\(window, \{"hints":true\}\);$/)
  })

  it("an address that names a hint replaces the kept record, so the record is not read", () => {
    storeThroughThemeProvider("light")
    arriveAt("/profile?ui_theme=dark")
    captureEntryHints({ pendingRequest: null, clientId: null, continuation: "never" })
    // A new entry whose only hint is unusable: captureEntryHints will end the
    // kept record, so the stored choice decides.
    arriveAt("/profile?ui_theme=Dark")

    bootTheme(window)

    expect(html.classList.contains("light")).toBe(true)
  })

  it("in the console, neither the address nor a kept record speaks: its ThemeProvider reads neither", () => {
    arriveAt("/?ui_theme=dark")
    captureEntryHints({ pendingRequest: null, clientId: null, continuation: "never" })
    arriveAt("/?ui_theme=dark")

    runServedScript(false)

    expect(html.classList.contains("light")).toBe(true)
    expect(html.classList.contains("dark")).toBe(false)
  })
})

