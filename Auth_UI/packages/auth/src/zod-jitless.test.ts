import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { describe, expect, it } from "vitest"
import { z } from "zod"

const AUTH_UI = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..")

/** The module specifiers of a file's import declarations, in source order. */
function importsOf(source: string): string[] {
  return [
    ...source.matchAll(/^import\s+(?:[^"';]*?\s+from\s+)?["']([^"']+)["']/gm),
  ].map((match) => match[1])
}

describe("zod-jitless (card S02 PR B, OI-03)", () => {
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
