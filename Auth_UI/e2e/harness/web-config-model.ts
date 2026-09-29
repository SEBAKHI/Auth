import { JSDOM } from "jsdom"

/**
 * What IIS does with one application's web.config, reduced to a model the
 * harness can execute, and executed here the same way for every request.
 *
 * The file is read at run time, so the harness never holds a second copy of any
 * policy: the CSP a harness page runs under is the attribute in the file.
 *
 * Only the elements listed in SUPPORTED are understood. Anything else - a new
 * <mimeMap>, a <remove>, a server variable the rules do not use today - throws
 * at load time with its path, because a harness that silently skipped it would
 * go on passing while serving something IIS does not.
 *
 * IIS behaviour the files rely on WITHOUT declaring it is written down here as
 * explicit defaults, each from the URL Rewrite configuration reference
 * (learn.microsoft.com/en-us/iis/extensions/url-rewrite-module/
 * url-rewrite-module-configuration-reference, read 2026-09-18):
 *   - a rule's <match url> sees the path without its leading "/" and without
 *     the query string, and ignores case unless ignoreCase="false";
 *   - Rewrite and Redirect keep the query string unless appendQueryString="false";
 *   - {REQUEST_URI} is the path WITH its leading "/" and its query string;
 *   - a Redirect without redirectType is Permanent (301);
 *   - a request for a directory serves its index.html (defaultDocument), and any
 *     path ending in ".config" is refused with 404 (requestFiltering).
 * That conditions ignore case too is an assumption the reference does not state
 * for conditions (UNVERIFIED-EXTERNAL); it is listed in the README's fidelity limits.
 */

export type ClientCache =
  | { mode: "DisableCache" }
  | { mode: "UseMaxAge"; maxAgeSeconds: number }

export interface Condition {
  input: "{REQUEST_FILENAME}" | "{REQUEST_URI}"
  matchType: "IsFile" | "IsDirectory" | "Pattern"
  pattern?: string
  negate: boolean
  ignoreCase: boolean
}

export type RuleAction =
  | { type: "Rewrite"; url: string; appendQueryString: boolean }
  | { type: "Redirect"; url: string; status: number; appendQueryString: boolean }
  | { type: "None" }
  | {
      type: "CustomResponse"
      status: number
      statusReason: string
      statusDescription: string
    }

export interface Rule {
  name: string
  stopProcessing: boolean
  match: { url: string; ignoreCase: boolean }
  conditions: Condition[]
  action: RuleAction
}

export interface WebConfigModel {
  /** Where the model came from, for error messages. */
  file: string
  rules: Rule[]
  /** customHeaders in document order, sent on every response. */
  headers: Array<[name: string, value: string]>
  clientCache?: ClientCache
  locations: Array<{ path: string; clientCache?: ClientCache }>
}

export class UnsupportedWebConfigError extends Error {}

/**
 * Element path (relative to <configuration>, "location" standing for any
 * <location path>) → the attributes it may carry. Everything else throws.
 */
const SUPPORTED: Record<string, readonly string[]> = {
  "system.webServer": [],
  "system.webServer/rewrite": [],
  "system.webServer/rewrite/rules": [],
  "system.webServer/rewrite/rules/rule": ["name", "stopProcessing"],
  "system.webServer/rewrite/rules/rule/match": ["url", "ignoreCase"],
  "system.webServer/rewrite/rules/rule/conditions": ["logicalGrouping"],
  "system.webServer/rewrite/rules/rule/conditions/add": [
    "input",
    "matchType",
    "pattern",
    "negate",
    "ignoreCase",
  ],
  "system.webServer/rewrite/rules/rule/action": [
    "type",
    "url",
    "redirectType",
    "appendQueryString",
    "statusCode",
    "statusReason",
    "statusDescription",
  ],
  "system.webServer/httpProtocol": [],
  "system.webServer/httpProtocol/customHeaders": [],
  "system.webServer/httpProtocol/customHeaders/add": ["name", "value"],
  "system.webServer/staticContent": [],
  "system.webServer/staticContent/clientCache": [
    "cacheControlMode",
    "cacheControlMaxAge",
  ],
  location: ["path"],
  "location/system.webServer": [],
  "location/system.webServer/staticContent": [],
  "location/system.webServer/staticContent/clientCache": [
    "cacheControlMode",
    "cacheControlMaxAge",
  ],
}

