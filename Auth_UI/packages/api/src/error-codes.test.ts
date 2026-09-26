import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import { PUBLISHED_ERROR_CODES } from "./error-codes.generated"

/**
 * The client does not decide which codes exist — the API's published list does
 * (docs/api/error-codes.json, ADR 0001). This test reads that file and holds the
 * generated map to it, so a code added, renamed or given a pointer server-side
 * fails here until `pnpm gen:error-codes` has run, instead of reaching a screen
 * as an "unknown" error nobody can trace.
 */
interface PublishedEntry {
  code: string
  source: "catalog" | "transport" | "challenge"
  pointer?: string | null
}

function publishedList(): PublishedEntry[] {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(dir, "docs/api/error-codes.json")
    if (existsSync(candidate)) {
      return (JSON.parse(readFileSync(candidate, "utf8")) as { codes: PublishedEntry[] }).codes
    }
    dir = dirname(dir)
  }
  throw new Error("docs/api/error-codes.json not found above " + process.cwd())
}

describe("PUBLISHED_ERROR_CODES", () => {
  it("is exactly the list the API publishes, pointers included", () => {
    const expected = Object.fromEntries(
      publishedList().map(({ code, source, pointer }) => [code, pointer ?? source])
    )

    expect(PUBLISHED_ERROR_CODES).toEqual(expected)
  })

  it("names a member only as an RFC 6901 pointer in URI-fragment form", () => {
    const pointers = Object.values(PUBLISHED_ERROR_CODES).filter(
      (value) => !["catalog", "transport", "challenge"].includes(value)
    )

    expect(pointers.length).toBeGreaterThan(100)
    for (const pointer of pointers) expect(pointer).toMatch(/^#\/[a-z][A-Za-z0-9]*(\/[A-Za-z0-9]+)*$/)
  })
})
