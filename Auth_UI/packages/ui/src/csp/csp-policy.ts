import { createHash } from "node:crypto"

/**
 * Reads web.config the way IIS does for card S02's guards: every CSP header,
 * keyed by where it applies (site-wide, or the <location path> it sits in)
 * and by its name. Shared by apps/<app>/src/csp-style-policy.test.ts and the
 * login-surface allow-lists, so no guard reads "the first CSP" by accident.
 */

export type Directives = Record<string, string[]>

export interface CspHeader {
  /** The <location path> it applies to; null for the site-wide headers. */
  location: string | null
  name: "Content-Security-Policy" | "Content-Security-Policy-Report-Only"
  value: string
  directives: Directives
}

/**
 * A policy's directives by name. Names are lower-cased, as a browser compares
 * them. A repeated directive throws: a browser applies the FIRST copy and
 * ignores the rest, so a guard that read any copy could pass a policy whose
 * enforced copy says something else (`style-src 'unsafe-inline'; style-src …`).
 */
export function parsePolicy(value: string): Directives {
  const directives: Directives = {}
  for (const directive of value.split(";").map((part) => part.trim()).filter(Boolean)) {
    const [rawName, ...sources] = directive.split(/\s+/)
    const name = rawName.toLowerCase()
    if (Object.hasOwn(directives, name)) {
      throw new Error(`the policy repeats ${name}; a browser would apply only its first copy: ${value}`)
    }
    directives[name] = sources
  }
  return directives
}

export function cspHeaders(webConfig: string): CspHeader[] {
  const configuration = new DOMParser().parseFromString(webConfig, "application/xml")
  if (configuration.querySelector("parsererror")) throw new Error("web.config is not well-formed XML")
  const read = (scope: Element, location: string | null) =>
    [...scope.querySelectorAll(":scope > system\\.webServer > httpProtocol > customHeaders > add")]
      .filter((add) => /^Content-Security-Policy(-Report-Only)?$/.test(add.getAttribute("name") ?? ""))
      .map(
        (add): CspHeader => ({
          location,
          name: add.getAttribute("name") as CspHeader["name"],
          value: add.getAttribute("value") ?? "",
          directives: parsePolicy(add.getAttribute("value") ?? ""),
        })
      )
  const root = configuration.documentElement
  return [
    ...read(root, null),
    ...[...root.querySelectorAll(":scope > location")].flatMap((location) =>
      read(location, location.getAttribute("path"))
    ),
  ]
}

/** The one site-wide header of that name. */
export function siteHeader(webConfig: string, name: CspHeader["name"]) {
  const matches = cspHeaders(webConfig).filter((header) => header.location === null && header.name === name)
  if (matches.length !== 1) throw new Error(`expected one site-wide ${name}, found ${matches.length}`)
  return matches[0]
}

/** A source expression's sha256 for a text, quotes included, as a policy writes it. */
export function sha256Source(text: string) {
  return `'sha256-${createHash("sha256").update(text, "utf8").digest("base64")}'`
}

/** The text of every inline <script> in an HTML document, in order. */
export function inlineScripts(html: string) {
  return [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map((match) => match[1])
}
