import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { API_BASE_URL } from "@authsystem/api/env"
import { THEME_APPLIED_EVENT } from "@authsystem/ui/theme/apply-theme"

import {
  applyCachedFonts,
  applyPlatformFonts,
  DEFAULT_FONT_STACK,
  FONT_MAX_SIZE_BYTES,
  fontUrlsFrom,
  PLATFORM_FONT_FAMILY,
  platformFontFaces,
  platformFontsCss,
} from "./platform-fonts"

/**
 * The platform's own fonts on the page (UI batch P4, item 7): each file registered under a
 * family of the page's own, and the preset's two variables pointed at it, with the shipped
 * stack kept behind it (Arabic stays in IBM Plex Sans Arabic when the font has none).
 *
 * Deliberate breaks, each turning a case red: put the fonts back in preset.css's
 * `@theme inline` (the variable no longer reaches the utilities); drop DEFAULT_FONT_STACK from
 * the body declaration (Arabic falls to the OS font); use the uploaded family name in the CSS;
 * register the same URL twice on a refetch.
 */

function repoFile(relative: string): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(dir, relative)
    if (existsSync(candidate)) {
      const text = readFileSync(candidate, "utf8").split("\r\n").join("\n")
      return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text
    }
    dir = dirname(dir)
  }
  throw new Error(`${relative} not found above ${process.cwd()}`)
}

class FakeFontFace {
  static loads: { family: string; resolve: () => void; reject: () => void }[] = []
  status = "unloaded"
  family: string
  source: string
  descriptors: FontFaceDescriptors
  constructor(family: string, source: string, descriptors: FontFaceDescriptors) {
    this.family = family
    this.source = source
    this.descriptors = descriptors
  }
  load() {
    return new Promise<FakeFontFace>((resolve, reject) => {
      FakeFontFace.loads.push({
        family: this.family,
        resolve: () => {
          this.status = "loaded"
          resolve(this)
        },
        reject: () => {
          this.status = "error"
          reject(new Error("refused"))
        },
      })
    })
  }
}

const fontSet = new Set<FakeFontFace>()

beforeEach(() => {
  FakeFontFace.loads = []
  fontSet.clear()
  vi.stubGlobal("FontFace", FakeFontFace)
  Object.defineProperty(document, "fonts", {
    configurable: true,
    value: { add: (face: FakeFontFace) => fontSet.add(face), delete: (face: FakeFontFace) => fontSet.delete(face) },
  })
})

afterEach(async () => {
  await applyPlatformFonts({ heading: null, body: null })
  vi.unstubAllGlobals()
  window.localStorage.clear()
})

const sheet = () => document.getElementById("platform-fonts")?.textContent ?? null

describe("the shipped fonts", () => {
  it("are live variables in preset.css, not inlined into the utilities", () => {
    const preset = repoFile("Auth_UI/packages/ui/src/preset.css")
    const inline = /@theme inline \{([\s\S]*?)\n\}/.exec(preset)?.[1] ?? ""
    expect(inline).not.toContain("--font-sans")
    expect(inline).not.toContain("--font-heading")
    const live = /@theme \{([\s\S]*?)\n\}/.exec(preset)?.[1] ?? ""
    expect(live).toContain("--font-heading: var(--font-sans);")
    expect(live).toContain(`--font-sans: ${DEFAULT_FONT_STACK};`)
  })

  it("follow the server's size limit", () => {
    const source = repoFile("Auth/Auth.Domain/Constants/PlatformFonts.cs")
    const match = /MaxSizeBytes = ([\d *]+);/.exec(source)
    expect(match).not.toBeNull()
    const bytes = match![1].split("*").reduce((product, factor) => product * Number(factor.trim()), 1)
    expect(FONT_MAX_SIZE_BYTES).toBe(bytes)
  })
})

describe("platformFontsCss", () => {
  it("is nothing while both roles use the shipped font", () => {
    expect(platformFontsCss({ heading: null, body: null })).toBeNull()
  })

  it("puts the body font before the shipped stack, so Arabic still finds IBM Plex Sans Arabic", () => {
    expect(platformFontsCss({ heading: null, body: "https://api.test/f.woff2" })).toBe(
      `:root{--font-sans: "${PLATFORM_FONT_FAMILY.body}", 'IBM Plex Sans Variable', 'IBM Plex Sans Arabic', sans-serif;}`
    )
  })

  it("puts the heading font before the body's", () => {
    expect(platformFontsCss({ heading: "https://api.test/h.woff2", body: null })).toBe(
      `:root{--font-heading: "${PLATFORM_FONT_FAMILY.heading}", var(--font-sans);}`
    )
  })
})

