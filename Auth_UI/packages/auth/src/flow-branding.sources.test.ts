import { readdirSync, readFileSync } from "node:fs"
import { join, relative } from "node:path"

import { describe, expect, it } from "vitest"

/**
 * Every page that names an application takes its whole header from one hook,
 * `useFlowBranding`: the name, the logos, the application's appearance, its
 * panel design and the trust marker (owner, 2026-10-29). A page that fetched any
 * of them by itself would drift from the others one fix at a time, and nothing
 * would fail: the organization step did exactly that twice, first without the
 * appearance, then without the design.
 *
 * So no source file but the hook's module may call, or even import, the hooks it
 * is built from. Those are every hook `use-app-branding.ts` exports, read from
 * the file so a new one is guarded the day it is added, and the provider's
 * `useApplicationTheme`. Their own tests are not pages and are not read.
 */

const ROOT = join(__dirname, "../../..")
const HOOK_MODULE = "packages/auth/src/flow-branding.ts"
const FETCH_MODULE = join(__dirname, "use-app-branding.ts")

/** Comments may name a hook in passing; only code counts. */
function code(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|\s)\/\/.*$/gm, "$1")
}

const guarded = [
  ...[...readFileSync(FETCH_MODULE, "utf8").matchAll(/^export function (use[A-Z]\w*)/gm)].map(
    ([, name]) => name
  ),
  "useApplicationTheme",
]

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) {
      return entry.name === "node_modules" || entry.name === "dist" ? [] : sourceFiles(path)
    }
    return /\.tsx?$/.test(entry.name) && !/\.(test|spec)\.tsx?$|\.d\.ts$/.test(entry.name) ? [path] : []
  })
}

const files = ["apps", "packages"].flatMap((top) =>
  readdirSync(join(ROOT, top), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .flatMap((entry) => sourceFiles(join(ROOT, top, entry.name, "src")))
)

const relativePath = (file: string) => relative(ROOT, file).replaceAll("\\", "/")

/** `name(` as a call: not the hook's own `function name(` definition. */
function callsOf(source: string, name: string): number {
  return [...code(source).matchAll(new RegExp(`(?<!function\\s+)\\b${name}\\s*\\(`, "g"))].length
}

/** Names brought in by an import, before any `as`, so an alias cannot hide one. */
function importedNames(source: string): string[] {
  return [...code(source).matchAll(/import\s+(?:type\s+)?\{([^}]*)\}\s*from/g)].flatMap(([, list]) =>
    list
      .split(",")
      .map((specifier) => specifier.trim().replace(/^type\s+/, "").split(/\s+as\s+/)[0])
      .filter(Boolean)
  )
}

describe("the header of every page that names an application", () => {
  it("reads every source file of both apps and every package", () => {
    // Floors, so a path or a pattern that matches nothing cannot pass below.
    expect(files.length).toBeGreaterThan(300)
    const read = files.map(relativePath)
    for (const page of [
      HOOK_MODULE,
      "apps/accounts/src/pages/logout.tsx",
      "apps/accounts/src/pages/signed-out.tsx",
      "apps/accounts/src/pages/sign-in-expired.tsx",
      "apps/accounts/src/pages/create-organization.tsx",
      "packages/auth/src/pages/login.tsx",
    ]) {
      expect(read).toContain(page)
    }
  })

  it("guards every hook the header is built from", () => {
    expect(guarded).toEqual(
      expect.arrayContaining([
        "useAppBranding",
        "useAppTheme",
        "useAppLoginDesign",
        "useAppKnown",
        "useApplicationTheme",
      ])
    )
  })

  it("finds the hook module's own calls", () => {
    const source = readFileSync(join(ROOT, HOOK_MODULE), "utf8")
    for (const name of guarded) {
      expect(callsOf(source, name), name).toBeGreaterThan(0)
    }
  })

  it("lets no other file call or import them", () => {
    const offenders = files
      .filter((file) => relativePath(file) !== HOOK_MODULE)
      .flatMap((file) => {
        const source = readFileSync(file, "utf8")
        const imported = importedNames(source)
        return guarded
          .filter((name) => callsOf(source, name) > 0 || imported.includes(name))
          .map((name) => `${relativePath(file)}: ${name}`)
      })

    expect(offenders).toEqual([])
  })
})
