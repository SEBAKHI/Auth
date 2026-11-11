import { describe, expect, it } from "vitest"

import { cspHeaders, siteHeader } from "@authsystem/ui/csp/csp-policy"
import inventoryJson from "../csp/inline-styles.json?raw"
import webConfig from "../public/web.config?raw"
import routes from "./routes.tsx?raw"

/**
 * The console's "Continue with Google" button depends on two things that no other
 * test touches and that no runtime check can catch in time.
 *
 * The first is the Content-Security-Policy in web.config. Since S02 PR D the button is
 * the app's own and loads nothing from Google into the page, so the policy names no
 * Google address at all; a source left in it would make Google a script, style or frame
 * source on every console page, secrets and audit logs included, for nothing.
 *
 * The second is the `providers` slot on the shared LoginPage. It is optional, and
 * the console rendered LoginPage bare for as long as the app has existed, so
 * dropping the prop is a silent revert to a console no Google-created
 * administrator can enter.
 */
// The site-wide policy, read by where it sits rather than as "the first CSP in
// the file": the email preview's <location> carries a CSP of its own (card
// S02), which src/csp-style-policy.test.ts pins instead.
const CSP = siteHeader(webConfig, "Content-Security-Policy").directives

// Every site-wide header. Card S02 left one, the enforced policy; a
// Report-Only header added later must satisfy the same rules.
const SITE_HEADERS = cspHeaders(webConfig).filter((header) => header.location === null)

const GOOGLE = "https://accounts.google.com"

describe("the console login surface", () => {
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

  it("keeps 'self' in frame-src", () => {
    // The notification preview frames /email-preview.html, a document on this
    // origin (card S02). A bare Google-only directive would refuse it, and the
    // preview would show its "unavailable" alert instead of the email.
    for (const header of SITE_HEADERS) {
      expect(header.directives["frame-src"], header.name).toContain("'self'")
    }
  })

  it("does not weaken script-src to buy the button", () => {
    for (const header of SITE_HEADERS) {
      expect(header.directives["script-src"], header.name).not.toContain("'unsafe-inline'")
      expect(header.directives["script-src"], header.name).not.toContain("'unsafe-eval'")
    }
  })

  it("still omits the Apple origins from the policy itself", () => {
    // The apple provider row is seeded disabled, so its button never renders here.
    // If that changes, these two hosts have to be added deliberately - this
    // assertion is what turns "forgot" into "decided". Asserted per directive
    // rather than over the raw file, which names them in a comment on purpose.
    expect(CSP["script-src"]).not.toContain("https://appleid.cdn-apple.com")
    expect(CSP["connect-src"]).not.toContain("https://appleid.apple.com")
  })

  it("commits the placeholder API origin, never a real deployment's", () => {
    // The deployed origin is written into dist/web.config by
    // scripts/seal-web-config.mjs at build time, from VITE_API_BASE_URL. Editing
    // this committed file to a real domain instead would publish that domain in a
    // public repository and hand every fork an origin that is not theirs - the
    // exact defect the placeholder exists to prevent. Listing the whole allowed
    // set rather than banning one string also makes any new source a decision:
    // add it here, or the build stays red.
    // No 'unsafe-inline' (card S02): a site-wide header that allows inline styles
    // or scripts fails here.
    const ALLOWED = new Set([
      "'self'",
      "'none'",
      "data:",
      "https://auth.example.com",
    ])
    // A hash is a source only in style-src, and only one of the inline styles
    // the bundle injects (csp/inline-styles.json, card S02).
    const HASHES = new Set(
      (JSON.parse(inventoryJson) as { styles: { hash: string }[] }).styles.map((style) => `'${style.hash}'`)
    )

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

  it("mounts the login page with its providers slot filled", () => {
    expect(routes).toContain("<ExternalProviders recoveryPath=")
    expect(routes).toMatch(/<LoginPage\s+recoveryPath=/)
  })

  it("gives both sign-in paths somewhere to send a pending-deletion account", () => {
    // Two independent dead ends lived here. The Google branch is guarded on
    // `recoveryPath &&`, so without the prop it short-circuited to a toast that
    // named the deletion and offered nothing to act on. The password branch
    // used to navigate to a hardcoded "/account-recovery" — a route only the
    // accounts app has — and fell through to the catch-all 404. Neither is
    // reachable by any test that renders a component, because both need an
    // account that is pending deletion.
    expect(routes).toContain(
      "const CONSOLE_RECOVERY_URL = `${ACCOUNTS_URL}/account-recovery`"
    )
    expect(routes).toContain("recoveryPath={CONSOLE_RECOVERY_URL}")
  })

  it("never derives the recovery target from anything user-controlled", () => {
    // An absolute recoveryPath reaches window.location.assign. Built from a
    // build-time constant it is a handoff; built from a query string or from
    // router state it is an open redirect.
    const declaration = routes.match(/const CONSOLE_RECOVERY_URL = [^\n]*/)?.[0]
    expect(declaration).toBeTruthy()
    expect(declaration).not.toMatch(/location|search|params|state|window/i)
  })
})
