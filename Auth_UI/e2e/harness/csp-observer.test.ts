import { describe, expect, it } from "vitest"

import { normaliseDirective, parseConsoleViolation } from "./csp-observer"

/**
 * The console channel's parser, on the sentences Chromium wrote in the S02
 * spike (2026-10-12). A Report-Only violation is the enforced sentence at
 * "info" level with a closing clause; reading it as enforced would excuse a
 * real refusal, and dropping it would blind the channel to stage A of S02.
 */

const ENFORCED_STYLE =
  "Applying inline style violates the following Content Security Policy directive 'style-src 'self''. " +
  "Either the 'unsafe-inline' keyword, a hash ('sha256-lIwYhKbV90CwuOg56KqGEObmrozd3X7Hh9Wkv8O0FlM='), " +
  "or a nonce ('nonce-...') is required to enable inline execution."
const REPORTED_STYLE = `${ENFORCED_STYLE} The policy is report-only, so the violation has been logged but no further action has been taken.`

describe("parseConsoleViolation", () => {
  it("reads an enforced inline-style refusal", () => {
    expect(parseConsoleViolation(ENFORCED_STYLE)).toEqual({
      directive: "style-src",
      blocked: "inline",
      disposition: "enforce",
    })
  })

  it("reads the Report-Only sentence as a report, not a refusal", () => {
    expect(parseConsoleViolation(REPORTED_STYLE)).toEqual({
      directive: "style-src",
      blocked: "inline",
      disposition: "report",
    })
    expect(parseConsoleViolation(`[Report Only] ${ENFORCED_STYLE}`)?.disposition).toBe("report")
  })

  it("keeps the blocked URL of a load", () => {
    expect(
      parseConsoleViolation(
        "Loading the image 'https://attacker.example.net/x.png' violates the following Content Security " +
          "Policy directive: \"img-src 'self' data:\". The policy is report-only, so the violation has been " +
          "logged but no further action has been taken."
      )
    ).toEqual({ directive: "img-src", blocked: "https://attacker.example.net/x.png", disposition: "report" })
  })

  it("reads the Report-Only eval sentence, which has its own wording", () => {
    expect(
      parseConsoleViolation(
        "Evaluating a string as JavaScript violates the following Content Security Policy directive because " +
          "'unsafe-eval' is not an allowed source of script: \"script-src 'self' https://accounts.google.com\". " +
          "The policy is report-only, so the violation has been logged but no further action has been taken."
      )
    ).toEqual({ directive: "script-src", blocked: "eval", disposition: "report" })
  })

  it("reads a refused frame navigation, enforced and report-only", () => {
    const framing = (policy: string) =>
      `Framing 'https://attacker.example.net/' violates the following ${policy}Content Security Policy ` +
      `directive: "frame-src 'self' https://accounts.google.com". The request has been blocked.`
    expect(parseConsoleViolation(framing(""))).toEqual({
      directive: "frame-src",
      blocked: "https://attacker.example.net/",
      disposition: "enforce",
    })
    expect(parseConsoleViolation(framing("report-only "))?.disposition).toBe("report")
  })

  it("ignores any other console message", () => {
    expect(parseConsoleViolation("Download the React DevTools for a better development experience")).toBeNull()
  })

  it("counts -elem and -attr as their parent directive", () => {
    expect(normaliseDirective("style-src-elem")).toBe("style-src")
    expect(normaliseDirective("style-src-attr")).toBe("style-src")
  })
})
