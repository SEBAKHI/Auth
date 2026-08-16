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

  it("mounts the login page with its providers slot filled", () => {
    expect(routes).toContain("<LoginPage providers={<ExternalProviders />} />")
  })
})
