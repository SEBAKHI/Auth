import { describe, expect, it } from "vitest"

import { cspHeaders } from "@authsystem/ui/csp/csp-policy"
import inventoryJson from "../csp/inline-styles.json?raw"
import privacyHashesJson from "../csp/privacy-style-hashes.json?raw"
import webConfig from "../public/web.config?raw"

/**
 * The accounts app's "Continue with Google" button depends on a
 * Content-Security-Policy that no runtime check can catch in time: a missing
 * directive produces no exception, no failed request the SPA can observe, and no
 * toast. Every unit test, every typecheck and every curl against the deployed
 * origin still passes.
 *
 * <p>
 * The console has carried this guard since its own button existed; this app did
 * not, and that asymmetry is how the defect shipped here. The policy admitted
 * Google's script and its button iframe while refusing the stylesheet that script
 * fetches from /gsi/style, so the button rendered unstyled and every page
 * offering it logged two violations.
 * </p>
 */
// Both site-wide headers, read by where they sit (outside any <location>), not
// as "the first CSP in the file": the enforced one and, while card S02 is in
// its stage A, the Report-Only target that will replace it. What the button
// needs must hold in both.
const SITE_HEADERS = cspHeaders(webConfig).filter((header) => header.location === null)

const GOOGLE = "https://accounts.google.com"

describe("the accounts login surface", () => {
  it.each(["script-src", "connect-src", "frame-src", "style-src"])(
    "allows Google Identity Services in %s",
    (directive) => {
      expect(SITE_HEADERS.length).toBeGreaterThan(0)
      for (const header of SITE_HEADERS) {
        expect(header.directives[directive], header.name).toContain(GOOGLE)
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
      GOOGLE,
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
