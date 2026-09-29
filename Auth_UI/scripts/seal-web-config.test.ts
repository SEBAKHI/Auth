import { spawnSync } from "node:child_process"
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { afterEach, describe, expect, it } from "vitest"

import { pinnedEnvironment } from "./build-harness.mjs"

/**
 * The deploy path must not move because the harness exists (card S30a, target
 * 24): seal-web-config.mjs without --dist still seals dist/web.config with a
 * real origin and still refuses the placeholder unless --allow-placeholder is
 * given. And build-harness refuses a VITE_ key that only an untracked file sets.
 */

const SEAL = join(dirname(fileURLToPath(import.meta.url)), "seal-web-config.mjs")
const PLACEHOLDER = "https://auth.example.com"
const REAL = "https://auth.contoso-sample.net"
const POLICY = `default-src 'self'; img-src 'self' data: ${PLACEHOLDER}; connect-src 'self' ${PLACEHOLDER}`
const WEB_CONFIG = `<?xml version="1.0"?><configuration><system.webServer><httpProtocol><customHeaders>
  <add name="Content-Security-Policy" value="${POLICY}" />
</customHeaders></httpProtocol></system.webServer></configuration>`

const made: string[] = []
afterEach(() => {
  for (const dir of made.splice(0)) rmSync(dir, { recursive: true, force: true })
})

/** A throwaway application folder with a built output folder. */
function app(origin: string, out = "dist") {
  const dir = mkdtempSync(join(tmpdir(), "seal-test-"))
  made.push(dir)
  writeFileSync(join(dir, ".env.production"), `VITE_API_BASE_URL=${origin}\n`)
  mkdirSync(join(dir, out, "assets"), { recursive: true })
  writeFileSync(join(dir, out, "web.config"), WEB_CONFIG)
  writeFileSync(join(dir, out, "assets", "x.js"), `const api = "${origin}"`)
  return dir
}

/** Runs the seal as the build does, from the app folder, with no VITE_ variable inherited. */
function seal(cwd: string, ...args: string[]) {
  const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith("VITE_")))
  return spawnSync(process.execPath, [SEAL, ...args], { cwd, env, encoding: "utf8" })
}

function directive(xml: string, name: string) {
  return new RegExp(`${name}([^;"]*)`).exec(xml)?.[1] ?? ""
}

describe("seal-web-config.mjs", () => {
  it("without --dist seals dist/web.config with the real origin", () => {
    const dir = app(REAL)
    const run = seal(dir)
    expect(run.status, run.stderr).toBe(0)
    const sealed = readFileSync(join(dir, "dist", "web.config"), "utf8")
    expect(directive(sealed, "connect-src")).toContain(REAL)
    expect(directive(sealed, "img-src")).toContain(REAL)
    expect(sealed).not.toContain(PLACEHOLDER)
  })

  it("refuses the placeholder origin without --allow-placeholder", () => {
    const dir = app(PLACEHOLDER)
    const run = seal(dir)
    expect(run.status).toBe(1)
    expect(run.stderr).toContain(`VITE_API_BASE_URL is still the placeholder ${PLACEHOLDER}`)
  })

  it("--dist seals only the named folder and leaves dist alone", () => {
    const dir = app(PLACEHOLDER, "dist-harness")
    mkdirSync(join(dir, "dist"))
    writeFileSync(join(dir, "dist", "web.config"), "untouched")
    const run = seal(dir, "--allow-placeholder", "--dist", "dist-harness")
    expect(run.status, run.stderr).toBe(0)
    expect(run.stdout).toContain(`CSP sealed to ${PLACEHOLDER} (placeholder allowed: test build)`)
    expect(readFileSync(join(dir, "dist", "web.config"), "utf8")).toBe("untouched")
    expect(existsSync(join(dir, "dist-harness", "web.config"))).toBe(true)
  })
})

describe("build-harness.mjs key guard", () => {
  it("pins the tracked VITE_ keys when nothing else sets one", () => {
    const dir = app(PLACEHOLDER)
    expect(pinnedEnvironment(dir)).toEqual({ VITE_API_BASE_URL: PLACEHOLDER })
  })

  it("refuses a VITE_ key that only .env.production.local sets, naming key and file", () => {
    const dir = app(PLACEHOLDER)
    writeFileSync(join(dir, ".env.production.local"), `VITE_API_BASE_URL=${REAL}\nVITE_ONLY_LOCAL=1\n`)
    expect(() => pinnedEnvironment(dir)).toThrow(
      /VITE_ONLY_LOCAL comes from .*\.env\.production\.local/
    )
  })
})