const REDIRECT_STATUS: Record<string, number> = {
  Permanent: 301,
  Found: 302,
  SeeOther: 303,
  Temporary: 307,
}

function unsupported(file: string, what: string): never {
  throw new UnsupportedWebConfigError(
    `${file}: unsupported ${what}. The harness serves only what ` +
      `e2e/harness/web-config-model.ts understands; extend it and update ` +
      `web-config-model.test.ts in the same commit.`
  )
}

function bool(file: string, path: string, value: string | null, fallback: boolean) {
  if (value === null) return fallback
  if (value === "true") return true
  if (value === "false") return false
  return unsupported(file, `value "${value}" at ${path}`)
}

/** "365.00:00:00" (d.hh:mm:ss, as IIS writes a TimeSpan) → seconds. */
function timeSpanSeconds(file: string, path: string, value: string) {
  const parts = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})$/.exec(value)
  if (!parts) return unsupported(file, `cacheControlMaxAge "${value}" at ${path}`)
  const [, days = "0", hours, minutes, seconds] = parts
  return ((Number(days) * 24 + Number(hours)) * 60 + Number(minutes)) * 60 + Number(seconds)
}

function readClientCache(file: string, path: string, element: Element): ClientCache {
  const mode = element.getAttribute("cacheControlMode")
  if (mode === "DisableCache") return { mode }
  if (mode === "UseMaxAge") {
    const age = element.getAttribute("cacheControlMaxAge")
    if (!age) return unsupported(file, `UseMaxAge without cacheControlMaxAge at ${path}`)
    return { mode, maxAgeSeconds: timeSpanSeconds(file, path, age) }
  }
  return unsupported(file, `cacheControlMode "${mode}" at ${path}`)
}

/** Every URL a Rewrite or Redirect names may only use {R:n} back-references. */
function checkActionUrl(file: string, path: string, url: string) {
  const references = url.match(/\{[^}]*\}/g) ?? []
  for (const reference of references) {
    if (!/^\{R:\d+\}$/.test(reference)) {
      unsupported(file, `reference ${reference} in action url at ${path}`)
    }
  }
}

function readRule(file: string, rulePath: string, rule: Element): Rule {
  const name = rule.getAttribute("name") ?? unsupported(file, `rule without a name`)
  const path = `${rulePath}[name=${name}]`
  const match = rule.querySelector(":scope > match")
  const url = match?.getAttribute("url")
  if (!url) return unsupported(file, `rule without <match url> at ${path}`)

  const conditionsElement = rule.querySelector(":scope > conditions")
  const grouping = conditionsElement?.getAttribute("logicalGrouping") ?? "MatchAll"
  if (grouping !== "MatchAll") {
    unsupported(file, `logicalGrouping "${grouping}" at ${path}/conditions`)
  }
  const conditions = [...(conditionsElement?.querySelectorAll(":scope > add") ?? [])].map(
    (add): Condition => {
      const input = add.getAttribute("input")
      const matchType = add.getAttribute("matchType") ?? "Pattern"
      const negate = bool(file, `${path}/conditions/add`, add.getAttribute("negate"), false)
      const ignoreCase = bool(file, `${path}/conditions/add`, add.getAttribute("ignoreCase"), true)
      if ((matchType === "IsFile" || matchType === "IsDirectory") && input === "{REQUEST_FILENAME}") {
        return { input, matchType, negate, ignoreCase }
      }
      const pattern = add.getAttribute("pattern")
      if (matchType === "Pattern" && input === "{REQUEST_URI}" && pattern) {
        return { input, matchType, pattern, negate, ignoreCase }
      }
      return unsupported(
        file,
        `condition input="${input}" matchType="${matchType}" at ${path}/conditions/add`
      )
    }
  )

  const actionElement = rule.querySelector(":scope > action")
  const type = actionElement?.getAttribute("type")
  const actionPath = `${path}/action`
  let action: RuleAction
  const append = () =>
    bool(file, actionPath, actionElement!.getAttribute("appendQueryString"), true)
  if (type === "Rewrite" || type === "Redirect") {
    const target = actionElement!.getAttribute("url")
    if (!target) return unsupported(file, `${type} without url at ${actionPath}`)
    checkActionUrl(file, actionPath, target)
    if (type === "Rewrite") {
      action = { type, url: target, appendQueryString: append() }
    } else {
      const redirectType = actionElement!.getAttribute("redirectType") ?? "Permanent"
      const status = REDIRECT_STATUS[redirectType]
      if (!status) return unsupported(file, `redirectType "${redirectType}" at ${actionPath}`)
      action = { type, url: target, status, appendQueryString: append() }
    }
  } else if (type === "None") {
    action = { type }
  } else if (type === "CustomResponse") {
    const statusCode = actionElement!.getAttribute("statusCode") ?? ""
    if (!/^[1-5]\d\d$/.test(statusCode)) {
      return unsupported(file, `CustomResponse statusCode "${statusCode}" at ${actionPath}`)
    }
    action = {
      type,
      status: Number(statusCode),
      statusReason: actionElement!.getAttribute("statusReason") ?? "",
      statusDescription: actionElement!.getAttribute("statusDescription") ?? "",
    }
  } else {
    return unsupported(file, `action type "${type}" at ${actionPath}`)
  }

  return {
    name,
    stopProcessing: bool(file, path, rule.getAttribute("stopProcessing"), false),
    match: { url, ignoreCase: bool(file, `${path}/match`, match!.getAttribute("ignoreCase"), true) },
    conditions,
    action,
  }
}

