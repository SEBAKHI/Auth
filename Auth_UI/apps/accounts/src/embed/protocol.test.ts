import { afterEach, describe, expect, it, vi } from "vitest"

import { isLinkedAppOrigin, onHostMessage, readParentOrigin, tellHost, NAMESPACE } from "./protocol"

/** Which pages the bar's frames talk to: https subdomains of the IAM site, nothing else. */
describe("isLinkedAppOrigin", () => {
  it.each([
    ["https://crm.example.com", true],
    ["https://a.b.example.com", true],
    ["https://crm.example.com:8443", true],
    // The site itself serves no application here, and a look-alike is another site.
    ["https://example.com", false],
    ["https://evilexample.com", false],
    ["https://example.com.evil.net", false],
    ["http://crm.example.com", false],
    ["null", false],
    ["https://crm.example.com/path", false],
    ["", false],
  ])("%s -> %s", (origin, expected) => {
    expect(isLinkedAppOrigin(origin, "example.com")).toBe(expected)
  })

  it("accepts https://localhost on any port in local development only", () => {
    expect(isLinkedAppOrigin("https://localhost:5180", "localhost")).toBe(true)
    expect(isLinkedAppOrigin("https://localhost:5180", "example.com")).toBe(false)
    expect(isLinkedAppOrigin("http://localhost:5180", "localhost")).toBe(false)
  })
})

describe("readParentOrigin", () => {
  afterEach(() => vi.restoreAllMocks())

  function framedAt(search: string, ancestorOrigins?: string[]) {
    vi.spyOn(window, "parent", "get").mockReturnValue({} as Window)
    return {
      search,
      ancestorOrigins: ancestorOrigins ? Object.assign([...ancestorOrigins], { contains: () => false }) : undefined,
    } as unknown as Location
  }

  it("is the origin the loader named, when it is a linked application's", () => {
    expect(readParentOrigin(framedAt("?origin=https%3A%2F%2Flocalhost%3A5180"))).toBe("https://localhost:5180")
  })

  it("is null for an origin that is not a linked application's", () => {
    expect(readParentOrigin(framedAt("?origin=https%3A%2F%2Fevil.net"))).toBeNull()
    expect(readParentOrigin(framedAt(""))).toBeNull()
  })

  it("is null when the browser says another page framed the frame", () => {
    const location = framedAt("?origin=https%3A%2F%2Flocalhost%3A5180", ["https://localhost:6000"])
    expect(readParentOrigin(location)).toBeNull()
  })

  it("is null when nothing framed the page", () => {
    expect(readParentOrigin({ search: "?origin=https%3A%2F%2Flocalhost%3A5180" } as Location)).toBeNull()
  })
})

describe("messages", () => {
  afterEach(() => vi.restoreAllMocks())

  it("posts to the parent's origin only, with the namespace", () => {
    const postMessage = vi.fn()
    vi.spyOn(window, "parent", "get").mockReturnValue({ postMessage } as unknown as Window)
    tellHost("https://crm.example.com", { type: "close", focusBar: true })
    expect(postMessage).toHaveBeenCalledWith({ ns: NAMESPACE, type: "close", focusBar: true }, "https://crm.example.com")
  })

  it("hears only its parent, from its origin, in its namespace", () => {
    const parent = {} as Window
    vi.spyOn(window, "parent", "get").mockReturnValue(parent)
    const heard = vi.fn()
    const stop = onHostMessage("https://crm.example.com", heard)
    const send = (data: unknown, origin: string, source: unknown) =>
      window.dispatchEvent(new MessageEvent("message", { data, origin, source: source as Window }))

    send({ ns: NAMESPACE, type: "theme", value: "dark" }, "https://evil.example.com", parent)
    send({ ns: NAMESPACE, type: "theme", value: "dark" }, "https://crm.example.com", window)
    send({ type: "theme", value: "dark" }, "https://crm.example.com", parent)
    expect(heard).not.toHaveBeenCalled()

    send({ ns: NAMESPACE, type: "theme", value: "dark" }, "https://crm.example.com", parent)
    expect(heard).toHaveBeenCalledTimes(1)
    stop()
  })
})
