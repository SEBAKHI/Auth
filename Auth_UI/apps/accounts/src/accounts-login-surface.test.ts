import { describe, expect, it } from "vitest"

import { cspHeaders } from "@authsystem/ui/csp/csp-policy"
import inventoryJson from "../csp/inline-styles.json?raw"
import privacyHashesJson from "../csp/privacy-style-hashes.json?raw"
import webConfig from "../public/web.config?raw"

/**
 * The accounts app's Content-Security-Policy around its "Continue with Google" button.
 *
 * <p>
 * Until S02 PR D the policy had to admit Google's script, its stylesheet and its button
 * frame, and Google's button wrote 81 style rules into the page, which kept the policy
 * from refusing inline styles. The button is now the app's own: a popup goes to Google,
 * and nothing of Google's is loaded into the page, so the policy names no Google address.
 * </p>
 */
// Both site-wide headers, read by where they sit (outside any <location>), not
// as "the first CSP in the file": the enforced one and, while card S02 is in
// its stage A, the Report-Only target that will replace it. What the button
// needs must hold in both.
const SITE_HEADERS = cspHeaders(webConfig).filter((header) => header.location === null)

const GOOGLE = "https://accounts.google.com"

describe("the accounts login surface", () => {
  // S02 PR D: the button is the app's own, its popup goes to Google, and no Google script,
  // stylesheet or frame is loaded into these pages. Google's own button needed the host in four
  // directives and wrote 81 style rules into the page; a source left here would admit Google
  // on every page for nothing.
  it.each(["script-src", "style-src", "connect-src", "frame-src", "img-src", "font-src", "default-src"])(
    "names no Google address in %s",
    (directive) => {
      expect(SITE_HEADERS.length).toBeGreaterThan(0)
      for (const header of SITE_HEADERS) {
        expect(header.directives[directive] ?? [], header.name).not.toContain(GOOGLE)
        expect((header.directives[directive] ?? []).filter((source) => source.includes("google")), header.name).toEqual([])
      }
    }
  )

  it("does not weaken script-src to buy the button", () => {
    for (const header of SITE_HEADERS) {
      expect(header.directives["script-src"], header.name).not.toContain("'unsafe-inline'")
      expect(header.directives["script-src"], header.name).not.toContain("'unsafe-eval'")
    }
  })

  it("commits the placeholder API origin, never a real deployment's", () => {
    // The deployed origin is written into dist/web.config at build time. Editing
    // this committed file to a real domain would publish that domain in a public
    // repository. Listing the whole allowed set rather than banning one string
    // makes any new source a decision: add it here, or the build stays red.
    // 'unsafe-inline' is still allowed: the enforced style-src keeps it until
    // card S02's stage B, which removes it from this set.
    const ALLOWED = new Set([
      "'self'",
      "'none'",
      "'unsafe-inline'",
      "data:",
      "https://auth.example.com",
      "https://appleid.cdn-apple.com",
      "https://appleid.apple.com",
    ])
    // A hash is a source only in style-src, and only one of the derived set:
    // the inline styles the bundle injects and the privacy renderer's
    // stylesheet hashes (csp/*.json, card S02).
    const HASHES = new Set([
      ...(JSON.parse(inventoryJson) as { styles: { hash: string }[] }).styles.map((style) => `'${style.hash}'`),
      ...(JSON.parse(privacyHashesJson) as { hashes: { hash: string }[] }).hashes.map((entry) => `'${entry.hash}'`),
    ])

    const undeclared = SITE_HEADERS.flatMap((header) =>
      Object.entries(header.directives).flatMap(([directive, sources]) =>
        sources
          .filter((source) => !ALLOWED.has(source) && !(directive === "style-src" && HASHES.has(source)))
          .map((source) => `${header.name}: ${directive} ${source}`)
      )
    )

    expect(SITE_HEADERS.length).toBeGreaterThan(0)
    expect(undeclared).toEqual([])
  })
})