/** Walks every element and attribute and refuses what SUPPORTED does not list. */
function checkSupported(file: string, element: Element, path: string) {
  const allowed = SUPPORTED[path]
  if (!allowed) unsupported(file, `element ${path}`)
  for (const attribute of [...element.attributes]) {
    if (!allowed.includes(attribute.name)) {
      unsupported(file, `attribute ${attribute.name} on ${path}`)
    }
  }
  for (const child of [...element.children]) {
    checkSupported(file, child, `${path}/${child.tagName}`)
  }
}

export function parseWebConfig(xml: string, file: string): WebConfigModel {
  const { DOMParser: Parser } = new JSDOM().window
  const document = new Parser().parseFromString(xml, "application/xml")
  if (document.querySelector("parsererror")) {
    unsupported(file, "document: it is not well-formed XML")
  }
  const root = document.documentElement
  if (root.tagName !== "configuration") unsupported(file, `root element ${root.tagName}`)
  for (const attribute of [...root.attributes]) {
    unsupported(file, `attribute ${attribute.name} on configuration`)
  }
  for (const child of [...root.children]) checkSupported(file, child, child.tagName)

  const server = root.querySelector(":scope > system\\.webServer")
  const rules = [
    ...(server?.querySelectorAll(":scope > rewrite > rules > rule") ?? []),
  ].map((rule) => readRule(file, "system.webServer/rewrite/rules/rule", rule))
  const headers = [
    ...(server?.querySelectorAll(":scope > httpProtocol > customHeaders > add") ?? []),
  ].map((add): [string, string] => [add.getAttribute("name") ?? "", add.getAttribute("value") ?? ""])
  const cacheElement = server?.querySelector(":scope > staticContent > clientCache")
  const locations = [...root.querySelectorAll(":scope > location")].map((location) => {
    const path = location.getAttribute("path") ?? ""
    const element = location.querySelector(
      ":scope > system\\.webServer > staticContent > clientCache"
    )
    return {
      path,
      clientCache: element
        ? readClientCache(file, `location[path=${path}]/system.webServer/staticContent/clientCache`, element)
        : undefined,
    }
  })

  return {
    file,
    rules,
    headers,
    clientCache: cacheElement
      ? readClientCache(file, "system.webServer/staticContent/clientCache", cacheElement)
      : undefined,
    locations,
  }
}

// ------------------------------------------------------------- EXECUTION

/** What the physical file system under the site says about a URL path. */
export interface SiteFiles {
  isFile(urlPath: string): boolean
  isDirectory(urlPath: string): boolean
}

export type Outcome =
  /** Serve the file at this URL path (already mapped by rewrite/default document). */
  | { kind: "file"; urlPath: string }
  | { kind: "redirect"; status: number; location: string }
  | { kind: "status"; status: number; reason: string; description: string }

const NOT_FOUND = (description: string): Outcome => ({
  kind: "status",
  status: 404,
  reason: "Not Found",
  description,
})

function substitute(template: string, match: RegExpExecArray) {
  return template.replace(/\{R:(\d+)\}/g, (_, index: string) => match[Number(index)] ?? "")
}

function withQuery(path: string, query: string) {
  return query ? `${path}${path.includes("?") ? "&" : "?"}${query}` : path
}

