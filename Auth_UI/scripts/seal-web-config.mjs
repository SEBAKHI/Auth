// Runs after `vite build`, from the application directory. Two jobs, in order:
//
//   GATE - refuse to finish a build whose API origin is still the committed
//          placeholder, so an unconfigured artifact never becomes deployable.
//   SEAL - write the effective origin into dist/web.config's CSP, so the policy
//          cannot drift from the origin baked into the JavaScript bundle.
//
// Why this exists: VITE_API_BASE_URL is baked in at build time and the CSP lives
// in a separate static file. Nothing but a comment used to ask the two to agree,
// and on 2026-08-29 they did not: a build carrying the placeholder reached
// production and every API call failed. Prose asking for a step is not a step.
//
// Tests that genuinely do not need a real origin (the Playwright suites mock the
// API by pathname) opt out explicitly with `pnpm build:test`, which passes
// --allow-placeholder. The exception is named in package.json where a reader
// sees it, rather than hidden in an environment variable.
//
// --dist <dir> (default "dist") names the output folder, relative to the
// application. Only the browser harness passes it (dist-harness, through
// scripts/build-harness.mjs), so a test build never touches the deployable dist.
// Without the flag nothing changes.
//
// The post-conditions (card S02) also hold the CSP's style-src to what ships:
// every CSP header names the API origin where it must; a <location> that allows
// its script by hash matches the built file; every inline style text the CSP
// allows by hash is in the bundle; and the bundle has no <style> site that
// csp/bundle-style-sites.json does not count.

import { createHash } from "node:crypto"
import { existsSync, readFileSync, readdirSync, writeFileSync } from "node:fs"
import { join } from "node:path"
import { loadEnv } from "vite"

/** The origin committed to .env.production and public/web.config. RFC 2606 reserves
 *  example.com for documentation, so it resolves nowhere and cannot silently "work". */
const PLACEHOLDER_ORIGIN = "https://auth.example.com"

const allowPlaceholder = process.argv.includes("--allow-placeholder")
const distFlag = process.argv.indexOf("--dist")
const distName = distFlag === -1 ? "dist" : process.argv[distFlag + 1]
const appDir = process.cwd()
const appName = appDir.split(/[\\/]/).pop()
// One plain folder name inside the application, nothing else: "--dist=x" would
// otherwise be ignored silently, and "../x" would seal outside the app.
if (
  process.argv.some((arg) => arg.startsWith("--dist=")) ||
  !distName ||
  !/^[A-Za-z0-9._-]+$/.test(distName) ||
  distName.includes("..")
) {
  console.error(
    `\nseal-web-config [${appName}] BUILD REFUSED: --dist needs one folder name inside the app, as "--dist <name>"\n`,
  )
  process.exit(1)
}
const distDir = join(appDir, distName)
const webConfigPath = join(distDir, "web.config")

function refuse(headline, detail) {
  console.error(`\nseal-web-config [${appName}] BUILD REFUSED: ${headline}\n`)
  console.error(detail.trim())
  console.error("")
  process.exit(1)
}

const configuredOrigin = `  Set your own origin in a file git does not track:

    ${join(appDir, ".env.production.local")}
      VITE_API_BASE_URL=https://auth.yourdomain.com
      VITE_ACCOUNTS_URL=https://accounts.yourdomain.com

  Vite loads .env.production.local after .env.production, so it wins and the
  committed placeholder stays intact for anyone who forks this repository.

  A build that does not need a reachable API (the Playwright suites mock it)
  should run: pnpm build:test`

// ---------------------------------------------------------------- GATE

const env = loadEnv("production", appDir, "VITE_")
const raw = (env.VITE_API_BASE_URL ?? "").trim()

if (!raw) {
  refuse("VITE_API_BASE_URL is not set", configuredOrigin)
}

let origin
try {
  origin = new URL(raw).origin
} catch {
  refuse(
    `VITE_API_BASE_URL is not an absolute URL: ${raw}`,
    `  An empty or relative value makes new URL() throw inside the API client, which
  swallows it and treats the sign-in request as an authenticated one.\n\n${configuredOrigin}`,
  )
}

if (origin === PLACEHOLDER_ORIGIN && !allowPlaceholder) {
  refuse(
    `VITE_API_BASE_URL is still the placeholder ${PLACEHOLDER_ORIGIN}`,
    configuredOrigin,
  )
}

// ---------------------------------------------------------------- SEAL

