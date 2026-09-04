import { describe, expect, it } from "vitest"

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
const CSP = (() => {
  const configuration = new DOMParser().parseFromString(
    webConfig,
    "application/xml"
  )
  expect(configuration.querySelector("parsererror")).toBeNull()

  const value = configuration
    .querySelector('customHeaders > add[name="Content-Security-Policy"]')
    ?.getAttribute("value")
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

describe("the accounts login surface", () => {
  it.each(["script-src", "connect-src", "frame-src", "style-src"])(
    "allows Google Identity Services in %s",
    (directive) => {
      expect(CSP[directive]).toContain(GOOGLE)
    }
  )

  it("does not weaken script-src to buy the button", () => {
    expect(CSP["script-src"]).not.toContain("'unsafe-inline'")
    expect(CSP["script-src"]).not.toContain("'unsafe-eval'")
  })

  it("commits the placeholder API origin, never a real deployment's", () => {
    // The deployed origin is written into dist/web.config at build time. Editing
    // this committed file to a real domain would publish that domain in a public
    // repository. Listing the whole allowed set rather than banning one string
    // makes any new source a decision: add it here, or the build stays red.
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

    const undeclared = Object.entries(CSP).flatMap(([directive, sources]) =>
      sources
        .filter((source) => !ALLOWED.has(source))
        .map((source) => `${directive} ${source}`)
    )

    expect(undeclared).toEqual([])
  })
})
