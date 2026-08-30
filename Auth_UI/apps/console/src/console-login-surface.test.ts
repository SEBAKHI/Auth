import { describe, expect, it } from "vitest"

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
const CSP = (() => {
  const configuration = new DOMParser().parseFromString(
    webConfig,
    "application/xml"
  )
  expect(configuration.querySelector("parsererror")).toBeNull()

  const header = configuration.querySelector(
    'customHeaders > add[name="Content-Security-Policy"]'
  )
  const value = header?.getAttribute("value")
  expect(value).toBeTruthy()

  return Object.fromEntries(
    value!
      .split(";")
      .map((directive) => directive.trim())
      .filter(Boolean)
      .map((directive) => {
        const [name, ...sources] = directive.split(/\s+/)
        return [name, sources]
      })
  ) as Record<string, string[]>
})()

const GOOGLE = "https://accounts.google.com"

describe("the console login surface", () => {
  it.each(["script-src", "connect-src", "frame-src"])(
    "allows Google Identity Services in %s",
    (directive) => {
      expect(CSP[directive]).toContain(GOOGLE)
    }
  )

  it("keeps 'self' in frame-src", () => {
    // There was no frame-src before the Google button, so frames fell back to
    // default-src 'self'. A bare Google-only directive would silently narrow the
    // notification-preview srcdoc iframe, which inherits this policy.
    expect(CSP["frame-src"]).toContain("'self'")
  })

  it("does not weaken script-src to buy the button", () => {
    expect(CSP["script-src"]).not.toContain("'unsafe-inline'")
    expect(CSP["script-src"]).not.toContain("'unsafe-eval'")
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
    const ALLOWED = new Set([
      "'self'",
      "'none'",
      "'unsafe-inline'",
      "data:",
      "https://auth.example.com",
      GOOGLE,
    ])

    const undeclared = Object.entries(CSP).flatMap(([directive, sources]) =>
      sources.filter((source) => !ALLOWED.has(source)).map((s) => `${directive} ${s}`)
    )

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
