import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { describe, expect, it } from "vitest"
import { z } from "zod"

const AUTH_UI = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..")

/**
 * The module specifiers a file loads, in source order: its import declarations
 * and its `export … from` re-exports (both evaluate the named module). Block
 * comments are dropped first, so a commented-out import cannot stand in for the
 * real first one; `import type` and `export type` are erased at build time, so
 * they are not counted.
 */
function importsOf(source: string): string[] {
  const code = source.replace(/\/\*[\s\S]*?\*\//g, "")
  return [
    ...code.matchAll(
      /^(?:import|export)(?!\s+type\s)\s+(?:[^"';]*?\s+from\s+)?["']([^"']+)["']/gm
    ),
  ].map((match) => match[1])
}

describe("zod-jitless (card S02 PR B, OI-03)", () => {
  it("counts a re-export as an import, and a type-only import as none", () => {
    expect(
      importsOf('export {} from "./x"\nimport "@authsystem/auth/zod-jitless"')
    ).toEqual(["./x", "@authsystem/auth/zod-jitless"])
    expect(
      importsOf(
        'import type { X } from "y"\nexport type { Y } from "z"\nimport "@authsystem/auth/zod-jitless"\nexport * from "./w"'
      )
    ).toEqual(["@authsystem/auth/zod-jitless", "./w"])
  })

  it("turns zod's JIT off, so zod never probes for eval", async () => {
    await import("./zod-jitless")
    expect(z.config().jitless).toBe(true)
  })

  // The probe runs when the first object schema is built; a module-level schema
  // is built when its module is evaluated, and ES modules are evaluated in the
  // order they are imported. So the setting must be the entry's first import.
  for (const app of ["console", "accounts"]) {
    it(`is the first import of the ${app} entry`, () => {
      const imports = importsOf(
        readFileSync(join(AUTH_UI, "apps", app, "src", "main.tsx"), "utf8")
      )
      expect(imports.length).toBeGreaterThan(3)
      expect(imports[0]).toBe("@authsystem/auth/zod-jitless")
    })
  }
})
