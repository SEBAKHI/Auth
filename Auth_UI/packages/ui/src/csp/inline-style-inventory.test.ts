import { describe, expect, it } from "vitest"

import {
  APP_STYLE_SOURCES,
  collectBySource,
  cspHash,
  inventoryFile,
  inventoryFor,
  recordInlineStyles,
  type AppName,
  type StyleSource,
} from "./collect-inline-styles"

/**
 * apps/<app>/csp/inline-styles.json is exactly what the installed libraries
 * inject (card S02, target 3). A dependency bump that changes a library's
 * stylesheet fails here first, naming the file; `pnpm csp:collect` rewrites
 * it (this file with --update), and csp-style-policy.test.ts then asks for
 * the same hash in web.config.
 */

let collected: Promise<Record<StyleSource, string[]>> | undefined
const bySource = () => (collected ??= collectBySource())

describe("the inline style collector", () => {
  it("is not blind: a known <style> is recorded, with exactly its hash", async () => {
    const texts = await recordInlineStyles(() => {
      const style = document.createElement("style")
      document.head.appendChild(style)
      style.appendChild(document.createTextNode("p{color:red}"))
      const later = document.createElement("style")
      later.textContent = "b{color:blue}"
      document.head.appendChild(later)
      // React rewrites dangerouslySetInnerHTML on a mounted element in place.
      later.innerHTML = "b{color:navy}"
    })
    expect(texts).toEqual(["", "p{color:red}", "b{color:blue}", "b{color:navy}"])
    // The CSP hash grammar: base64 SHA-256 of the UTF-8 bytes. The empty text's
    // value is the well-known one, so a mistake in the encoding cannot hide.
    expect(cspHash("")).toBe("sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=")
  })

  it("does not record a <style> that never joins the document", async () => {
    const texts = await recordInlineStyles(() => {
      const detached = document.createElement("div")
      const style = document.createElement("style")
      style.textContent = "i{color:green}"
      detached.appendChild(style)
    })
    expect(texts).toEqual([])
  })

  it("sees every source inject (a floor: an empty inventory is never written)", async () => {
    const found = await bySource()
    expect(found.sonner).toContain("")
    expect(found.sonner.some((text) => text.includes("[data-sonner-toaster]"))).toBe(true)
    expect(found["radix-scroll-area"]).toEqual([
      expect.stringContaining("[data-radix-scroll-area-viewport]"),
    ])
    expect(found["radix-select"]).toEqual([expect.stringContaining("[data-radix-select-viewport]")])
    expect(found["input-otp"]).toEqual([""])
  })

  it.each<AppName>(["console", "accounts"])(
    "apps/%s/csp/inline-styles.json is what the installed libraries inject",
    async (app) => {
      const styles = inventoryFor(app, await bySource())
      expect(styles.flatMap((style) => style.sources).sort()).toEqual(
        expect.arrayContaining([...APP_STYLE_SOURCES[app]].sort())
      )
      await expect(inventoryFile(app, styles)).toMatchFileSnapshot(
        `../../../../apps/${app}/csp/inline-styles.json`
      )
    }
  )
})
