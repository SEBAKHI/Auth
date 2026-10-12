import { spawnSync } from "node:child_process"
import { createHash } from "node:crypto"
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
  // Card S02: the inline-style inventory and the pinned style-site count the
  // seal holds the bundle to. Nothing injects styles in this bundle.
  mkdirSync(join(dir, "csp"))
  writeFileSync(join(dir, "csp", "inline-styles.json"), JSON.stringify({ styles: [] }))
  writeFileSync(join(dir, "csp", "bundle-style-sites.json"), JSON.stringify({ count: 0, sites: [] }))
  return dir
}

const sha = (text: string) => `'sha256-${createHash("sha256").update(text, "utf8").digest("base64")}'`
const RECEIVER = 'parent.postMessage("ready", location.origin)'

/**
 * Card S02's shape: a site-wide CSP, its Report-Only twin, and a <location>
 * with its own CSP for a preview document whose inline script it allows by hash.
 */
function stagedApp(origin: string, overrides: { report?: string; preview?: string; html?: string } = {}) {
  const dir = app(origin)
  const report = overrides.report ?? `default-src 'self'; img-src 'self' ${PLACEHOLDER}; connect-src 'self' ${PLACEHOLDER}`
  const preview =
    overrides.preview ?? `default-src 'none'; script-src ${sha(RECEIVER)}; img-src 'self' data: ${PLACEHOLDER}`
  writeFileSync(
    join(dir, "dist", "web.config"),
    `<?xml version="1.0"?><configuration><system.webServer><httpProtocol><customHeaders>
  <add name="Content-Security-Policy" value="${POLICY}" />
  <add name="Content-Security-Policy-Report-Only" value="${report}" />
</customHeaders></httpProtocol></system.webServer>
<location path="email-preview.html"><system.webServer><httpProtocol><customHeaders>
  <remove name="Content-Security-Policy" />
  <add name="Content-Security-Policy" value="${preview}" />
</customHeaders></httpProtocol></system.webServer></location></configuration>`
  )
  writeFileSync(join(dir, "dist", "email-preview.html"), overrides.html ?? `<!doctype html><script>${RECEIVER}</script>`)
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

  it.each([["--dist", "../outside"], ["--dist=dist-harness"], ["--dist"]])(
    "refuses a malformed --dist (%s %s) instead of guessing",
    (...args) => {
      const dir = app(PLACEHOLDER, "dist-harness")
      const run = seal(dir, "--allow-placeholder", ...args.filter(Boolean))
      expect(run.status).toBe(1)
      expect(run.stderr).toContain("--dist needs one folder name inside the app")
    }
  )
})

describe("seal-web-config.mjs post-conditions (card S02)", () => {
  it("seals all three CSP headers and accepts a matching preview script", () => {
    const dir = stagedApp(REAL)
    const run = seal(dir)
    expect(run.status, run.stderr).toBe(0)
    const sealed = readFileSync(join(dir, "dist", "web.config"), "utf8")
    expect(sealed).not.toContain(PLACEHOLDER)
    expect(sealed.split(REAL).length - 1).toBe(5)
  })

  it("refuses a Report-Only header without the origin in connect-src", () => {
    const run = seal(stagedApp(REAL, { report: `default-src 'self'; img-src 'self' ${PLACEHOLDER}; connect-src 'self'` }))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain(`connect-src of Content-Security-Policy-Report-Only does not allow ${REAL}`)
  })

  it("refuses the preview header without the origin in img-src, and asks for no connect-src there", () => {
    const run = seal(stagedApp(REAL, { preview: `default-src 'none'; script-src ${sha(RECEIVER)}; img-src 'self'` }))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain(`img-src of Content-Security-Policy for email-preview.html does not allow ${REAL}`)
  })

  it("refuses a preview script whose hash does not match the built file", () => {
    const run = seal(stagedApp(REAL, { html: `<!doctype html><script>${RECEIVER}\r\n</script>` }))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain("the script-src hash for email-preview.html does not match the file")
  })

  it("refuses an inline style from csp/inline-styles.json that the bundle does not contain", () => {
    const dir = stagedApp(REAL)
    writeFileSync(
      join(dir, "csp", "inline-styles.json"),
      JSON.stringify({ styles: [{ hash: "sha256-x", sources: ["sonner"], text: "[data-sonner-toaster]{x:y}" }] })
    )
    const run = seal(dir)
    expect(run.status).toBe(1)
    expect(run.stderr).toContain("an inline style from csp/inline-styles.json (sonner) is not in the bundle")
  })

  it("accepts the text when the bundle carries it, literally or escaped as a string", () => {
    const dir = stagedApp(REAL)
    const text = 'p{content:"x"}'
    writeFileSync(join(dir, "csp", "inline-styles.json"), JSON.stringify({ styles: [{ hash: "h", sources: ["s"], text }] }))
    writeFileSync(join(dir, "dist", "assets", "y.js"), `const css = ${JSON.stringify(text)}`)
    expect(seal(dir).status).toBe(0)
  })

  it("refuses a bundle whose <style> sites differ from the pinned count, naming the file", () => {
    const dir = stagedApp(REAL)
    writeFileSync(join(dir, "dist", "assets", "z.js"), 'const s = document.createElement("style")')
    const run = seal(dir)
    expect(run.status).toBe(1)
    expect(run.stderr).toContain("the bundle creates <style> elements in 1 places; csp/bundle-style-sites.json pins 0")
    expect(run.stderr).toMatch(/1\s+dist\/assets\/z\.js/)
    // Pinned, it passes - and a sites list that does not add up is refused too.
    writeFileSync(join(dir, "csp", "bundle-style-sites.json"), JSON.stringify({ count: 1, sites: [{ source: "z", count: 1 }] }))
    expect(seal(dir).status).toBe(0)
    writeFileSync(join(dir, "csp", "bundle-style-sites.json"), JSON.stringify({ count: 1, sites: [] }))
    expect(seal(dir).status).toBe(1)
  })

  it("counts a JSX <style> with dangerouslySetInnerHTML, in the minifier's backtick form", () => {
    const dir = stagedApp(REAL)
    writeFileSync(join(dir, "dist", "assets", "w.js"), "(0,e.jsx)(`style`,{dangerouslySetInnerHTML:{__html:`a{}`}})")
    expect(seal(dir).stderr).toContain("in 1 places")
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