describe("fontUrlsFrom", () => {
  it("reads the API's relative addresses against the API, and drops anything not http(s)", () => {
    expect(
      fontUrlsFrom({ heading: { url: "/uploads/images/fonts/a.woff2" }, body: { url: "javascript:alert(1)" } })
    ).toEqual({ heading: `${API_BASE_URL}/uploads/images/fonts/a.woff2`, body: null })
  })

  it("is none for an answer without fonts (an older API, a cold cache)", () => {
    expect(fontUrlsFrom(undefined)).toEqual({ heading: null, body: null })
  })
})

describe("applyPlatformFonts", () => {
  it("registers each file under the page's own family, sets the variables, and tells the design frames", async () => {
    const told = vi.fn()
    document.addEventListener(THEME_APPLIED_EVENT, told)

    const applied = applyPlatformFonts({ heading: "https://api.test/h.woff2", body: "https://api.test/b.woff2" })
    FakeFontFace.loads.forEach((load) => load.resolve())
    await applied

    expect([...fontSet].map((face) => [face.family, face.source, face.descriptors.display])).toEqual([
      [PLATFORM_FONT_FAMILY.heading, 'url("https://api.test/h.woff2") format("woff2")', "swap"],
      [PLATFORM_FONT_FAMILY.body, 'url("https://api.test/b.woff2") format("woff2")', "swap"],
    ])
    expect(sheet()).toContain(`--font-sans: "${PLATFORM_FONT_FAMILY.body}"`)
    expect(platformFontFaces()).toEqual([
      { family: PLATFORM_FONT_FAMILY.heading, url: "https://api.test/h.woff2" },
      { family: PLATFORM_FONT_FAMILY.body, url: "https://api.test/b.woff2" },
    ])
    expect(told).toHaveBeenCalledTimes(1)

    // A refetch that changes nothing registers and announces nothing.
    await applyPlatformFonts({ heading: "https://api.test/h.woff2", body: "https://api.test/b.woff2" })
    expect(fontSet.size).toBe(2)
    expect(FakeFontFace.loads).toHaveLength(2)
    expect(told).toHaveBeenCalledTimes(1)
    document.removeEventListener(THEME_APPLIED_EVENT, told)
  })

  it("takes a removed font away, and the variables with it", async () => {
    const applied = applyPlatformFonts({ heading: null, body: "https://api.test/b.woff2" })
    FakeFontFace.loads.forEach((load) => load.resolve())
    await applied

    await applyPlatformFonts({ heading: null, body: null })

    expect(fontSet.size).toBe(0)
    expect(sheet()).toBeNull()
    expect(platformFontFaces()).toEqual([])
  })

  it("settles when the browser refuses a file: the stack's next family takes over", async () => {
    const applied = applyPlatformFonts({ heading: null, body: "https://api.test/broken.woff2" })
    FakeFontFace.loads.forEach((load) => load.reject())

    await expect(applied).resolves.toBeUndefined()
    expect(sheet()).toContain(DEFAULT_FONT_STACK)
  })
})

describe("applyCachedFonts", () => {
  it("applies the fonts of the branding cached on the last visit", async () => {
    window.localStorage.setItem(
      "auth.ui.branding",
      JSON.stringify({ platformName: "X", fonts: { body: { url: "https://api.test/b.woff2" } } })
    )

    const ready = applyCachedFonts(1_000)
    FakeFontFace.loads.forEach((load) => load.resolve())
    await ready

    expect(platformFontFaces()).toEqual([{ family: PLATFORM_FONT_FAMILY.body, url: "https://api.test/b.woff2" }])
  })

  it("never holds the first frame past its budget", async () => {
    vi.useFakeTimers()
    window.localStorage.setItem("auth.ui.branding", JSON.stringify({ fonts: { body: { url: "https://api.test/slow.woff2" } } }))

    const ready = applyCachedFonts(600)
    let settled = false
    void ready.then(() => (settled = true))
    await vi.advanceTimersByTimeAsync(599)
    expect(settled).toBe(false)
    await vi.advanceTimersByTimeAsync(1)
    expect(settled).toBe(true)
    vi.useRealTimers()
  })

  it("does nothing without a cached copy", async () => {
    await applyCachedFonts()
    expect(sheet()).toBeNull()
    expect(fontSet.size).toBe(0)
  })
})
