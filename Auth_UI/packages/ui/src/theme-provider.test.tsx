import { act, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { isTheme, ThemeProvider, useTheme } from "./theme-provider"

function ThemeProbe() {
  const { theme, resolvedTheme, setTheme } = useTheme()
  return (
    <div>
      <output aria-label="theme">{theme}</output>
      <output aria-label="resolved theme">{resolvedTheme}</output>
      <button type="button" onClick={() => setTheme("dark")}>
        dark
      </button>
      <input aria-label="editor" />
      {/* What CodeMirror renders for a template body: not an input, so only
          the contenteditable check keeps the shortcut off it. */}
      <div aria-label="code" contentEditable suppressContentEditableWarning>
        <span aria-label="code text">body</span>
      </div>
    </div>
  )
}

function dispatchStorage(newValue: string) {
  const event = new Event("storage")
  Object.defineProperties(event, {
    key: { value: "theme" },
    newValue: { value: newValue },
    storageArea: { value: localStorage },
  })
  window.dispatchEvent(event)
}

describe("ThemeProvider", () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.classList.remove("light", "dark")
  })

  afterEach(() => vi.restoreAllMocks())

  it("validates stored theme values", () => {
    expect(isTheme("dark")).toBe(true)
    expect(isTheme("light")).toBe(true)
    expect(isTheme("system")).toBe(true)
    expect(isTheme("sepia")).toBe(false)
    expect(isTheme(null)).toBe(false)
  })

  it("uses the default, persists explicit changes, and applies the class", async () => {
    render(
      <ThemeProvider defaultTheme="light" disableTransitionOnChange={false}>
        <ThemeProbe />
      </ThemeProvider>
    )

    expect(screen.getByLabelText("theme")).toHaveTextContent("light")
    expect(document.documentElement).toHaveClass("light")
    await userEvent.click(screen.getByRole("button", { name: "dark" }))
    expect(screen.getByLabelText("resolved theme")).toHaveTextContent("dark")
    expect(document.documentElement).toHaveClass("dark")
    expect(localStorage.getItem("theme")).toBe("dark")
  })

  it("switching the theme leaves no <style> in <head>: the transition guard is an adopted sheet (card S02)", async () => {
    // jsdom has no adoptedStyleSheets; give the document one, as every
    // supported browser has, for this test only.
    let sheets: CSSStyleSheet[] = []
    Object.defineProperty(document, "adoptedStyleSheets", {
      configurable: true,
      get: () => sheets,
      set: (next: CSSStyleSheet[]) => {
        sheets = [...next]
      },
    })
    try {
      render(
        <ThemeProvider defaultTheme="light">
          <ThemeProbe />
        </ThemeProvider>
      )
      await waitFor(() => expect(document.adoptedStyleSheets).toEqual([]))
      await userEvent.click(screen.getByRole("button", { name: "dark" }))
      expect(document.documentElement).toHaveClass("dark")
      expect(document.head.querySelectorAll("style")).toHaveLength(0)
      // Floor: the guard really was adopted for the switch, then taken off.
      expect(sheets.map((sheet) => sheet.cssRules[0]?.cssText ?? "")).toEqual([
        expect.stringContaining("transition: none"),
      ])
      await waitFor(() => expect(document.adoptedStyleSheets).toEqual([]))
      expect(document.head.querySelectorAll("style")).toHaveLength(0)
    } finally {
      delete (document as { adoptedStyleSheets?: unknown }).adoptedStyleSheets
    }
  })

  /**
   * Each guard is asserted on its own. Dispatching two ignorable keystrokes in
   * one block used to hide the whole thing: with the guards removed the theme
   * toggled twice and landed back where it started, so the assertion passed.
   */
  function pressD(options: KeyboardEventInit = {}, target?: HTMLElement) {
    act(() => {
      ;(target ?? window).dispatchEvent(
        new KeyboardEvent("keydown", { key: "d", bubbles: true, ...options })
      )
    })
    return screen.getByLabelText("theme").textContent
  }

  function renderProbe() {
    render(
      <ThemeProvider defaultTheme="light" disableTransitionOnChange={false}>
        <ThemeProbe />
      </ThemeProvider>
    )
  }

  it("toggles the theme with D", () => {
    renderProbe()
    expect(pressD()).toBe("dark")
    expect(pressD()).toBe("light")
  })

  it.each([
    ["a held key", { repeat: true }],
    ["a Ctrl chord", { ctrlKey: true }],
    ["a Meta chord", { metaKey: true }],
    ["an Alt chord", { altKey: true }],
  ])("ignores %s", (_name, options) => {
    renderProbe()
    expect(pressD(options)).toBe("light")
  })

  it.each([
    ["a text field", "editor"],
    ["a contenteditable body", "code"],
  ])("ignores a D typed into %s", (_name, label) => {
    renderProbe()
    expect(pressD({}, screen.getByLabelText(label))).toBe("light")
  })

  it("ignores a D typed inside a contenteditable, not only on it", () => {
    // The event target is the inner span; only walking up to the editable
    // ancestor keeps the shortcut from firing mid-sentence.
    renderProbe()
    expect(pressD({}, screen.getByLabelText("code text"))).toBe("light")
  })

  it("adopts valid cross-tab changes and resets invalid values to the default", () => {
    render(
      <ThemeProvider defaultTheme="light" disableTransitionOnChange={false}>
        <ThemeProbe />
      </ThemeProvider>
    )

    act(() => {
      dispatchStorage("dark")
    })
    expect(screen.getByLabelText("theme")).toHaveTextContent("dark")

    act(() => {
      dispatchStorage("invalid")
    })
    expect(screen.getByLabelText("theme")).toHaveTextContent("light")
  })

  it("throws when the hook is used outside its provider", () => {
    const Broken = () => {
      useTheme()
      return null
    }
    expect(() => render(<Broken />)).toThrow(
      "useTheme must be used within a ThemeProvider"
    )
  })
})
