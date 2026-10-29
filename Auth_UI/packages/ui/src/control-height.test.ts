import { existsSync, readdirSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import { cn } from "./utils"

/*
 * The platform's control height (Platform settings › Appearance) reaches a
 * control only if its primitive reads `--control-height`: a primitive that
 * writes `h-9` again draws 36px whatever the administrator chose, and nothing
 * else fails. These checks read the primitives' source, so they also catch a
 * primitive added later that nobody thought to list.
 */

/** This package's source folder, found from wherever vitest runs. */
function sourceFolder(): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    for (const candidate of [join(dir, "Auth_UI/packages/ui/src"), join(dir, "packages/ui/src"), join(dir, "src")]) {
      if (existsSync(join(candidate, "button.tsx"))) return candidate
    }
    dir = dirname(dir)
  }
  throw new Error(`packages/ui/src not found above ${process.cwd()}`)
}

const here = sourceFolder()

/** Every top-level primitive (the shadcn components of this package). */
const primitives = readdirSync(here)
  .filter((name) => name.endsWith(".tsx") && !name.includes(".test."))
  .map((name) => ({ name, source: readFileSync(join(here, name), "utf8") }))

/**
 * Primitives whose rows keep their own heights, and why. Anything else that
 * writes the preset's control height is a control the setting misses.
 */
const OWN_HEIGHTS: Record<string, string> = {
  // Navigation rows and the collapse control, not form controls: their
  // heights are fitted to the collapsed icon rail (--sidebar-width-icon) and
  // to each other, and a taller control would spill out of the rail.
  "sidebar.tsx": "sidebar menu rows and trigger",
}

/** The controls the setting is about; each must read the token. */
const CONTROLS = [
  "button.tsx",
  "input.tsx",
  "input-group.tsx",
  "native-select.tsx",
  "select.tsx",
  "tabs.tsx",
  "toggle.tsx",
  "menubar.tsx",
  "pagination.tsx",
]

/** A height class of `n` (Tailwind's 0.25rem steps), with any variant prefix. */
const fixedHeight = (steps: string) =>
  new RegExp(`(?:^|[\\s"'\`:])((?:h|size|min-w)-(?:${steps}))(?![\\w.-])`, "g")

/** "file: class" for every match, for a failure that names what to fix. */
const found = (name: string, source: string, pattern: RegExp) =>
  [...source.matchAll(pattern)].map((match) => `${name}: ${match[1]}`)

describe("the control height reaches every control", () => {
  it("reads the primitives it guards", () => {
    // A floor: a scan that found nothing would pass every check below.
    expect(primitives.length).toBeGreaterThan(30)
    for (const name of CONTROLS) {
      expect(primitives.map((p) => p.name), name).toContain(name)
    }
  })

  it("never writes the preset's control height (h-9) as a primitive's own", () => {
    const offenders = primitives
      .filter(({ name }) => !(name in OWN_HEIGHTS))
      .flatMap(({ name, source }) => found(name, source, fixedHeight("9")))
    expect(offenders).toEqual([])
  })

  it("derives the small and large sizes of a control from it too", () => {
    // sm and lg are a quarter rem either side of --control-height; a fixed
    // h-8 or h-10 would keep its 32 or 40px under every setting.
    const offenders = primitives
      .filter(({ name }) => CONTROLS.includes(name))
      .flatMap(({ name, source }) => found(name, source, fixedHeight("8|10")))
    expect(offenders).toEqual([])
  })

  it("has every control read the token", () => {
    for (const { name, source } of primitives.filter((p) => CONTROLS.includes(p.name))) {
      expect(source, name).toMatch(/(?:h|size)-control\b/)
    }
  })

  it("lays a table row on its token", () => {
    const table = primitives.find((p) => p.name === "table.tsx")!.source
    const cell = table.slice(table.indexOf("function TableCell"))
    expect(cell).toMatch(/\bpy-cell-y\b/)
    expect(cell.slice(0, cell.indexOf("whitespace-nowrap"))).not.toMatch(/\bp-\d/)
  })

  it("lets a caller's height still win over the token", () => {
    // Without the names in utils.ts, tailwind-merge keeps both classes and
    // the stylesheet's order picks the height.
    expect(cn("h-control px-3", "h-8")).toBe("px-3 h-8")
    expect(cn("size-control-sm", "size-6")).toBe("size-6")
    expect(cn("px-3 py-cell-y", "p-0")).toBe("p-0")
  })
})
