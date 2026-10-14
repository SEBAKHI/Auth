import { describe, expect, it } from "vitest"

import { cspHeaders, siteHeader } from "@authsystem/ui/csp/csp-policy"
import inventoryJson from "../csp/inline-styles.json?raw"
import webConfig from "../public/web.config?raw"
import routes from "./routes.tsx?raw"

/**
 * The console's "Continue with Google" button depends on two things that no other
 * test touches and that no runtime check can catch in time.
 *
 * The first is the Content-Security-Policy in web.config. When a directive is
 * missing the browser refuses the GSI script and the button simply never appears:
 * no exception, no failed request the SPA can observe, no toast. Every unit test,
 * every typecheck and every curl against the deployed origin still passes. The
 * console's CSP deliberately excluded these origins until the button existed, and
 * that history is exactly why a later cleanup is likely to remove them again.
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

// Both site-wide headers: the enforced one and, while card S02 is in its
// stage A, the Report-Only target that will replace it. What the button needs
// must hold in both.
const SITE_HEADERS = cspHeaders(webConfig).filter((header) => header.location === null)

const GOOGLE = "https://accounts.google.com"

describe("the console login surface", () => {
  // style-src is in this list because it was missing from it. The policy admitted
  // Google's script and its button iframe and refused the stylesheet that script
  // fetches from /gsi/style, so the button rendered unstyled and every page
  // offering it logged two violations - observed in a browser, and
  // invisible to every test here because only three directives were checked.
  it.each(["script-src", "connect-src", "frame-src", "style-src"])(
    "allows Google Identity Services in %s",
    (directive) => {
      expect(SITE_HEADERS.length).toBeGreaterThan(0)
      for (const header of SITE_HEADERS) {
        expect(header.directives[directive], header.name).toContain(GOOGLE)
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
    // 'unsafe-inline' is still allowed: the enforced style-src keeps it until
    // card S02's stage B, which removes it from this set.
    const ALLOWED = new Set([
      "'self'",
      "'none'",
      "'unsafe-inline'",
      "data:",
      "https://auth.example.com",
      GOOGLE,
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
