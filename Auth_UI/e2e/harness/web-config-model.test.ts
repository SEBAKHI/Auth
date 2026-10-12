import { describe, expect, it } from "vitest"

import accountsWebConfig from "../../apps/accounts/public/web.config?raw"
import consoleWebConfig from "../../apps/console/public/web.config?raw"
import {
  UnsupportedWebConfigError,
  cacheControlValue,
  clientCacheFor,
  headersFor,
  headerValue,
  parseWebConfig,
  resolveRequest,
  sealedOriginProblem,
  type SiteFiles,
} from "./web-config-model"

/**
 * Pins what the harness extracts from each tracked web.config. Any edit to a
 * web.config fails here first, so whoever changes what IIS serves also reviews
 * what the harness serves (card S30a, Q3). The CSP is compared with the
 * attribute read straight from the file, never with a copy kept in this test.
 */

const console_ = parseWebConfig(consoleWebConfig, "apps/console/public/web.config")
const accounts = parseWebConfig(accountsWebConfig, "apps/accounts/public/web.config")

/** The CSP attribute exactly as the file writes it, read without the model. */
function cspAttribute(xml: string) {
  const match = /<add name="Content-Security-Policy" value="([^"]*)"/.exec(xml)
  expect(match).not.toBeNull()
  return match![1]
}

function site(files: string[], directories: string[]): SiteFiles {
  const fileSet = new Set(files.map((file) => file.toLowerCase()))
  const directorySet = new Set(directories.map((directory) => directory.toLowerCase()))
  return {
    isFile: (path) => fileSet.has(path.toLowerCase()),
    isDirectory: (path) => directorySet.has(path.replace(/(.)\/$/, "$1").toLowerCase()),
  }
}

const SPA_FILES = ["/index.html", "/assets/index-abc.js", "/web.config"]
const SPA_DIRECTORIES = ["/", "/assets"]
const PRIVACY_FILES = ["/privacy/index.html", "/privacy/ar.html", "/privacy/v2026.09/index.html"]
const PRIVACY_DIRECTORIES = ["/privacy", "/privacy/v2026.09"]

describe("web.config model: console", () => {
  it("has one SPA Fallback rule with four conditions", () => {
    expect(console_.rules.map((rule) => rule.name)).toEqual(["SPA Fallback"])
    const [fallback] = console_.rules
    expect(fallback.stopProcessing).toBe(true)
    expect(fallback.conditions).toEqual([
      { input: "{REQUEST_FILENAME}", matchType: "IsFile", negate: true, ignoreCase: true },
      { input: "{REQUEST_FILENAME}", matchType: "IsDirectory", negate: true, ignoreCase: true },
      { input: "{REQUEST_URI}", matchType: "Pattern", pattern: "^/api/", negate: true, ignoreCase: true },
      { input: "{REQUEST_URI}", matchType: "Pattern", pattern: "^/assets/", negate: true, ignoreCase: true },
    ])
    expect(fallback.action).toEqual({ type: "Rewrite", url: "/index.html", appendQueryString: true })
  })

  it("sends six headers, the CSP and its Report-Only twin verbatim from the file", () => {
    expect(console_.headers.map(([name]) => name)).toEqual([
      "X-Content-Type-Options",
      "X-Frame-Options",
      "Referrer-Policy",
      "Strict-Transport-Security",
      "Content-Security-Policy",
      // Card S02, stage A: the target style-src, reported and not enforced.
      "Content-Security-Policy-Report-Only",
    ])
    expect(headerValue(console_, "Content-Security-Policy")).toBe(cspAttribute(consoleWebConfig))
  })

  it("disables caching for the shell and caches assets for 365 days", () => {
    expect(console_.clientCache).toEqual({ mode: "DisableCache" })
    expect(console_.locations).toEqual([
      { path: "assets", clientCache: { mode: "UseMaxAge", maxAgeSeconds: 31_536_000 } },
      expect.objectContaining({ path: "email-preview.html", clientCache: undefined }),
    ])
  })
})

describe("web.config model: accounts", () => {
  it("has the privacy rules then SPA Fallback, in order", () => {
    expect(accounts.rules.map((rule) => rule.name)).toEqual([
      "Privacy Root Canonical Slash",
      "Privacy Root Document",
      "Privacy Archive Root Canonical Slash",
      "Privacy Archive Root Document",
      "Privacy Archive Language Canonical URL",
      "Privacy Archive Language Document",
      "Privacy Language Canonical URL",
      "Privacy Language Document",
      "Privacy Static File",
      "Privacy Unknown Document",
      "SPA Fallback",
    ])
    expect(accounts.rules.map((rule) => rule.action.type)).toEqual([
      "Redirect",
      "Rewrite",
      "Redirect",
      "Rewrite",
      "Redirect",
      "Rewrite",
      "Redirect",
      "Rewrite",
      "None",
      "CustomResponse",
      "Rewrite",
    ])
    expect(accounts.rules[9].action).toMatchObject({ type: "CustomResponse", status: 404 })
  })

  it("sends six headers, the CSP and its Report-Only twin verbatim from the file", () => {
    expect(accounts.headers.map(([name]) => name)).toEqual([
      "X-Content-Type-Options",
      "X-Frame-Options",
      "Referrer-Policy",
      "Strict-Transport-Security",
      "Content-Security-Policy",
      // Card S02, stage A: the target style-src, reported and not enforced.
      "Content-Security-Policy-Report-Only",
    ])
    expect(headerValue(accounts, "Content-Security-Policy")).toBe(cspAttribute(accountsWebConfig))
  })

  it("disables caching for the shell and caches assets for 365 days", () => {
    expect(accounts.clientCache).toEqual({ mode: "DisableCache" })
    expect(accounts.locations).toEqual([
      { path: "assets", clientCache: { mode: "UseMaxAge", maxAgeSeconds: 31_536_000 } },
    ])
  })
})

