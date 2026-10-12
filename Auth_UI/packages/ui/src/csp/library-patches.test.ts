import { readFileSync } from "node:fs"
import { createRequire } from "node:module"
import { dirname, join, resolve } from "node:path"
import { fileURLToPath } from "node:url"

import { afterEach, beforeEach, describe, expect, it } from "vitest"

/**
 * The two pnpm patches of card S02 (Auth_UI/patches/) are really applied to
 * the packages the applications load. Without them CodeMirror (style-mod)
 * and every dialog's scroll lock (react-style-singleton) inject a computed
 * <style> again, which the strict style-src refuses - and the bundle's
 * style-site count would not notice, because the unpatched libraries keep
 * the same single createElement("style"). So this runs the installed copies.
 *
 * Each package is resolved through the package that depends on it, as the
 * bundler does, so a second, unpatched copy in the store cannot stand in.
 */

const AUTH_UI = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "..")
const MARKER = "AuthSystem patch (card S02)"

function resolveThrough(start: string, ...chain: string[]) {
  let from = start
  for (const name of chain) from = createRequire(from).resolve(name)
  return from
}

/** The installed package folder that contains `file` (its package.json's folder). */
function packageRoot(file: string, name: string) {
  const at = file.lastIndexOf(join("node_modules", ...name.split("/")))
  return file.slice(0, at + join("node_modules", ...name.split("/")).length)
}

const styleMod = resolveThrough(
  join(AUTH_UI, "apps", "console", "package.json"),
  "@uiw/react-codemirror",
  "@codemirror/view",
  "style-mod"
)
const singleton = resolveThrough(
  join(AUTH_UI, "packages", "ui", "package.json"),
  "radix-ui",
  "@radix-ui/react-dialog",
  "react-remove-scroll",
  "react-remove-scroll-bar",
  "react-style-singleton"
)

let sheets: CSSStyleSheet[] = []
beforeEach(() => {
  document.head.innerHTML = ""
  sheets = []
  Object.defineProperty(document, "adoptedStyleSheets", {
    configurable: true,
    get: () => sheets,
    set: (next: CSSStyleSheet[]) => {
      sheets = [...next]
    },
  })
})
afterEach(() => {
  delete (document as { adoptedStyleSheets?: unknown }).adoptedStyleSheets
})

const adoptedText = () => sheets.map((sheet) => [...sheet.cssRules].map((rule) => rule.cssText).join(" ")).join(" ")

describe("pnpm patches (card S02)", () => {
  it("style-mod mounts its rules on the document as an adopted sheet, not a <style>", () => {
    const { StyleModule } = createRequire(import.meta.url)(styleMod) as {
      StyleModule: { new (spec: object): object; mount(root: Document, module: object): void }
    }
    StyleModule.mount(document, new StyleModule({ ".s02-patched": { color: "red" } }))
    expect(document.querySelectorAll("style")).toHaveLength(0)
    expect(adoptedText()).toContain(".s02-patched")
  })

  it("react-style-singleton adopts its computed sheet and takes it off again", () => {
    const { stylesheetSingleton } = createRequire(import.meta.url)(singleton) as {
      stylesheetSingleton: () => { add(css: string): void; remove(): void }
    }
    const sheet = stylesheetSingleton()
    sheet.add("body{margin-right:17px}")
    expect(document.querySelectorAll("style")).toHaveLength(0)
    expect(adoptedText()).toContain("margin-right: 17px")
    sheet.remove()
    expect(sheets).toEqual([])
  })

  it("the builds the bundler reads (ESM) carry the same patch", () => {
    const styleModRoot = packageRoot(styleMod, "style-mod")
    expect(readFileSync(join(styleModRoot, "src", "style-mod.js"), "utf8")).toContain(MARKER)
    const singletonRoot = packageRoot(singleton, "react-style-singleton")
    for (const build of ["es2015", "es2019", "es5"]) {
      expect(readFileSync(join(singletonRoot, "dist", build, "singleton.js"), "utf8"), build).toContain(MARKER)
    }
  })
})
