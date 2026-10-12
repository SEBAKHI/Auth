import { existsSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"

import { describe, expect, it } from "vitest"

import { cspHeaders, sha256Source, siteHeader, type CspHeader, type Directives } from "@authsystem/ui/csp/csp-policy"
import inventoryJson from "../csp/inline-styles.json?raw"
import privacyHashesJson from "../csp/privacy-style-hashes.json?raw"
import webConfig from "../public/web.config?raw"
import vitestConfig from "../../../vitest.config.ts?raw"

/**
 * The accounts style-src carries no 'unsafe-inline' and exactly the derived
 * hashes: the inline styles its bundle injects, and every stylesheet the
 * privacy-policy renderer has published, because /privacy is served from this
 * site under this policy (card S02, targets 1-2 and 12, Q3 A).
 *
 * Stage A (now): the enforced policy is unchanged and the target sits in a
 * Content-Security-Policy-Report-Only header. Stage B: the target is enforced
 * and the Report-Only header is gone. A file in neither stage fails.
 */

const AUTH_UI = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..")
const inventory = JSON.parse(inventoryJson) as { styles: { hash: string; text: string }[] }
const privacy = JSON.parse(privacyHashesJson) as { hashes: { hash: string }[] }
const LIBRARY_HASHES = inventory.styles.map((style) => `'${style.hash}'`)
const PRIVACY_HASHES = privacy.hashes.map((entry) => `'${entry.hash}'`)

const enforced = siteHeader(webConfig, "Content-Security-Policy")
const reportOnly = cspHeaders(webConfig).filter(
  (header) => header.location === null && header.name === "Content-Security-Policy-Report-Only"
)
const stage = reportOnly.length === 1 && enforced.directives["style-src"].includes("'unsafe-inline'") ? "A" : "B"
const target: CspHeader = stage === "A" ? reportOnly[0] : enforced

const hashesIn = (directives: Directives) =>
  Object.entries(directives).flatMap(([directive, sources]) =>
    sources.filter((source) => source.startsWith("'sha")).map((source) => ({ directive, source }))
  )

describe("accounts CSP: inline styles (card S02)", () => {
  it("is in stage A or B, never in between", () => {
    if (stage === "B") {
      expect(reportOnly).toEqual([])
      expect(enforced.directives["style-src"]).not.toContain("'unsafe-inline'")
    } else {
      expect(reportOnly).toHaveLength(1)
    }
  })

  it("(a) the target style-src allows exactly the library and privacy hashes, and no other directive has one", () => {
    const found = hashesIn(target.directives)
    expect(found.filter(({ directive }) => directive !== "style-src")).toEqual([])
    expect(found.map(({ source }) => source).sort()).toEqual([...LIBRARY_HASHES, ...PRIVACY_HASHES].sort())
    if (stage === "A") expect(hashesIn(enforced.directives)).toEqual([])
  })

  it("(a) each library hash is the hash of its text", () => {
    for (const style of inventory.styles) expect(sha256Source(style.text)).toBe(`'${style.hash}'`)
  })

  it("(a) the privacy list is append-only in form: unique, well-formed values", () => {
    expect(PRIVACY_HASHES.length).toBeGreaterThan(0)
    expect(new Set(PRIVACY_HASHES).size).toBe(PRIVACY_HASHES.length)
    for (const hash of PRIVACY_HASHES) expect(hash).toMatch(/^'sha256-[A-Za-z0-9+/]{43}='$/)
  })

  it("(b) the target allows no inline style: no 'unsafe-inline', 'unsafe-hashes', style-src-elem or style-src-attr", () => {
    expect(target.directives["style-src"]).not.toContain("'unsafe-inline'")
    expect(target.value).not.toContain("'unsafe-hashes'")
    expect(Object.keys(target.directives)).not.toContain("style-src-elem")
    expect(Object.keys(target.directives)).not.toContain("style-src-attr")
  })

  it("(c) the Report-Only header is the enforced policy with only style-src changed, and no frame-ancestors", () => {
    if (stage === "B") return
    const { "style-src": enforcedStyles, "frame-ancestors": framing, ...rest } = enforced.directives
    const { "style-src": targetStyles, ...targetRest } = target.directives
    expect(framing).toEqual(["'none'"])
    expect(targetRest).toEqual(rest)
    expect(Object.keys(target.directives)).not.toContain("frame-ancestors")
    expect(targetStyles.filter((source) => !source.startsWith("'sha"))).toEqual(
      enforcedStyles.filter((source) => source !== "'unsafe-inline'")
    )
  })

  it("has no <location> policy of its own (/privacy stays under the site's)", () => {
    expect(cspHeaders(webConfig).filter((header) => header.location !== null)).toEqual([])
  })

  it("(e) floor: the inventory is not empty (at least three texts reach accounts)", () => {
    expect(inventory.styles.length).toBeGreaterThanOrEqual(3)
  })

  it("(f) floor: the seal's own test is collected by vitest", () => {
    expect(vitestConfig).toContain('"scripts/**/*.test.ts"')
    expect(existsSync(join(AUTH_UI, "scripts", "seal-web-config.test.ts"))).toBe(true)
  })
})
