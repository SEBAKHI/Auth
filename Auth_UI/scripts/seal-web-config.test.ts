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
const POLICY = `default-src 'self'; img-src 'self' data: ${PLACEHOLDER}; font-src 'self' data: ${PLACEHOLDER}; connect-src 'self' ${PLACEHOLDER}`
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
function stagedApp(
  origin: string,
  overrides: { site?: string; report?: string; preview?: string; html?: string; frame?: string } = {}
) {
  const dir = app(origin)
  const site = overrides.site ?? POLICY
  const report =
    overrides.report ??
    `default-src 'self'; img-src 'self' ${PLACEHOLDER}; font-src 'self' ${PLACEHOLDER}; connect-src 'self' ${PLACEHOLDER}`
  const preview =
    overrides.preview ?? `default-src 'none'; script-src ${sha(RECEIVER)}; img-src 'self' data: ${PLACEHOLDER}`
  writeFileSync(
    join(dir, "dist", "web.config"),
    `<?xml version="1.0"?><configuration><system.webServer><httpProtocol><customHeaders>
  <add name="Content-Security-Policy" value="${site}" />
  <add name="Content-Security-Policy-Report-Only" value="${report}" />
</customHeaders></httpProtocol></system.webServer>
<location path="email-preview.html"><system.webServer><httpProtocol><customHeaders>
  <remove name="Content-Security-Policy" />
  <add name="Content-Security-Policy" value="${preview}" />
</customHeaders></httpProtocol></system.webServer></location>${
      overrides.frame === undefined
        ? ""
        : `
<location path="login-design-frame.html"><system.webServer><httpProtocol><customHeaders>
  <remove name="Content-Security-Policy" />
  <add name="Content-Security-Policy" value="${overrides.frame}" />
</customHeaders></httpProtocol></system.webServer></location>`
    }</configuration>`
  )
  writeFileSync(join(dir, "dist", "email-preview.html"), overrides.html ?? `<!doctype html><script>${RECEIVER}</script>`)
  writeFileSync(join(dir, "dist", "login-design-frame.html"), `<!doctype html><script>${RECEIVER}</script>`)
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
    expect(sealed.split(REAL).length - 1).toBe(7)
  })

  it("refuses a site policy without the origin in font-src: the platform's fonts are served by the API", () => {
    const run = seal(
      stagedApp(REAL, {
        report: `default-src 'self'; img-src 'self' ${PLACEHOLDER}; font-src 'self' data:; connect-src 'self' ${PLACEHOLDER}`,
      })
    )
    expect(run.status).toBe(1)
    expect(run.stderr).toContain(`font-src of Content-Security-Policy-Report-Only does not allow ${REAL}`)
  })

  it("asks the sign-in page design frame for the origin in font-src, and the email preview for none", () => {
    const frame = (fonts: string) =>
      `default-src 'none'; script-src ${sha(RECEIVER)}; font-src 'self' data:${fonts}; img-src https: data:`
    const refused = seal(stagedApp(REAL, { frame: frame("") }))
    expect(refused.status).toBe(1)
    expect(refused.stderr).toContain(
      `font-src of Content-Security-Policy for login-design-frame.html does not allow ${REAL}`
    )
    const accepted = seal(stagedApp(REAL, { frame: frame(` ${PLACEHOLDER}`) }))
    expect(accepted.status, accepted.stderr).toBe(0)
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

  const HASHED = `style-src 'self' ${sha("p{}")}`

  it.each([
    ["'unsafe-inline' in style-src", `${POLICY}; style-src 'self' 'unsafe-inline'`, "'unsafe-inline'"],
    ["'unsafe-hashes' in style-src", `${POLICY}; ${HASHED} 'Unsafe-Hashes'`, "'unsafe-hashes'"],
    ["a style-src-elem beside it", `${POLICY}; ${HASHED}; style-src-elem 'self' 'unsafe-inline'`, "style-src-elem"],
    ["a style-src-attr beside it", `${POLICY}; ${HASHED}; style-src-attr 'unsafe-inline'`, "style-src-attr"],
  ])("refuses a site-wide policy that loosens style-src: %s (S02 stage B)", (_, site, named) => {
    const run = seal(stagedApp(REAL, { site }))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain(`the site-wide Content-Security-Policy loosens style-src: ${named}`)
    expect(run.stderr).toContain("re-uploading the previous dist in full")
  })

  it("refuses the same in a site-wide Report-Only header, and leaves a frame's own policy alone", () => {
    const report = `default-src 'self'; img-src 'self' ${PLACEHOLDER}; connect-src 'self' ${PLACEHOLDER}; style-src 'self' 'unsafe-inline'`
    const refused = seal(stagedApp(REAL, { report }))
    expect(refused.status).toBe(1)
    expect(refused.stderr).toContain("the site-wide Content-Security-Policy-Report-Only loosens style-src: 'unsafe-inline'")

    const frame = `default-src 'none'; script-src ${sha(RECEIVER)}; style-src 'unsafe-inline'; style-src-attr 'unsafe-inline'; font-src 'self' data: ${PLACEHOLDER}; img-src https: data:`
    const accepted = seal(stagedApp(REAL, { site: `${POLICY}; ${HASHED}`, frame }))
    expect(accepted.status, accepted.stderr).toBe(0)
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

  it("counts React's createElement(\"style\", props, text) and a JSX <style> with children or later props", () => {
    const dir = stagedApp(REAL)
    writeFileSync(
      join(dir, "dist", "assets", "v.js"),
      'i.createElement(`style`,null,b);(0,t.jsxs)("style",{id:"x",children:c});(0,t.jsx)(\'style\',{nonce:n,dangerouslySetInnerHTML:{__html:d}})'
    )
    expect(seal(dir).stderr).toContain("in 3 places")
  })

  it("does not read a commented-out header, and reads value before name", () => {
    const dir = stagedApp(REAL)
    const file = join(dir, "dist", "web.config")
    const xml = readFileSync(file, "utf8")
      .replace('<add name="Content-Security-Policy-Report-Only" value="', '<!-- <add name="Content-Security-Policy-Report-Only" value="default-src \'self\'" /> --><add name="Content-Security-Policy-Report-Only" value="')
      .replace(`<add name="Content-Security-Policy" value="${POLICY}" />`, `<add value="${POLICY}" name="Content-Security-Policy" />`)
    // Floor: both rewrites happened, or this test proves nothing.
    expect(xml).toContain("<!-- <add name=\"Content-Security-Policy-Report-Only\"")
    expect(xml).toContain('<add value="')
    writeFileSync(file, xml)
    const run = seal(dir)
    expect(run.status, run.stderr).toBe(0)
    // The commented-out header (no API origin) was not read: had it been, the
    // Report-Only connect-src check would have refused the build.
  })

  // A <location> policy frames untrusted HTML: its script-src is its own
  // scripts by hash, and nothing else (review F4).
  it.each([
    ["lets inline scripts run", `script-src 'unsafe-inline';`],
    ["adds 'self' beside the hash", `script-src ${sha(RECEIVER)} 'self';`],
    ["adds a scheme beside the hash", `script-src ${sha(RECEIVER)} https:;`],
    ["has no script-src at all", ""],
    ["has an empty script-src", "script-src;"],
  ])("refuses a <location> policy that %s", (_, scriptSrc) => {
    const run = seal(stagedApp(REAL, { preview: `default-src 'none'; ${scriptSrc} img-src 'self' data: ${PLACEHOLDER}` }))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain(
      "script-src of Content-Security-Policy for email-preview.html must be one or more 'sha256-…' sources only"
    )
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

/**
 * The account bar (<location path="embed">): pages on the IAM site's subdomains may frame
 * it, and the seal writes the site into frame-ancestors. A value broader than one
 * registrable domain would let any site frame the bar, so the build stops instead.
 */
describe("seal-web-config.mjs account bar site", () => {
  const SITE_POLICY = `${POLICY}; frame-ancestors 'none'`
  const EMBED = `default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data: ${PLACEHOLDER}; font-src 'self' data: ${PLACEHOLDER}; connect-src ${PLACEHOLDER}; frame-ancestors https://*.example.com`
  const RULE = `<rewrite><rules><rule name="Embed Unknown Path" stopProcessing="true"><match url="^embed(/.*)?$" /></rule></rules></rewrite>`

  function barApp(site: string | null, overrides: { embed?: string; site?: string; rule?: string; accounts?: string } = {}) {
    const dir = app(REAL)
    const env = [`VITE_API_BASE_URL=${REAL}`, `VITE_ACCOUNTS_URL=${overrides.accounts ?? "https://accounts.contoso-sample.net"}`]
    if (site !== null) env.push(`VITE_IAM_SITE=${site}`)
    writeFileSync(join(dir, ".env.production"), `${env.join("\n")}\n`)
    writeFileSync(
      join(dir, "dist", "web.config"),
      `<?xml version="1.0"?><configuration><system.webServer>${overrides.rule ?? RULE}<httpProtocol><customHeaders>
  <add name="Content-Security-Policy" value="${overrides.site ?? SITE_POLICY}" />
</customHeaders></httpProtocol></system.webServer>
<location path="embed"><system.webServer><httpProtocol><customHeaders>
  <remove name="Content-Security-Policy" />
  <add name="Content-Security-Policy" value="${overrides.embed ?? EMBED}" />
</customHeaders></httpProtocol></system.webServer></location></configuration>`
    )
    return dir
  }

  it("writes the site into the bar's frame-ancestors, and leaves the rest of the site unframable", () => {
    const dir = barApp("contoso-sample.net")
    const run = seal(dir)
    expect(run.status, run.stderr).toBe(0)
    const sealed = readFileSync(join(dir, "dist", "web.config"), "utf8")
    expect(sealed).toContain("frame-ancestors https://*.contoso-sample.net")
    expect(sealed).not.toContain("*.example.com")
    expect(sealed).toContain("frame-ancestors 'none'")
    expect(run.stdout).toContain("account bar framed by https://*.contoso-sample.net")
  })

  it("refuses a narrower site that leaves the API outside it", () => {
    const run = seal(barApp("iam.contoso-sample.net", { accounts: "https://accounts.iam.contoso-sample.net" }))
    // The API (auth.contoso-sample.net) is outside iam.contoso-sample.net.
    expect(run.status).toBe(1)
    expect(run.stderr).toContain("does not hold auth.contoso-sample.net")
  })

  it.each([
    ["is missing", null, "VITE_IAM_SITE is not set"],
    ["is a bare wildcard", "*", "must be a host name"],
    ["is a wildcard pattern", "*.contoso-sample.net", "must be a host name"],
    ["carries a scheme", "https://contoso-sample.net", "must be a host name"],
    ["is a single label", "net", "must be a host name"],
    ["is a public suffix", "co.uk", "is a public suffix"],
    ["is a private public suffix anyone can register under", "github.io", "is a public suffix"],
    ["holds neither the API nor the accounts app", "example.org", "does not hold auth.contoso-sample.net and accounts.contoso-sample.net"],
  ])("refuses a site that %s", (_, site, message) => {
    const run = seal(barApp(site))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain(message)
  })

  it("refuses an embed script-src beyond 'self'", () => {
    const run = seal(barApp("contoso-sample.net", { embed: EMBED.replace("script-src 'self'", "script-src 'self' 'unsafe-inline'") }))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain("script-src of Content-Security-Policy for embed must be 'self' alone")
  })

  it("refuses an embed frame-ancestors beyond the site", () => {
    const run = seal(barApp("contoso-sample.net", { embed: EMBED.replace("frame-ancestors https://*.example.com", "frame-ancestors https://*.example.com https:") }))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain("frame-ancestors of Content-Security-Policy for embed must be https://*.contoso-sample.net alone")
  })

  it("refuses a site policy that lets every page be framed", () => {
    const run = seal(barApp("contoso-sample.net", { site: POLICY }))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain("must keep frame-ancestors 'none'")
  })

  it("still checks the bar when its <location> carries another attribute", () => {
    const dir = barApp("*")
    const file = join(dir, "dist", "web.config")
    writeFileSync(file, readFileSync(file, "utf8").replace('<location path="embed">', '<location path="embed" inheritInChildApplications="false">'))
    const run = seal(dir)
    expect(run.status).toBe(1)
    expect(run.stderr).toContain("must be a host name")
  })

  it("still checks the bar when the build ships its frames but web.config lost the <location>", () => {
    const dir = barApp("contoso-sample.net")
    const file = join(dir, "dist", "web.config")
    const xml = readFileSync(file, "utf8")
    writeFileSync(file, xml.slice(0, xml.indexOf("<location")) + "</configuration>")
    mkdirSync(join(dir, "dist", "embed", "v1"), { recursive: true })
    writeFileSync(join(dir, "dist", "embed", "v1", "bar.html"), "<!doctype html>")
    const run = seal(dir)
    expect(run.status).toBe(1)
    expect(run.stderr).toContain('<location path="embed"> has no Content-Security-Policy of its own')
  })

  it("refuses any other policy that lets another site frame a page", () => {
    // A console-like app, no account bar at all: its site policy may not open framing either.
    const dir = app(REAL)
    const file = join(dir, "dist", "web.config")
    writeFileSync(file, readFileSync(file, "utf8").replace(POLICY, `${POLICY}; frame-ancestors https://*.contoso-sample.net`))
    const run = seal(dir)
    expect(run.status).toBe(1)
    expect(run.stderr).toContain("frame-ancestors of Content-Security-Policy allows")
  })

  it("refuses a web.config whose SPA fallback could serve the app under /embed", () => {
    const run = seal(barApp("contoso-sample.net", { rule: "" }))
    expect(run.status).toBe(1)
    expect(run.stderr).toContain('the "Embed Unknown Path" rewrite rule is missing')
  })
})