describe("rewrite table and implicit IIS behaviour", () => {
  const consoleSite = site(SPA_FILES, SPA_DIRECTORIES)
  const accountsSite = site([...SPA_FILES, ...PRIVACY_FILES], [...SPA_DIRECTORIES, ...PRIVACY_DIRECTORIES])

  function cacheOf(model: typeof console_, path: string) {
    return cacheControlValue(clientCacheFor(model, path))
  }

  it.each([
    ["console", console_, consoleSite],
    ["accounts", accounts, accountsSite],
  ] as const)("%s: / serves index.html (defaultDocument), deep links fall back", (_, model, files) => {
    expect(resolveRequest(model, "/", "", files)).toEqual({ kind: "file", urlPath: "/index.html" })
    expect(resolveRequest(model, "/users/123", "", files)).toEqual({ kind: "file", urlPath: "/index.html" })
    expect(cacheOf(model, "/index.html")).toBe("no-cache")
    expect(resolveRequest(model, "/assets/index-abc.js", "", files)).toEqual({
      kind: "file",
      urlPath: "/assets/index-abc.js",
    })
    expect(cacheOf(model, "/assets/index-abc.js")).toBe("max-age=31536000")
  })

  it.each([
    ["console", console_, consoleSite],
    ["accounts", accounts, accountsSite],
  ] as const)("%s: a missing asset, an API path and web.config all 404", (_, model, files) => {
    expect(resolveRequest(model, "/assets/missing.js", "", files)).toMatchObject({ kind: "status", status: 404 })
    expect(resolveRequest(model, "/api/v1/x", "", files)).toMatchObject({ kind: "status", status: 404 })
    expect(resolveRequest(model, "/web.config", "", files)).toMatchObject({ kind: "status", status: 404 })
    // requestFiltering sees the requested path, so even a .config that does not
    // exist (which the fallback would otherwise rewrite to index.html) is refused.
    expect(resolveRequest(model, "/nothing.CONFIG", "", files)).toMatchObject({ kind: "status", status: 404 })
  })

  it("matches rule patterns ignoring case and keeps the query string", () => {
    expect(resolveRequest(accounts, "/PRIVACY", "", accountsSite)).toEqual({
      kind: "redirect",
      status: 301,
      location: "/privacy/",
    })
    expect(resolveRequest(accounts, "/privacy", "", accountsSite)).toEqual({
      kind: "redirect",
      status: 301,
      location: "/privacy/",
    })
    expect(resolveRequest(accounts, "/privacy", "lang=ar", accountsSite)).toEqual({
      kind: "redirect",
      status: 301,
      location: "/privacy/?lang=ar",
    })
  })

  it("maps the privacy documents and refuses unknown ones", () => {
    expect(resolveRequest(accounts, "/privacy/", "", accountsSite)).toEqual({
      kind: "file",
      urlPath: "/privacy/index.html",
    })
    expect(resolveRequest(accounts, "/privacy/ar-SA", "", accountsSite)).toEqual({
      kind: "file",
      urlPath: "/privacy/ar.html",
    })
    expect(resolveRequest(accounts, "/privacy/v2026.09/", "", accountsSite)).toEqual({
      kind: "file",
      urlPath: "/privacy/v2026.09/index.html",
    })
    expect(resolveRequest(accounts, "/privacy/xx", "", accountsSite)).toEqual({
      kind: "status",
      status: 404,
      reason: "Not Found",
      description: "The requested privacy-policy document was not found.",
    })
  })
})

