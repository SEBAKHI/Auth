import { readFileSync } from "node:fs"
import { join } from "node:path"
import { afterEach, beforeAll, describe, expect, it } from "vitest"

import { HIDE_VALUES, NAMESPACE, PANELS, THEMES } from "./protocol"

/**
 * The loader a linked application adds (public/embed/v1/account-bar.js), run as the
 * application's page would run it: a classic script from the accounts origin. Its shadow
 * roots are closed, as they are for the application, so the test keeps the roots it makes.
 */

const LOADER = readFileSync(join(__dirname, "..", "..", "public", "embed", "v1", "account-bar.js"), "utf8")
const ACCOUNTS = "https://accounts.example.com"

const roots: ShadowRoot[] = []

beforeAll(() => {
  const attach = Element.prototype.attachShadow
  Element.prototype.attachShadow = function (init) {
    const root = attach.call(this, init)
    roots.push(root)
    return root
  }
  const script = document.createElement("script")
  script.src = `${ACCOUNTS}/embed/v1/account-bar.js`
  Object.defineProperty(document, "currentScript", { configurable: true, get: () => script })
  new Function(LOADER)()
  Object.defineProperty(document, "currentScript", { configurable: true, get: () => null })
})

afterEach(() => {
  document.body.replaceChildren()
  roots.length = 0
})

/** The element, its bar frame, and a way to post as one of its frames. */
function mount(attributes: Record<string, string> = {}) {
  const element = document.createElement("iam-account-bar")
  for (const [name, value] of Object.entries(attributes)) element.setAttribute(name, value)
  document.body.appendChild(element)
  const bar = roots[0].querySelector("iframe")!
  const layer = roots[1]
  const navigations: { action: string; href: string }[] = []
  element.addEventListener("iam-account-bar:navigate", (event) => {
    event.preventDefault()
    navigations.push((event as CustomEvent).detail)
  })
  const post = (source: Window, data: Record<string, unknown>, origin = ACCOUNTS) =>
    window.dispatchEvent(new MessageEvent("message", { data: { ns: NAMESPACE, ...data }, origin, source }))
  const panel = () => layer.querySelector("iframe")
  return { element, bar, layer, panel, navigations, post }
}