if (!existsSync(webConfigPath)) {
  refuse(
    `${distName}/web.config is missing`,
    `  public/web.config is copied into ${distName} by Vite's publicDir. Without it IIS
  serves no CSP and no SPA fallback, so every deep link 404s.`,
  )
}

const sealed = readFileSync(webConfigPath, "utf8").split(PLACEHOLDER_ORIGIN).join(origin)
writeFileSync(webConfigPath, sealed, "utf8")

// ------------------------------------------------- POST-CONDITIONS
//
// Every CSP header is checked, not only the first (card S02): the enforced one
// and its Report-Only twin outside any <location>, and each <location>'s own
// (console's /email-preview.html). A header inside a <location> applies to that
// path only.

/** Every CSP and CSP-Report-Only header, with the <location path> it sits in (null: site-wide). */
function cspHeaders(xml) {
  // Comments blanked (same length, so positions hold): a commented-out <add> is not a header.
  const text = xml.replace(/<!--[\s\S]*?-->/g, (comment) => " ".repeat(comment.length))
  const locations = [...text.matchAll(/<location\s+path="([^"]*)"\s*>([\s\S]*?)<\/location>/g)].map(
    (match) => ({ path: match[1], start: match.index, end: match.index + match[0].length }),
  )
  const attribute = (attributes, name) => new RegExp(`\\b${name}="([^"]*)"`).exec(attributes)?.[1]
  return [...text.matchAll(/<add\b([^>]*)>/g)]
    .map((match) => ({ match, name: attribute(match[1], "name"), policy: attribute(match[1], "value") }))
    .filter(({ name }) => name === "Content-Security-Policy" || name === "Content-Security-Policy-Report-Only")
    .map(({ match, name, policy }) => ({
      name,
      policy: policy ?? "",
      location: locations.find((range) => match.index > range.start && match.index < range.end)?.path ?? null,
    }))
}

/** A directive's sources, split on whitespace; null when the policy lacks it. */
function sourcesOf(policy, directive) {
  const value = new RegExp(`(?:^|;)\\s*${directive}(?:\\s+([^;]*))?(?:;|$)`).exec(policy)
  return value ? (value[1] ?? "").trim().split(/\s+/).filter(Boolean) : null
}

const headers = cspHeaders(sealed)
if (!headers.some((header) => header.name === "Content-Security-Policy" && header.location === null)) {
  refuse(
    `no Content-Security-Policy header found in ${distName}/web.config`,
    "  The seal cannot verify a policy it cannot parse.",
  )
}

for (const header of headers) {
  // A site-wide policy needs the API for fetch and images. A <location>'s own
  // policy (the email preview) fetches nothing, so only img-src names it there.
  const required = header.location === null ? ["connect-src", "img-src"] : ["img-src"]
  for (const directive of required) {
    const sources = sourcesOf(header.policy, directive) ?? []
    if (!sources.includes(origin)) {
      const where = header.location === null ? "" : ` for ${header.location}`
      refuse(
        `${directive} of ${header.name}${where} does not allow ${origin}`,
        `  The browser would block the API there. Directive as sealed:\n    ${directive} ${sources.join(" ")}`,
      )
    }
  }
}

const assetsDir = join(distDir, "assets")
const bundle = existsSync(assetsDir)
  ? readdirSync(assetsDir)
      .filter((file) => file.endsWith(".js"))
      .map((file) => ({ file, text: readFileSync(join(assetsDir, file), "utf8") }))
  : []

// Proves the origin actually reached the bundle, not just the policy. Catches the
// case where .env.production.local exists but Vite never loaded it.
if (!bundle.some(({ text }) => text.includes(origin))) {
  refuse(
    `${origin} is not present in any ${distName}/assets/*.js`,
    `  The CSP allows it but no code calls it, so the environment did not reach the
  build. Rebuild after confirming .env.production.local sits beside package.json.`,
  )
}