describe("customHeaders inside a <location> (card S02)", () => {
  const SITE_HEADERS = `<add name="X-Frame-Options" value="DENY" /><add name="Content-Security-Policy" value="default-src 'self'" />`
  const withLocation = (entries: string) =>
    `<?xml version="1.0"?><configuration><system.webServer><httpProtocol><customHeaders>${SITE_HEADERS}</customHeaders></httpProtocol></system.webServer>
    <location path="frame.html"><system.webServer><httpProtocol><customHeaders>${entries}</customHeaders></httpProtocol></system.webServer></location></configuration>`

  it("serves /email-preview.html its own CSP and SAMEORIGIN, and leaves every other path alone", () => {
    const preview = headersFor(console_, "/email-preview.html")
    expect(preview.map(([name]) => name)).toEqual([
      "X-Content-Type-Options",
      "Referrer-Policy",
      "Strict-Transport-Security",
      "X-Frame-Options",
      "Content-Security-Policy",
    ])
    const values = Object.fromEntries(preview)
    expect(values["X-Frame-Options"]).toBe("SAMEORIGIN")
    expect(values["Content-Security-Policy"]).toMatch(/^default-src 'none'; script-src 'sha256-[^']+'; /)
    for (const path of ["/index.html", "/email-preview.html.bak", "/assets/email-preview.html"]) {
      expect(headersFor(console_, path), path).toEqual(console_.headers)
    }
    expect(headersFor(accounts, "/email-preview.html")).toEqual(accounts.headers)
  })

  it("applies remove then add in document order", () => {
    const model = parseWebConfig(
      withLocation(`<remove name="Content-Security-Policy" /><add name="Content-Security-Policy" value="default-src 'none'" />`),
      "synthetic/web.config"
    )
    expect(headersFor(model, "/frame.html")).toEqual([
      ["X-Frame-Options", "DENY"],
      ["Content-Security-Policy", "default-src 'none'"],
    ])
    expect(headersFor(model, "/other.html")).toEqual(model.headers)
  })

  it("refuses an add of a header the location still inherits, as IIS does", () => {
    expect(() =>
      parseWebConfig(withLocation(`<add name="X-Frame-Options" value="SAMEORIGIN" />`), "synthetic/web.config")
    ).toThrow(/duplicate add of "X-Frame-Options" in location\[path=frame\.html\]/)
  })

  it("refuses a remove that removes nothing", () => {
    expect(() =>
      parseWebConfig(withLocation(`<remove name="X-Frame-Option" />`), "synthetic/web.config")
    ).toThrow(/remove of "X-Frame-Option" in location\[path=frame\.html\], which removes nothing/)
  })

  it("still refuses a site-wide <remove> and an unknown element inside a location", () => {
    const siteRemove = `<?xml version="1.0"?><configuration><system.webServer><httpProtocol><customHeaders>
      <remove name="X-Powered-By" /></customHeaders></httpProtocol></system.webServer></configuration>`
    expect(() => parseWebConfig(siteRemove, "synthetic/web.config")).toThrow(
      /unsupported element system\.webServer\/httpProtocol\/customHeaders\/remove/
    )
    expect(() => parseWebConfig(withLocation(`<clear />`), "synthetic/web.config")).toThrow(
      /unsupported element location\/system\.webServer\/httpProtocol\/customHeaders\/clear/
    )
  })
})

describe("sealed API origin (harness start-up check)", () => {
  it("accepts the tracked policies, which name the placeholder the harness serves", () => {
    expect(sealedOriginProblem(console_, "https://auth.example.com")).toBeNull()
    expect(sealedOriginProblem(accounts, "https://auth.example.com")).toBeNull()
  })

  it("refuses a build sealed to another origin, naming the directive", () => {
    const sealedElsewhere = parseWebConfig(
      consoleWebConfig.split("https://auth.example.com").join("https://auth.real.test"),
      "apps/console/dist-harness/web.config"
    )
    expect(sealedOriginProblem(sealedElsewhere, "https://auth.example.com")).toMatch(
      /dist-harness\/web\.config does not name https:\/\/auth\.example\.com in connect-src/
    )
  })
})

describe("unsupported configuration", () => {
  it("names the path of an element the harness does not model", () => {
    const xml = `<?xml version="1.0"?><configuration><system.webServer><staticContent>
      <clientCache cacheControlMode="DisableCache" />
      <mimeMap fileExtension=".webmanifest" mimeType="application/manifest+json" />
    </staticContent></system.webServer></configuration>`
    expect(() => parseWebConfig(xml, "synthetic/web.config")).toThrow(UnsupportedWebConfigError)
    expect(() => parseWebConfig(xml, "synthetic/web.config")).toThrow(
      /synthetic\/web\.config: unsupported element system\.webServer\/staticContent\/mimeMap/
    )
  })

  it("refuses an attribute on <configuration> and a CustomResponse without a status", () => {
    expect(() =>
      parseWebConfig(`<?xml version="1.0"?><configuration xmlns:x="urn:x"></configuration>`, "synthetic/web.config")
    ).toThrow(/attribute xmlns:x on configuration/)
    const noStatus = `<?xml version="1.0"?><configuration><system.webServer><rewrite><rules>
      <rule name="R"><match url=".*" /><action type="CustomResponse" /></rule>
    </rules></rewrite></system.webServer></configuration>`
    expect(() => parseWebConfig(noStatus, "synthetic/web.config")).toThrow(/CustomResponse statusCode ""/)
  })

  it("names an attribute the harness does not model", () => {
    const xml = `<?xml version="1.0"?><configuration><system.webServer><rewrite><rules>
      <rule name="R" patternSyntax="Wildcard"><match url="*" /><action type="None" /></rule>
    </rules></rewrite></system.webServer></configuration>`
    expect(() => parseWebConfig(xml, "synthetic/web.config")).toThrow(
      /attribute patternSyntax on system\.webServer\/rewrite\/rules\/rule/
    )
  })
})