/**
 * IIS request processing for one GET, as far as the model goes: requestFiltering
 * on the requested path, the rewrite rules in order, then the default document.
 * `rawPath` is the decoded path with its leading "/", `query` without its "?".
 */
export function resolveRequest(
  model: WebConfigModel,
  rawPath: string,
  query: string,
  files: SiteFiles
): Outcome {
  // requestFiltering runs before URL Rewrite and sees the requested URL, so a
  // ".config" path is refused whatever a rule would have made of it.
  if (/\.config$/i.test(rawPath)) return NOT_FOUND("requestFiltering: .config is not served")

  let path = rawPath
  let currentQuery = query
  for (const rule of model.rules) {
    const matched = new RegExp(rule.match.url, rule.match.ignoreCase ? "i" : "").exec(
      path.slice(1)
    )
    if (!matched) continue
    const requestUri = withQuery(path, currentQuery)
    const passes = rule.conditions.every((condition) => {
      let hit: boolean
      if (condition.matchType === "IsFile") hit = files.isFile(path)
      else if (condition.matchType === "IsDirectory") hit = files.isDirectory(path)
      else hit = new RegExp(condition.pattern!, condition.ignoreCase ? "i" : "").test(requestUri)
      return condition.negate ? !hit : hit
    })
    if (!passes) continue

    const action = rule.action
    if (action.type === "CustomResponse") {
      return {
        kind: "status",
        status: action.status,
        reason: action.statusReason,
        description: action.statusDescription,
      }
    }
    if (action.type === "Redirect") {
      const target = substitute(action.url, matched)
      return {
        kind: "redirect",
        status: action.status,
        location: action.appendQueryString ? withQuery(target, currentQuery) : target,
      }
    }
    if (action.type === "Rewrite") {
      const target = new URL(substitute(action.url, matched), "https://site.invalid")
      path = decodeURIComponent(target.pathname)
      const ruleQuery = target.search.slice(1)
      currentQuery = action.appendQueryString
        ? [ruleQuery, currentQuery].filter(Boolean).join("&")
        : ruleQuery
    }
    if (rule.stopProcessing) break
  }

  if (/\.config$/i.test(path)) return NOT_FOUND("requestFiltering: .config is not served")
  if (files.isDirectory(path)) {
    const index = `${path.replace(/\/?$/, "/")}index.html`
    if (files.isFile(index)) return { kind: "file", urlPath: index }
    // IIS would answer 403.14 (directory listing denied) or add a trailing
    // slash first; neither is modelled (README fidelity limits).
    return NOT_FOUND("directory without index.html")
  }
  if (files.isFile(path)) return { kind: "file", urlPath: path }
  return NOT_FOUND("no such file")
}

/** The clientCache that governs a served URL path: the deepest <location> wins. */
export function clientCacheFor(model: WebConfigModel, urlPath: string): ClientCache | undefined {
  const segments = urlPath.replace(/^\//, "").toLowerCase()
  const location = model.locations
    .filter(
      (candidate) =>
        candidate.clientCache &&
        (segments === candidate.path.toLowerCase() ||
          segments.startsWith(`${candidate.path.toLowerCase()}/`))
    )
    .sort((a, b) => b.path.length - a.path.length)[0]
  return location?.clientCache ?? model.clientCache
}

/** The Cache-Control header value IIS writes for a clientCache setting. */
export function cacheControlValue(cache: ClientCache | undefined) {
  if (!cache) return undefined
  return cache.mode === "DisableCache" ? "no-cache" : `max-age=${cache.maxAgeSeconds}`
}

/** The value of one customHeaders entry, read from the model (never a copy). */
export function headerValue(model: WebConfigModel, name: string) {
  return model.headers.find(([header]) => header.toLowerCase() === name.toLowerCase())?.[1]
}

/**
 * Why a built web.config cannot serve the harness topology, or null: its CSP
 * must allow `origin` - the API host the bundle calls - in connect-src and
 * img-src, the two directives seal-web-config.mjs seals.
 */
export function sealedOriginProblem(model: WebConfigModel, origin: string) {
  const csp = headerValue(model, "Content-Security-Policy") ?? ""
  for (const directive of ["connect-src", "img-src"]) {
    const value = new RegExp(`(?:^|;)\\s*${directive}\\s+([^;]*)`).exec(csp)?.[1] ?? ""
    if (!value.split(/\s+/).includes(origin)) {
      return `${model.file} does not name ${origin} in ${directive} (found "${value.trim()}").`
    }
  }
  return null
}