// Each <location> policy that allows scripts by hash allows exactly the inline
// scripts of the file it serves - recomputed here from the built file, so a
// checkout that rewrote its line endings cannot ship a hash that matches nothing.
for (const header of headers.filter((candidate) => candidate.location !== null)) {
  const scriptSources = sourcesOf(header.policy, "script-src") ?? []
  // A path-specific policy exists to be narrower, and the only one today frames
  // untrusted HTML (the email preview). Its script-src must name its own
  // scripts by hash and nothing else: 'self', a scheme, a host, 'unsafe-*', or
  // no script-src at all (default-src would then decide) is refused.
  const isHash = (source) => /^'sha256-[A-Za-z0-9+/]+={0,2}'$/.test(source)
  if (!scriptSources.length || !scriptSources.every(isHash)) {
    refuse(
      `script-src of ${header.name} for ${header.location} must be one or more 'sha256-…' sources only, found "${scriptSources.join(" ")}"`,
      "  That document is framed to show untrusted HTML; only its own script, by hash, may run.",
    )
  }
  const allowed = scriptSources
  const file = join(distDir, ...header.location.split("/"))
  if (!existsSync(file)) {
    refuse(
      `${distName}/${header.location} is missing`,
      `  web.config gives it a policy that allows its script by hash, but the build has no such file.`,
    )
  }
  const shipped = [...readFileSync(file, "utf8").matchAll(/<script>([\s\S]*?)<\/script>/g)].map(
    (match) => `'sha256-${createHash("sha256").update(match[1], "utf8").digest("base64")}'`,
  )
  if (shipped.length !== allowed.length || shipped.some((hash) => !allowed.includes(hash))) {
    refuse(
      `the script-src hash for ${header.location} does not match the file`,
      `  ${header.location} as built carries ${shipped.join(" ") || "no inline script"};
  web.config allows ${allowed.join(" ")}. Recompute the hash from public/${header.location}
  (LF line endings, .gitattributes) and update web.config.`,
    )
  }
}

// Inline styles (card S02). The style-src hashes stand for texts the libraries
// inject; each text must really be in the bundle, or the hash allows something
// that never ships while the real text goes unlisted.
const inventoryPath = join(appDir, "csp", "inline-styles.json")
if (!existsSync(inventoryPath)) {
  refuse(
    "csp/inline-styles.json is missing",
    "  It lists the inline <style> texts the bundle injects. Run pnpm csp:collect.",
  )
}
const inventory = JSON.parse(readFileSync(inventoryPath, "utf8"))
for (const style of inventory.styles) {
  if (!style.text) continue
  const forms = [style.text, JSON.stringify(style.text).slice(1, -1)]
  if (!bundle.some(({ text }) => forms.some((form) => text.includes(form)))) {
    refuse(
      `an inline style from csp/inline-styles.json (${style.sources.join(", ")}) is not in the bundle`,
      `  The CSP allows ${style.hash} for it, but no ${distName}/assets/*.js contains its text.
  A library changed, or the source list for this app is wrong. Run pnpm csp:collect.`,
    )
  }
}

// And nothing else creates a <style>: every place the bundle can make one is
// counted, and the count is pinned. A new library or component that injects
// styles changes it, and the build stops until someone decides how that style
// is allowed (a hash, or an adopted stylesheet). Counted: document's and
// React's createElement("style", …), and JSX <style> in any form (jsx, jsxs,
// jsxDEV), whatever its props. Not counted - a tripwire, not a proof: markup
// strings written through innerHTML, and a site removed in the same build that
// adds another. The browser harness (s02-csp-styles.spec.ts) covers those.
const STYLE_SITE = [
  /\bcreateElement\(\s*(["'`])style\1\s*[,)]/g,
  /\bjsx(?:s|DEV)?\)?\(\s*(["'`])style\1\s*,/g,
]
const sites = bundle
  .map(({ file, text }) => ({ file, count: STYLE_SITE.reduce((sum, pattern) => sum + (text.match(pattern)?.length ?? 0), 0) }))
  .filter((entry) => entry.count > 0)
const total = sites.reduce((sum, entry) => sum + entry.count, 0)
const sitesPath = join(appDir, "csp", "bundle-style-sites.json")
const pinned = existsSync(sitesPath) ? JSON.parse(readFileSync(sitesPath, "utf8")) : null
const pinnedTotal = pinned?.sites?.reduce((sum, site) => sum + site.count, 0)
if (!pinned || pinned.count !== pinnedTotal || pinned.count !== total) {
  refuse(
    `the bundle creates <style> elements in ${total} places; csp/bundle-style-sites.json pins ${pinned?.count ?? "nothing"}`,
    `  Per file:\n${sites.map((entry) => `    ${entry.count}  ${distName}/assets/${entry.file}`).join("\n")}
  Find the new site, decide how its style is allowed - a hash in csp/inline-styles.json
  (pnpm csp:collect) or an adopted stylesheet (packages/ui/src/hooks/use-adopted-style-sheet.ts) -
  and only then update the file, naming the site.`,
  )
}

const note = allowPlaceholder ? " (placeholder allowed: test build)" : ""
console.log(`seal-web-config [${appName}] CSP sealed to ${origin}${note}`)
