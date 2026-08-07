import { describe, expect, it } from "vitest"

import webConfig from "../public/web.config?raw"

/**
 * The accounts app must ship no privacy-policy prose of its own.
 *
 * It used to carry a bundled copy of the whole document as an offline fallback.
 * That copy had bracketed placeholders where the data controller's identity
 * belongs, so it rendered a red "draft" banner and text like
 * "[LEGAL ENTITY NAME]" onto a public legal page for as long as the API call
 * took — on every cold load and every language switch.
 *
 * It also never bought the availability it was justified by: it lived inside
 * the lazily-imported /privacy chunk, so the most likely outage (a routine
 * frontend deploy leaving a tab asking for a chunk that no longer exists) took
 * the fallback down with the page it was meant to replace.
 *
 * The notice is now rendered when an operator publishes a revision and written
 * as complete HTML to persistent storage outside this application's deployment
 * root. This test fails if any policy prose comes back into the bundle.
 */
const applicationSources = import.meta.glob("./**/*.{ts,tsx}", {
  eager: true,
  query: "?raw",
  import: "default",
}) as Record<string, string>

/**
 * Strings that only exist inside a compiled-in policy document. Split so this
 * file's own text cannot match: the sources it scans include this one.
 */
const POLICY_ARTEFACTS = [
  ["[LEGAL", " ENTITY NAME]"],
  ["[PRIVACY", " CONTACT EMAIL]"],
  ["[REGISTERED", " ADDRESS]"],
  ["[HOSTING", " PROVIDER]"],
  ["unfilled", "Warning"],
  ["FALLBACK_", "DISCLOSURE"],
] as const

describe("the accounts app ships no policy document", () => {
  it.each(POLICY_ARTEFACTS)(
    "contains no source carrying %s%s",
    (head, tail) => {
      const needle = head + tail
      const offenders = Object.entries(applicationSources)
        .filter(([, source]) => source.includes(needle))
        .map(([path]) => path)

      expect(offenders).toEqual([])
    }
  )

  it("routes nothing at /privacy", () => {
    // The notice is a static document, so links to it are plain anchors. A
    // client route would reintroduce a page that cannot render without a bundle.
    const routes = applicationSources["./routes.tsx"]

    expect(routes).toBeDefined()
    expect(routes).not.toContain('path: "/privacy"')
  })

  it("serves /privacy from local static files without ARR or a CSP exception", () => {
    const configuration = new DOMParser().parseFromString(
      webConfig,
      "application/xml"
    )
    const parseError = configuration.querySelector("parsererror")
    const languageAction = configuration.querySelector(
      'rule[name="Privacy Language Document"] > action'
    )
    const archiveAction = configuration.querySelector(
      'rule[name="Privacy Archive Language Document"] > action'
    )
    const unknownAction = configuration.querySelector(
      'rule[name="Privacy Unknown Document"] > action'
    )
    const privacyCspRemoval = configuration.querySelector(
      'location[path="privacy"] customHeaders > remove[name="Content-Security-Policy"]'
    )

    expect(parseError).toBeNull()
    expect(languageAction?.getAttribute("type")).toBe("Rewrite")
    expect(languageAction?.getAttribute("url")).toBe("/privacy/{R:1}.html")
    expect(archiveAction?.getAttribute("url")).toBe(
      "/privacy/v{R:1}/{R:2}.html"
    )
    expect(unknownAction?.getAttribute("statusCode")).toBe("404")
    expect(webConfig).not.toContain("ApplicationRequestRouting")
    expect(webConfig).not.toContain("https://auth.astoom.com/privacy")
    expect(privacyCspRemoval).toBeNull()
  })
})
