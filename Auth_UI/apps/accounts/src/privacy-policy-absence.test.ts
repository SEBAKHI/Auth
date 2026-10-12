import { describe, expect, it } from "vitest"

import { cspHeaders } from "@authsystem/ui/csp/csp-policy"
import inventoryJson from "../csp/inline-styles.json?raw"
import privacyHashesJson from "../csp/privacy-style-hashes.json?raw"
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
    expect(webConfig).not.toMatch(/https?:\/\/[^"\s]*\/privacy/)
    expect(privacyCspRemoval).toBeNull()
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
      "https://accounts.google.com",
      "https://appleid.apple.com",
      "https://appleid.cdn-apple.com",
    ])
    // A hash is a source only in style-src, and only one of the derived set
    // (csp/*.json, card S02) - /privacy's stylesheet hashes among them.
    const HASHES = new Set([
      ...(JSON.parse(inventoryJson) as { styles: { hash: string }[] }).styles.map((style) => `'${style.hash}'`),
      ...(JSON.parse(privacyHashesJson) as { hashes: { hash: string }[] }).hashes.map((entry) => `'${entry.hash}'`),
    ])

    // Every site-wide header (the enforced one and the Report-Only target),
    // read by where it sits rather than as the first match in the file.
    const site = cspHeaders(webConfig).filter((header) => header.location === null)
    expect(site.length).toBeGreaterThan(0)

    const undeclared = site.flatMap((header) =>
      Object.entries(header.directives).flatMap(([name, sources]) =>
        sources
          .filter((s) => !ALLOWED.has(s) && !(name === "style-src" && HASHES.has(s)))
          .map((s) => `${header.name}: ${name} ${s}`)
      )
    )

    expect(undeclared).toEqual([])
  })
})
