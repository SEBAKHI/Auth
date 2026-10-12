import { existsSync, readFileSync, readdirSync } from "node:fs"
import { dirname, join, relative } from "node:path"
import { fileURLToPath } from "node:url"

import { describe, expect, it } from "vitest"

import {
  cspHeaders,
  inlineScripts,
  parsePolicy,
  sha256Source,
  siteHeader,
  type CspHeader,
  type Directives,
} from "@authsystem/ui/csp/csp-policy"
import inventoryJson from "../csp/inline-styles.json?raw"
import previewHtml from "../public/email-preview.html?raw"
import webConfig from "../public/web.config?raw"
import vitestConfig from "../../../vitest.config.ts?raw"

/**
 * The console's style-src carries no 'unsafe-inline' and exactly the hashes
 * of the inline styles its bundle injects (card S02, targets 1-2, 6), and the
 * email preview has its own policy (target 6).
 *
 * Stage A (now): the enforced policy is unchanged and the target sits in a
 * Content-Security-Policy-Report-Only header. Stage B: the target is enforced
 * and the Report-Only header is gone. The stage is read from the file, and a
 * file that is in neither (one header changed without the other) fails.
 */

const AUTH_UI = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..")
const inventory = JSON.parse(inventoryJson) as { styles: { hash: string; text: string; sources: string[] }[] }
const HASHES = inventory.styles.map((style) => `'${style.hash}'`)

const enforced = siteHeader(webConfig, "Content-Security-Policy")
const reportOnly = cspHeaders(webConfig).filter(
  (header) => header.location === null && header.name === "Content-Security-Policy-Report-Only"
)
const stage = reportOnly.length === 1 && enforced.directives["style-src"].includes("'unsafe-inline'") ? "A" : "B"
/** The header that carries the target policy in this stage. */
const target: CspHeader = stage === "A" ? reportOnly[0] : enforced

const hashesIn = (directives: Directives) =>
  Object.entries(directives).flatMap(([directive, sources]) =>
    sources.filter((source) => source.startsWith("'sha")).map((source) => ({ directive, source }))
  )

describe("console CSP: inline styles (card S02)", () => {
  it("is in stage A or B, never in between", () => {
    if (stage === "B") {
      expect(reportOnly).toEqual([])
      expect(enforced.directives["style-src"]).not.toContain("'unsafe-inline'")
    } else {
      expect(reportOnly).toHaveLength(1)
    }
  })

  it("(a) the target style-src allows exactly the hashes of csp/inline-styles.json, and no other directive has one", () => {
    const found = hashesIn(target.directives)
    expect(found.filter(({ directive }) => directive !== "style-src")).toEqual([])
    expect(found.map(({ source }) => source).sort()).toEqual([...HASHES].sort())
    // The enforced policy of stage A carries no hash anywhere: the target is
    // reported, not enforced.
    if (stage === "A") expect(hashesIn(enforced.directives)).toEqual([])
  })

  it("(a) each hash is the hash of its text", () => {
    for (const style of inventory.styles) expect(sha256Source(style.text)).toBe(`'${style.hash}'`)
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
    // The same sources besides the hashes, minus 'unsafe-inline'.
    expect(targetStyles.filter((source) => !source.startsWith("'sha"))).toEqual(
      enforcedStyles.filter((source) => source !== "'unsafe-inline'")
    )
  })

  it("(d) /email-preview.html has exactly its own policy and SAMEORIGIN, and the site's are removed", () => {
    const scripts = inlineScripts(previewHtml)
    expect(scripts).toHaveLength(1)
    expect(previewHtml).not.toMatch(/<script\s+src/i)
    const preview = cspHeaders(webConfig).filter((header) => header.location === "email-preview.html")
    expect(preview.map((header) => header.name)).toEqual(["Content-Security-Policy"])
    expect(preview[0].value).toBe(
      `default-src 'none'; script-src ${sha256Source(scripts[0])}; style-src 'unsafe-inline'; ` +
        "img-src 'self' data: https://auth.example.com; base-uri 'none'; form-action 'none'; frame-ancestors 'self'"
    )
    const scriptSrc = parsePolicy(preview[0].value)["script-src"]
    expect(scriptSrc).toEqual([sha256Source(scripts[0])])

    const location = new DOMParser()
      .parseFromString(webConfig, "application/xml")
      .querySelector('configuration > location[path="email-preview.html"] customHeaders')!
    expect(
      [...location.children].map((entry) => `${entry.tagName} ${entry.getAttribute("name")} ${entry.getAttribute("value") ?? ""}`.trim())
    ).toEqual([
      "remove Content-Security-Policy",
      "remove Content-Security-Policy-Report-Only",
      "remove X-Frame-Options",
      "add X-Frame-Options SAMEORIGIN",
      `add Content-Security-Policy ${preview[0].value}`,
    ])
  })

  it("(e) floor: the inventory is not empty (at least four texts reach the console)", () => {
    expect(inventory.styles.length).toBeGreaterThanOrEqual(4)
  })

  it("(f) floor: the seal's own test is collected by vitest", () => {
    expect(vitestConfig).toContain('"scripts/**/*.test.ts"')
    expect(existsSync(join(AUTH_UI, "scripts", "seal-web-config.test.ts"))).toBe(true)
  })

  it("target 9: no file under apps/ or packages/ mentions the inline-document frame attribute", () => {
    // Spelled in two halves so this file does not match itself.
    const attribute = new RegExp(["src", "doc"].join(""), "i")
    const skip = new Set(["node_modules", "dist", "dist-harness", "coverage"])
    const hits: string[] = []
    let scanned = 0
    const walk = (dir: string) => {
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        if (skip.has(entry.name)) continue
        const path = join(dir, entry.name)
        if (entry.isDirectory()) walk(path)
        else if (/\.(tsx?|html|config|css|json)$/.test(entry.name)) {
          scanned++
          if (attribute.test(readFileSync(path, "utf8"))) hits.push(relative(AUTH_UI, path))
        }
      }
    }
    walk(join(AUTH_UI, "apps"))
    walk(join(AUTH_UI, "packages"))
    expect(scanned).toBeGreaterThan(500)
    expect(hits).toEqual([])
  })
})
