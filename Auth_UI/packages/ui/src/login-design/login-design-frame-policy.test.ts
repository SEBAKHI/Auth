import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"

import { describe, expect, it } from "vitest"

import { cspHeaders, inlineScripts, parsePolicy, sha256Source } from "@authsystem/ui/csp/csp-policy"
import accountsFrame from "../../../../apps/accounts/login-design-frame.html?raw"
import consoleFrame from "../../../../apps/console/login-design-frame.html?raw"
import accountsWebConfig from "../../../../apps/accounts/public/web.config?raw"
import consoleWebConfig from "../../../../apps/console/public/web.config?raw"

/**
 * The sign-in page design frame's own policy, in both apps (the accounts sign-in pages
 * and the console's previews). A design is free HTML and CSS an administrator wrote; the
 * frame runs only its receiver script, by hash, and loads only the app's own stylesheet
 * and fonts, and images by address from anywhere (owner decision Q9).
 *
 * Deliberate breaks, each turning a case red: add 'self' to the frame's script-src; edit
 * one copy of the receiver; drop 'self' from the accounts frame-src; drop the CORS header
 * from /assets.
 */

const FRAME = "login-design-frame.html"
// Read from disk: a stylesheet imported in a test is handled as CSS, not text.
const APPS = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "..", "apps")
const accountsFonts = readFileSync(join(APPS, "accounts", "src", "login-design-frame.css"), "utf8")
const consoleFonts = readFileSync(join(APPS, "console", "src", "login-design-frame.css"), "utf8")
const apps = [
  { name: "accounts", html: accountsFrame, webConfig: accountsWebConfig },
  { name: "console", html: consoleFrame, webConfig: consoleWebConfig },
]

const withoutComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, "").trim()

function locationHeaders(webConfig: string, path: string) {
  const location = new DOMParser()
    .parseFromString(webConfig, "application/xml")
    .querySelector(`configuration > location[path="${path}"]`)
  return location
    ? [...location.querySelectorAll("customHeaders > *")].map((entry) =>
        `${entry.tagName} ${entry.getAttribute("name")} ${entry.getAttribute("value") ?? ""}`.trim()
      )
    : null
}

describe("the sign-in page design frame's page", () => {
  it("is one page: both apps serve the same receiver and the same fonts", () => {
    expect(consoleFrame).toBe(accountsFrame)
    expect(withoutComments(consoleFonts)).toBe(withoutComments(accountsFonts))
    expect(withoutComments(accountsFonts)).toContain("@fontsource-variable/ibm-plex-sans")
    expect(withoutComments(accountsFonts)).toContain("@fontsource/ibm-plex-sans-arabic")
  })

  it("holds exactly one inline classic script, and no other", () => {
    expect(inlineScripts(accountsFrame)).toHaveLength(1)
    expect(accountsFrame).not.toMatch(/<script\s+[^>]*src/i)
    expect(accountsFrame).not.toMatch(/type="module"/i)
  })

  it("forces every link to a new tab, never this frame", () => {
    const [receiver] = inlineScripts(accountsFrame)
    expect(receiver).toContain('setAttribute("target", "_blank")')
    expect(receiver).toContain('"noopener"')
    expect(receiver).toContain('"a[href], area[href]"')
  })

  it.each(apps)("$name: has exactly its own policy and SAMEORIGIN, and the site's are removed", ({ html, webConfig }) => {
    const [receiver] = inlineScripts(html)
    const policy =
      `default-src 'none'; script-src ${sha256Source(receiver)}; style-src 'self' 'unsafe-inline'; ` +
      // The API origin in font-src: the receiver registers the platform's own fonts (P4).
      "font-src 'self' data: https://auth.example.com; img-src https: data:; base-uri 'none'; form-action 'none'; " +
      "frame-ancestors 'self'"

    const headers = cspHeaders(webConfig).filter((header) => header.location === FRAME)
    expect(headers.map((header) => header.name)).toEqual(["Content-Security-Policy"])
    expect(headers[0].value).toBe(policy)

    const siteHasReportOnly = cspHeaders(webConfig).some(
      (header) => header.location === null && header.name === "Content-Security-Policy-Report-Only"
    )
    expect(locationHeaders(webConfig, FRAME)).toEqual([
      "remove Content-Security-Policy",
      ...(siteHasReportOnly ? ["remove Content-Security-Policy-Report-Only"] : []),
      "remove X-Frame-Options",
      "add X-Frame-Options SAMEORIGIN",
      `add Content-Security-Policy ${policy}`,
    ])
  })

  it.each(apps)("$name: the frame runs its receiver alone: script-src is that hash and nothing else", ({ html, webConfig }) => {
    const [header] = cspHeaders(webConfig).filter((candidate) => candidate.location === FRAME)
    const directives = parsePolicy(header.value)
    expect(directives["script-src"]).toEqual([sha256Source(inlineScripts(html)[0])])
    expect(header.value).not.toMatch(/'unsafe-eval'|'unsafe-hashes'|'strict-dynamic'/)
    expect(directives["connect-src"]).toBeUndefined()
    expect(directives["frame-src"]).toBeUndefined()
    expect(directives["default-src"]).toEqual(["'none'"])
  })

  it.each(apps)("$name: /assets answers the opaque-origin frame (Access-Control-Allow-Origin)", ({ webConfig }) => {
    expect(locationHeaders(webConfig, "assets")).toEqual(["add Access-Control-Allow-Origin *"])
  })

  it("the accounts pages may frame it: frame-src 'self' in every site policy", () => {
    const site = cspHeaders(accountsWebConfig).filter((header) => header.location === null)
    expect(site.length).toBeGreaterThan(0)
    for (const header of site) expect(header.directives["frame-src"], header.name).toContain("'self'")
  })
})
