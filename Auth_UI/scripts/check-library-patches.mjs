#!/usr/bin/env node
// Refuses a build whose installed libraries do not carry their pnpm patches.
//
// WHY: the two patches in patches/ (card S02) make style-mod (CodeMirror) and
// react-style-singleton (every dialog's scroll lock) adopt their stylesheets
// instead of injecting a <style> element, which the strict style-src refuses.
// An unpatched copy builds, seals and deploys without a sound - the bundle
// still has the same single createElement("style") - and the editor or the
// scroll lock then loses its styles in production. It has happened: a
// node_modules folder named as the patched package held the original files,
// and `pnpm install --frozen-lockfile` answered "Already up to date" (pnpm
// trusts node_modules/.pnpm-workspace-state-v1.json and checks no content).
//
// HOW: every entry of patchedDependencies in pnpm-workspace.yaml names a
// patch file. Each line the patch adds must be present in the patched file of
// every installed copy of that package version under node_modules/.pnpm.
// packages/ui/src/csp/library-patches.test.ts proves the patched behaviour;
// this check only proves the patch is on disk, fast enough for every build.
//
// Usage: node scripts/check-library-patches.mjs [--root <Auth_UI folder>]   (exit 1 on a problem)

import { existsSync, readdirSync, readFileSync } from "node:fs"
import { dirname, join, resolve } from "node:path"
import { fileURLToPath, pathToFileURL } from "node:url"

const AUTH_UI = resolve(dirname(fileURLToPath(import.meta.url)), "..")

/** `name@version: path` entries of the patchedDependencies block of pnpm-workspace.yaml. */
export function readPatchedDependencies(workspaceYaml) {
  const entries = []
  let inBlock = false
  for (const raw of workspaceYaml.replace(/\r\n/g, "\n").split("\n")) {
    if (/^patchedDependencies:\s*$/.test(raw)) {
      inBlock = true
      continue
    }
    if (!inBlock) continue
    if (/^\S/.test(raw)) break
    const match = /^\s+["']?([^"'\s]+)["']?:\s*["']?([^"'\s#]+)["']?/.exec(raw)
    if (!match) continue
    const at = match[1].lastIndexOf("@")
    if (at <= 0) throw new Error(`patchedDependencies entry without a version: ${match[1]}`)
    entries.push({ name: match[1].slice(0, at), version: match[1].slice(at + 1), patch: match[2] })
  }
  return entries
}

/** The files a unified diff changes, each with the non-blank lines it adds. */
export function readPatch(patchText) {
  const files = []
  let current = null
  for (const line of patchText.replace(/\r\n/g, "\n").split("\n")) {
    const header = /^\+\+\+ b\/(.+?)\s*$/.exec(line)
    if (header) {
      current = { path: header[1], added: [] }
      files.push(current)
      continue
    }
    if (current && line.startsWith("+") && !line.startsWith("+++")) {
      const text = line.slice(1).trim()
      if (text) current.added.push(text)
    }
  }
  return files
}

/** Every installed copy of name@version in the virtual store (patched or not). */
export function installedCopies(root, name, version) {
  const store = join(root, "node_modules", ".pnpm")
  if (!existsSync(store)) return []
  const prefix = `${name.replace(/\//g, "+")}@${version}`
  return readdirSync(store)
    .filter((entry) => entry === prefix || entry.startsWith(`${prefix}_`) || entry.startsWith(`${prefix}(`))
    .map((entry) => join(store, entry, "node_modules", ...name.split("/")))
    .filter((dir) => existsSync(dir))
}

/** Problems found under `root` (an Auth_UI folder); empty when every patch is on disk. */
export function checkLibraryPatches(root = AUTH_UI) {
  const problems = []
  const checked = []
  const entries = readPatchedDependencies(readFileSync(join(root, "pnpm-workspace.yaml"), "utf8"))
  for (const { name, version, patch } of entries) {
    const patchPath = join(root, patch)
    if (!existsSync(patchPath)) {
      problems.push(`${name}@${version}: the patch file ${patch} is missing`)
      continue
    }
    const files = readPatch(readFileSync(patchPath, "utf8"))
    if (files.length === 0 || files.every((file) => file.added.length === 0)) {
      problems.push(`${name}@${version}: ${patch} adds no line to check`)
      continue
    }
    const copies = installedCopies(root, name, version)
    if (copies.length === 0) {
      problems.push(`${name}@${version}: not installed under node_modules/.pnpm`)
      continue
    }
    for (const copy of copies) {
      for (const file of files) {
        const target = join(copy, file.path)
        const text = existsSync(target) ? readFileSync(target, "utf8").replace(/\r\n/g, "\n") : null
        const missing = text === null ? file.added : file.added.filter((line) => !text.includes(line))
        if (missing.length > 0) {
          problems.push(
            `${name}@${version}: ${target} lacks ${missing.length} line(s) of ${patch}, e.g. "${missing[0]}"`
          )
        }
      }
      checked.push(copy)
    }
  }
  return { problems, entries: entries.length, checked: checked.length }
}

function main() {
  // --root <folder>: another Auth_UI-shaped folder (tests); the default is this script's own.
  const at = process.argv.indexOf("--root")
  const root = at > 0 ? resolve(process.argv[at + 1]) : AUTH_UI
  const { problems, entries, checked } = checkLibraryPatches(root)
  if (problems.length === 0) {
    console.log(`check-library-patches: ${entries} patch(es) on disk in ${checked} installed copy(ies)`)
    return
  }
  console.error(
    "BUILD REFUSED: an installed library does not carry its pnpm patch (card S02).\n" +
      problems.map((problem) => `  - ${problem}`).join("\n") +
      "\nThe bundle would ship the unpatched library, and the strict style-src would refuse its styles.\n" +
      "Fix, in Auth_UI: delete node_modules/.pnpm-workspace-state-v1.json and the folder named above\n" +
      "(or all of node_modules), then run `pnpm install --frozen-lockfile` and build again."
  )
  process.exit(1)
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) main()
