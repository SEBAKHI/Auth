import { spawnSync } from "node:child_process"
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { afterEach, describe, expect, it } from "vitest"

import { checkLibraryPatches, readPatch, readPatchedDependencies } from "./check-library-patches.mjs"

const SCRIPTS = dirname(fileURLToPath(import.meta.url))
const AUTH_UI = join(SCRIPTS, "..")

const PATCH = [
  "diff --git a/src/lib.js b/src/lib.js",
  "--- a/src/lib.js",
  "+++ b/src/lib.js\t",
  "@@ -1,3 +1,4 @@",
  " export function mount(root) {",
  "-  if (!root.head && root.adopted) {",
  "+  // Example patch (test fixture)",
  "+  if (root.adopted) {",
  "",
].join("\n")
const ORIGINAL = "export function mount(root) {\n  if (!root.head && root.adopted) {\n  }\n}\n"
const PATCHED = "export function mount(root) {\n  // Example patch (test fixture)\n  if (root.adopted) {\n  }\n}\n"

const roots: string[] = []
afterEach(() => {
  for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true })
})

/** An Auth_UI-shaped folder with one patched dependency, installed as `installs` describes. */
function fixture(installs: { folder: string; source: string | null }[], name = "lib") {
  const root = mkdtempSync(join(tmpdir(), "patch-check-"))
  roots.push(root)
  writeFileSync(join(root, "pnpm-workspace.yaml"), `packages:\n  - apps/*\npatchedDependencies:\n  ${name}@1.0.0: patches/lib.patch\n`)
  mkdirSync(join(root, "patches"))
  writeFileSync(join(root, "patches", "lib.patch"), PATCH)
  for (const { folder, source } of installs) {
    const dir = join(root, "node_modules", ".pnpm", folder, "node_modules", ...name.split("/"), "src")
    mkdirSync(dir, { recursive: true })
    if (source !== null) writeFileSync(join(dir, "lib.js"), source)
  }
  return root
}

describe("check-library-patches (card S02 follow-up)", () => {
  it("reads the patched dependencies and the lines a patch adds", () => {
    expect(
      readPatchedDependencies("packages:\n  - a\npatchedDependencies:\n  x@1.0.0: patches/x.patch\n  '@s/y@2.1.0': patches/y.patch\nother: 1\n")
    ).toEqual([
      { name: "x", version: "1.0.0", patch: "patches/x.patch" },
      { name: "@s/y", version: "2.1.0", patch: "patches/y.patch" },
    ])
    expect(readPatch(PATCH)).toEqual([
      { path: "src/lib.js", added: ["// Example patch (test fixture)", "if (root.adopted) {"] },
    ])
  })

  it("passes when every installed copy carries the patch", () => {
    const root = fixture([{ folder: "lib@1.0.0_patch_hash=abc", source: PATCHED }])
    expect(checkLibraryPatches(root)).toEqual({ problems: [], entries: 1, checked: 1 })
  })

  it("refuses a copy named as patched that holds the original file", () => {
    const root = fixture([{ folder: "lib@1.0.0_patch_hash=abc", source: ORIGINAL }])
    const { problems } = checkLibraryPatches(root)
    expect(problems).toHaveLength(1)
    expect(problems[0]).toMatch(/lib@1\.0\.0: .*lib\.js lacks 2 line\(s\) of patches\/lib\.patch/)
  })

  it("refuses an unpatched second copy beside a patched one", () => {
    const root = fixture([
      { folder: "lib@1.0.0_patch_hash=abc", source: PATCHED },
      { folder: "lib@1.0.0", source: ORIGINAL },
    ])
    expect(checkLibraryPatches(root).problems).toHaveLength(1)
  })

  it("refuses a missing file, a missing install, and ignores another version", () => {
    expect(checkLibraryPatches(fixture([{ folder: "lib@1.0.0_x", source: null }])).problems[0]).toMatch(/lacks 2/)
    expect(checkLibraryPatches(fixture([{ folder: "lib@1.0.01", source: ORIGINAL }])).problems).toEqual([
      "lib@1.0.0: not installed under node_modules/.pnpm",
    ])
  })

  it("finds a scoped package in its encoded folder", () => {
    const root = fixture([{ folder: "@s+lib@1.0.0_patch_hash=abc", source: ORIGINAL }], "@s/lib")
    expect(checkLibraryPatches(root).problems[0]).toMatch(/^@s\/lib@1\.0\.0: /)
  })

  it("holds for this workspace's own patches, and refuses the build from the command line", () => {
    const own = checkLibraryPatches(AUTH_UI)
    expect(own.entries).toBeGreaterThanOrEqual(2)
    expect(own.checked).toBeGreaterThanOrEqual(own.entries)
    expect(own.problems).toEqual([])

    const script = join(SCRIPTS, "check-library-patches.mjs")
    const broken = fixture([{ folder: "lib@1.0.0_patch_hash=abc", source: ORIGINAL }])
    const refused = spawnSync(process.execPath, [script, "--root", broken], { encoding: "utf8" })
    expect(refused.status).toBe(1)
    expect(refused.stderr).toContain("BUILD REFUSED")
    expect(refused.stderr).toContain("pnpm install --frozen-lockfile")

    const passed = spawnSync(process.execPath, [script], { encoding: "utf8" })
    expect(passed.status).toBe(0)
    expect(passed.stdout).toMatch(/check-library-patches: \d+ patch\(es\) on disk/)
  })
})