describe("account-bar.js", () => {
  it("speaks the frames' protocol: the same namespace, panels, hide and theme values", () => {
    // The loader cannot import protocol.ts; this is what holds the two to one vocabulary.
    expect(LOADER).toContain(`var NS = "${NAMESPACE}"`)
    expect(LOADER).toContain(`var PANELS = ${JSON.stringify(PANELS).replaceAll(",", ", ")}`)
    expect(LOADER).toContain(`var HIDE = ${JSON.stringify(HIDE_VALUES).replaceAll(",", ", ")}`)
    expect(LOADER).toContain(`var THEMES = ${JSON.stringify(THEMES).replaceAll(",", ", ")}`)
  })

  it("frames the bar from the script's own origin, naming this page's origin, sandboxed without top navigation", () => {
    const { bar } = mount()
    const src = new URL(bar.src)
    expect(src.origin).toBe(ACCOUNTS)
    expect(src.pathname).toBe("/embed/v1/bar.html")
    expect(src.searchParams.get("origin")).toBe(location.origin)
    expect(bar.getAttribute("sandbox")).toBe("allow-scripts allow-same-origin")
  })

  it.each([
    ["apps", "apps"],
    ["avatar", "avatar"],
    ["everything", null],
  ])("passes hide=%s to the bar as %s", (value, expected) => {
    const { bar } = mount({ hide: value })
    expect(new URL(bar.src).searchParams.get("hide")).toBe(expected)
  })

  it("sizes the bar from its own message, and hides it while it draws nothing", () => {
    const { bar, post } = mount()
    expect(bar.hasAttribute("data-empty")).toBe(true)
    post(bar.contentWindow!, { type: "size", width: 88, height: 48, inset: 4 })
    expect(bar.style.width).toBe("88px")
    expect(bar.style.height).toBe("48px")
    expect(bar.hasAttribute("data-empty")).toBe(false)
    post(bar.contentWindow!, { type: "size", width: 0, height: 0, inset: 0 })
    expect(bar.hasAttribute("data-empty")).toBe(true)
  })

  it("keeps every frame inside the screen, whatever size it reports", () => {
    const { bar, panel, post } = mount()
    post(bar.contentWindow!, { type: "size", width: 99_999, height: 99_999, inset: 4 })
    expect(bar.style.width).toBe(`${window.innerWidth}px`)
    expect(bar.style.height).toBe("200px")
    post(bar.contentWindow!, { type: "toggle", panel: "apps" })
    const frame = panel()!
    post(frame.contentWindow!, { type: "size", width: 99_999, height: 99_999, inset: 16 })
    expect(frame.style.width).toBe(`${Math.min(window.innerWidth, 640)}px`)
    expect(frame.style.height).toBe(`${window.innerHeight}px`)
  })

  it("ignores a message from another origin, from a window it did not frame, or without the namespace", () => {
    const { bar, post } = mount()
    post(bar.contentWindow!, { type: "size", width: 88, height: 48, inset: 4 }, "https://evil.example.com")
    post(window, { type: "size", width: 88, height: 48, inset: 4 })
    window.dispatchEvent(
      new MessageEvent("message", { data: { type: "size", width: 88, height: 48 }, origin: ACCOUNTS, source: bar.contentWindow })
    )
    expect(bar.style.width).toBe("")
    expect(bar.hasAttribute("data-empty")).toBe(true)
  })

  it("opens a panel on the bar's request and closes it on a press anywhere on the page", () => {
    const { bar, panel, post } = mount()
    post(bar.contentWindow!, { type: "toggle", panel: "apps" })
    const frame = panel()!
    const src = new URL(frame.src)
    expect(src.pathname).toBe("/embed/v1/panel.html")
    expect(src.searchParams.get("panel")).toBe("apps")
    expect(frame.getAttribute("sandbox")).not.toContain("allow-top-navigation")
    post(frame.contentWindow!, { type: "size", width: 352, height: 300, inset: 16 })
    expect(frame.hasAttribute("data-shown")).toBe(true)

    document.dispatchEvent(new Event("pointerdown", { bubbles: true }))
    expect(frame.hasAttribute("data-shown")).toBe(false)
  })

  it("refuses a panel name it does not know", () => {
    const { bar, panel, post } = mount()
    post(bar.contentWindow!, { type: "toggle", panel: "settings" })
    expect(panel()).toBeNull()
  })

  it("signs out through the application's own address, from the account panel only", () => {
    const { bar, panel, navigations, post } = mount({ "signout-href": "/auth/signout?next=%2F" })
    post(bar.contentWindow!, { type: "toggle", panel: "account" })
    const frame = panel()!
    expect(new URL(frame.src).searchParams.get("signout")).toBe("1")

    post(bar.contentWindow!, { type: "signout" })
    expect(navigations).toEqual([])

    post(frame.contentWindow!, { type: "signout" })
    expect(navigations).toEqual([{ action: "signout", href: `${location.origin}/auth/signout?next=%2F` }])
  })

  it.each([undefined, "javascript:alert(1)", "data:text/html,x", "http://app.example.com/signout"])(
    "offers no sign-out without a safe address of the application's (%s)",
    (href) => {
      const { bar, panel, navigations, post } = mount(href === undefined ? {} : { "signout-href": href })
      post(bar.contentWindow!, { type: "toggle", panel: "account" })
      const frame = panel()!
      expect(new URL(frame.src).searchParams.get("signout")).toBeNull()
      post(frame.contentWindow!, { type: "signout" })
      expect(navigations).toEqual([])
    }
  )

  it("signs in through the application's address, else the accounts sign-in page", () => {
    const withHref = mount({ "signin-href": "https://app.example.com/login" })
    withHref.post(withHref.bar.contentWindow!, { type: "signin" })
    expect(withHref.navigations).toEqual([{ action: "signin", href: "https://app.example.com/login" }])
    document.body.replaceChildren()
    roots.length = 0

    const without = mount()
    without.post(without.bar.contentWindow!, { type: "signin" })
    expect(without.navigations).toEqual([{ action: "signin", href: `${ACCOUNTS}/login` }])
  })

  it("passes the application's colour mode, and only a known one", () => {
    expect(new URL(mount({ theme: "dark" }).bar.src).searchParams.get("theme")).toBe("dark")
    document.body.replaceChildren()
    roots.length = 0
    expect(new URL(mount({ theme: "purple" }).bar.src).searchParams.get("theme")).toBeNull()
  })
})
