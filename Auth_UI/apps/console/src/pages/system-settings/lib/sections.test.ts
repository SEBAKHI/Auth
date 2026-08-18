import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import { en } from "@authsystem/i18n/locales/en"

import { fieldI18nKey, SECTION_I18N } from "./sections"

/**
 * The console does not invent its settings sections — the API sends them, from
 * SystemSettingsRegistry. This test reads that C# file and holds the two sides
 * together.
 *
 * The gap it closes is real and was shipped: a section was added to the registry
 * with ten fields, and nothing on this side knew its name. The console rendered
 * the raw key as the card title and left every field unlabelled, and no test
 * failed — the backend suite only knows about C#, the frontend suite only about
 * TypeScript, and the section lives in the seam between them.
 */
/**
 * Walk up from the working directory until the solution root appears. Neither
 * __dirname nor cwd is dependable here: vitest runs from the workspace root,
 * not from this file.
 */
function registryPath(): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(
      dir,
      "Auth/Auth.Application/SystemSettings/SystemSettingsRegistry.cs"
    )
    if (existsSync(candidate)) return candidate
    dir = dirname(dir)
  }
  throw new Error("SystemSettingsRegistry.cs not found above " + process.cwd())
}

const REGISTRY = registryPath()

/** Section keys, in registry order. */
function registrySections(): string[] {
  const source = readFileSync(REGISTRY, "utf8")
  return [...source.matchAll(/^\s*Key:\s*"([A-Za-z]+)",/gm)].map((m) => m[1])
}

/** Field names declared under a given section. */
function registryFields(section: string): string[] {
  const source = readFileSync(REGISTRY, "utf8")
  const start = source.indexOf(`Key: "${section}",`)
  expect(start, `section ${section} not found in the registry`).toBeGreaterThan(-1)

  // Up to the next section, or the end of the array.
  const next = source.indexOf('Key: "', start + 1)
  const block = source.slice(start, next === -1 ? undefined : next)

  return [...block.matchAll(/new SettingFieldDefinition\("([^"]+)"/g)].map((m) => m[1])
}

type Tree = Record<string, unknown>

describe("every settings section the API can send is presentable", () => {
  const sections = registrySections()

  it("finds sections in the registry at all", () => {
    // Guards the regex itself: a refactor that changes the registry's shape
    // must not turn this whole file into a silent no-op.
    expect(sections.length).toBeGreaterThan(10)
    expect(sections).toContain("Jwt")
  })

  it.each(registrySections())("%s has a SECTION_I18N entry", (section) => {
    expect(
      SECTION_I18N[section],
      `Add "${section}" to SECTION_I18N, or the console shows the raw key as the card title`
    ).toBeTruthy()
  })

  it.each(registrySections())("%s has a translated title and description", (section) => {
    const key = SECTION_I18N[section]
    if (!key) return // reported by the test above

    const block = (en.systemSettings as Tree)[key] as Tree | undefined
    expect(block, `systemSettings.${key} is missing from en.ts`).toBeDefined()
    expect(block?.title, `systemSettings.${key}.title is missing`).toBeTruthy()
    expect(block?.description, `systemSettings.${key}.description is missing`).toBeTruthy()
  })
})

describe("every editable field has a label", () => {
  const cases = registrySections().flatMap((section) =>
    registryFields(section).map((field) => ({ section, field }))
  )

  it("finds fields at all", () => {
    expect(cases.length).toBeGreaterThan(50)
  })

  it.each(cases)("$section.$field", ({ section, field }) => {
    const key = SECTION_I18N[section]
    if (!key) return

    const block = (en.systemSettings as Tree)[key] as Tree | undefined
    if (!block) return

    expect(
      block[fieldI18nKey(field)],
      `systemSettings.${key}.${fieldI18nKey(field)} is missing — the field renders with no label`
    ).toBeTruthy()
  })
})
