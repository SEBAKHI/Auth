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

import { existsSync, readFileSync, readdirSync, writeFileSync } from "node:fs"
import { join } from "node:path"
import { loadEnv } from "vite"

/** The origin committed to .env.production and public/web.config. RFC 2606 reserves
 *  example.com for documentation, so it resolves nowhere and cannot silently "work". */
const PLACEHOLDER_ORIGIN = "https://auth.example.com"

const allowPlaceholder = process.argv.includes("--allow-placeholder")
const appDir = process.cwd()
const appName = appDir.split(/[\\/]/).pop()
const distDir = join(appDir, "dist")
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
    "dist/web.config is missing",
    `  public/web.config is copied into dist by Vite's publicDir. Without it IIS
  serves no CSP and no SPA fallback, so every deep link 404s.`,
  )
}

const sealed = readFileSync(webConfigPath, "utf8").split(PLACEHOLDER_ORIGIN).join(origin)
writeFileSync(webConfigPath, sealed, "utf8")

// ------------------------------------------------- POST-CONDITIONS

const csp = /<add\s+name="Content-Security-Policy"\s+value="([^"]*)"/.exec(sealed)?.[1]
if (!csp) {
  refuse(
    "no Content-Security-Policy header found in dist/web.config",
    "  The seal cannot verify a policy it cannot parse.",
  )
}

for (const directive of ["connect-src", "img-src"]) {
  const value = new RegExp(`(?:^|;)\\s*${directive}([^;]*)`).exec(csp)?.[1] ?? ""
  if (!value.includes(origin)) {
    refuse(
      `${directive} does not allow ${origin}`,
      `  The browser would block every request to the API. Directive as sealed:\n    ${directive}${value}`,
    )
  }
}

// Proves the origin actually reached the bundle, not just the policy. Catches the
// case where .env.production.local exists but Vite never loaded it.
const assetsDir = join(distDir, "assets")
const inBundle =
  existsSync(assetsDir) &&
  readdirSync(assetsDir)
    .filter((f) => f.endsWith(".js"))
    .some((f) => readFileSync(join(assetsDir, f), "utf8").includes(origin))

if (!inBundle) {
  refuse(
    `${origin} is not present in any dist/assets/*.js`,
    `  The CSP allows it but no code calls it, so the environment did not reach the
  build. Rebuild after confirming .env.production.local sits beside package.json.`,
  )
}

const note = allowPlaceholder ? " (placeholder allowed: test build)" : ""
console.log(`seal-web-config [${appName}] CSP sealed to ${origin}${note}`)
