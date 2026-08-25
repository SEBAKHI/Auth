import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import { en } from "./en"

/**
 * Holds the `fields` catalogue against the records it names.
 *
 * Auto-discovered columns and detail rows are built from field names no page
 * ever declared, so before this catalogue existed their headings were whatever
 * the raw identifier humanized to — English, in all seven languages. The
 * catalogue only stays useful if it keeps up with the API, and nothing else can
 * notice when it does not: a DTO gaining a property is a backend change, and
 * the property surfaces in the console with no code change at all.
 *
 * So this test does what the console does at runtime — find the row types the
 * tables are built on, read their properties out of the generated schema — and
 * insists every one of them has a name.
 */

const sources = import.meta.glob(
  [
    "../../../../apps/**/*.tsx",
    "../../../../packages/**/*.tsx",
    "!../../../../**/*.test.tsx",
  ],
  { eager: true, query: "?raw", import: "default" }
) as Record<string, string>

/** The generated OpenAPI types, found from wherever vitest was started. */
function schemaSource(): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    for (const relative of [
      "packages/api/src/schema.d.ts",
      "Auth_UI/packages/api/src/schema.d.ts",
    ]) {
      const candidate = join(dir, relative)
      if (existsSync(candidate)) return readFileSync(candidate, "utf8")
    }
    dir = dirname(dir)
  }
  throw new Error("schema.d.ts not found above " + process.cwd())
}

const SCHEMA = schemaSource()

/**
 * Every table row type, from the `ColumnDef<…>` declarations, split by how it
 * was written. A `Schemas["X"]` reference names a DTO and MUST resolve; a bare
 * identifier may be a local alias for one (the pages type most of their tables
 * that way), a locally declared shape, or a generic parameter — so it is used
 * when the schema knows it and ignored when it does not.
 */
function rowTypes(): { referenced: string[]; bare: string[] } {
  const referenced = new Set<string>()
  const bare = new Set<string>()
  for (const source of Object.values(sources)) {
    const aliases = new Map<string, string>()
    for (const match of source.matchAll(/type (\w+) = Schemas\["(\w+)"\]/g)) {
      aliases.set(match[1], match[2])
    }
    for (const match of source.matchAll(
      /ColumnDef<\s*(?:Schemas\["(\w+)"\]|(\w+))/g
    )) {
      if (match[1]) {
        referenced.add(match[1])
        continue
      }
      const local = match[2]
      if (!local) continue
      const aliased = aliases.get(local)
      if (aliased) referenced.add(aliased)
      else bare.add(local)
    }
  }
  return { referenced: [...referenced].sort(), bare: [...bare].sort() }
}

/** Every row type the generated schema can actually describe. */
function rowDtos(): string[] {
  const { referenced, bare } = rowTypes()
  return [...referenced, ...bare.filter((name) => propertiesOf(name) !== null)]
}

/** The property names of one schema component, in declaration order. */
function propertiesOf(dto: string): string[] | null {
  const start = SCHEMA.indexOf(`        ${dto}: {`)
  if (start === -1) return null
  const end = SCHEMA.indexOf("\n        };", start)
  const block = SCHEMA.slice(start, end === -1 ? undefined : end)
  return [...block.matchAll(/^ {12}(\w+)\??:/gm)].map((match) => match[1])
}

/** Mirrors `nameSiblingKey` in the table's field-format helper. */
function nameSiblingKey(key: string): string {
  return `${key.endsWith("Id") ? key.slice(0, -2) : key}Name`
}

/** Mirrors `pairedLabelKey`: a paired id is labelled by the pair's stem. */
function pairedLabelKey(key: string): string {
  return key.endsWith("Id") ? key.slice(0, -2) : key
}

/**
 * Every name the table can ask the catalogue for: each row field, plus the stem
 * of each id whose resolved name sits beside it — those render under the stem.
 */
function requestedKeys(): string[] {
  const keys = new Set<string>()
  for (const dto of rowDtos()) {
    const properties = propertiesOf(dto)
    if (!properties) continue
    const present = new Set(properties)
    for (const key of properties) {
      keys.add(key)
      const sibling = nameSiblingKey(key)
      if (sibling !== key && present.has(sibling)) keys.add(pairedLabelKey(key))
    }
  }
  return [...keys].sort()
}

const fields = en.fields as unknown as Record<string, string>

describe("the field catalogue names every field a table can show", () => {
  it("finds the row types and their properties at all", () => {
    // Guards the globs and the two regexes: if either stops matching, every
    // assertion below would pass over an empty list and prove nothing.
    expect(rowDtos().length).toBeGreaterThan(15)
    expect(requestedKeys().length).toBeGreaterThan(100)
  })

  it("resolves every row type written as a schema reference", () => {
    // A DTO named through `Schemas["…"]` that the schema no longer carries is a
    // rename, and it is invisible until a column shows a raw key on screen.
    const unresolved = rowTypes().referenced.filter(
      (dto) => propertiesOf(dto) === null
    )
    expect(unresolved).toEqual([])
  })

  it("has an English name for every field", () => {
    const missing = requestedKeys().filter((key) => !fields[key])
    expect(missing).toEqual([])
  })

  it("carries no name for a field no table can show", () => {
    // The other direction: a key left behind after a DTO dropped a property
    // would never be read again, in seven files at once.
    const requested = new Set(requestedKeys())
    const orphaned = Object.keys(fields).filter((key) => !requested.has(key))
    expect(orphaned).toEqual([])
  })
})
