// Builds both applications for the browser harness (e2e/harness), exactly the way
// `build:test` builds them - tsc -b, vite build, seal-web-config --allow-placeholder -
// with two differences, both there so a harness run can never change what ships:
//
//   OUTPUT  - everything goes to dist-harness. dist, the folder `pnpm build` seals
//             for deployment, is fingerprinted before and after, and a change fails
//             the run. (Card S30a, Q6: a placeholder build left in dist and uploaded
//             without a rebuild would repeat the 2026-08-29 outage.)
//   PINNING - every VITE_ key is pinned to its value in the TRACKED .env.production,
//             so the bundle calls https://auth.example.com - the harness's API host -
//             on every machine, even one whose .env.production.local names a real
//             origin. Vite lets process.env beat every .env file, which is what makes
//             the pin work; a VITE_ key that only an untracked file (or the shell)
//             provides is refused by name rather than silently built in.

import { spawnSync } from "node:child_process"
import { createHash } from "node:crypto"
import { existsSync, readFileSync, readdirSync } from "node:fs"
import { createRequire } from "node:module"
import { dirname, join, relative } from "node:path"
import { fileURLToPath, pathToFileURL } from "node:url"
import { parseEnv } from "node:util"
import { loadEnv } from "vite"

export const HARNESS_OUT_DIR = "dist-harness"
const AUTH_UI = dirname(dirname(fileURLToPath(import.meta.url)))
const APPS = ["console", "accounts"]

/** Untracked env files Vite also reads in production mode, most specific first. */
const UNTRACKED = [".env.production.local", ".env.local", ".env"]

function readEnvFile(path) {
  return existsSync(path) ? parseEnv(readFileSync(path, "utf8")) : {}
}

/**
 * The VITE_ keys of the tracked .env.production, or an Error naming every VITE_
 * key Vite would load from somewhere else and the file (or shell) it came from.
 */
export function pinnedEnvironment(appDir) {
  const trackedPath = join(appDir, ".env.production")
  if (!existsSync(trackedPath)) {
    throw new Error(`build-harness: ${trackedPath} is missing; the harness pins its build to that file.`)
  }
  const tracked = Object.fromEntries(
    Object.entries(readEnvFile(trackedPath)).filter(([key]) => key.startsWith("VITE_"))
  )
  const loaded = loadEnv("production", appDir, "VITE_")
  const extras = Object.keys(loaded).filter((key) => !(key in tracked))
  if (extras.length) {
    const lines = extras.map((key) => {
      const file = UNTRACKED.find((name) => key in readEnvFile(join(appDir, name)))
      return `  ${key} comes from ${file ? join(appDir, file) : "the process environment"}`
    })
    throw new Error(
      `build-harness: VITE_ keys that are not in ${trackedPath}:\n${lines.join("\n")}\n` +
        "The harness pins every VITE_ key to the tracked file so its build is the same on every\n" +
        "machine. Add the key to .env.production (with a placeholder value) or remove it from\n" +
        "where it came from."
    )
  }
  return tracked
}

/** A content hash of a folder, or "(absent)". Used to prove dist is left alone. */
export function fingerprint(dir) {
  if (!existsSync(dir)) return "(absent)"
  const hash = createHash("sha256")
  const walk = (current) => {
    for (const entry of readdirSync(current, { withFileTypes: true }).sort((a, b) =>
      a.name.localeCompare(b.name)
    )) {
      const full = join(current, entry.name)
      if (entry.isDirectory()) walk(full)
      else hash.update(relative(dir, full)).update("\0").update(readFileSync(full)).update("\0")
    }
  }
  walk(dir)
  return hash.digest("hex")
}

function step(appDir, env, args, label) {
  const result = spawnSync(process.execPath, args, { cwd: appDir, env, stdio: "inherit" })
  if (result.status !== 0) {
    throw new Error(`build-harness: ${label} failed in ${appDir} (exit ${result.status ?? result.signal})`)
  }
}

export function buildApp(app) {
  const appDir = join(AUTH_UI, "apps", app)
  const pinned = pinnedEnvironment(appDir)
  const env = { ...process.env, ...pinned }
  // The same binaries `pnpm build:test` runs, found from the app as pnpm finds them.
  const resolveFromApp = createRequire(join(appDir, "package.json")).resolve
  const bin = (name) => {
    const manifestPath = resolveFromApp(`${name}/package.json`)
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"))
    const entry = typeof manifest.bin === "string" ? manifest.bin : manifest.bin[name === "typescript" ? "tsc" : name]
    return join(dirname(manifestPath), entry)
  }
  const before = fingerprint(join(appDir, "dist"))

  step(appDir, env, [bin("typescript"), "-b"], "tsc -b")
  step(appDir, env, [bin("vite"), "build", "--outDir", HARNESS_OUT_DIR], "vite build")
  step(
    appDir,
    env,
    [join(AUTH_UI, "scripts", "seal-web-config.mjs"), "--allow-placeholder", "--dist", HARNESS_OUT_DIR],
    "seal-web-config"
  )

  const after = fingerprint(join(appDir, "dist"))
  if (before !== after) {
    throw new Error(`build-harness: ${app}/dist changed during the harness build; it must never be touched.`)
  }
  console.log(`build-harness [${app}] built ${HARNESS_OUT_DIR}; dist unchanged (${after.slice(0, 12)})`)
}

function main() {
  try {
    for (const app of APPS) buildApp(app)
  } catch (error) {
    console.error(`\n${error instanceof Error ? error.message : String(error)}\n`)
    process.exit(1)
  }
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) main()
