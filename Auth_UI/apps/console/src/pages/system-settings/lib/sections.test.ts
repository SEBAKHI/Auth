import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import { en } from "@authsystem/i18n/locales/en"

import {
  HIGH_IMPACT_PATHS,
  PROSE_VALUE_PATHS,
  fieldI18nKey,
  isHighImpactPath,
  isProseValuePath,
  valueDirection,
  SECTION_I18N,
} from "./sections"

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

/**
 * Fields declared under a given section, each with the flags its declaration
 * carries. `sensitive` is read rather than ignored because it changes which
 * component renders the row — see the hint test below.
 */
function registryFields(
  section: string
): { name: string; sensitive: boolean }[] {
  const source = readFileSync(REGISTRY, "utf8")
  const start = source.indexOf(`Key: "${section}",`)
  expect(start, `section ${section} not found in the registry`).toBeGreaterThan(
    -1
  )

  // Up to the next section, or the end of the array.
  const next = source.indexOf('Key: "', start + 1)
  const block = source.slice(start, next === -1 ? undefined : next)

  return [
    // `\s*` because two declarations wrap their arguments onto following lines
    // (Gateway:ExemptPaths at SystemSettingsRegistry.cs:136, and
    // ImageStorage:AllowedContentTypes at :464). Without it this parser
    // silently skipped both, and every registry this file cross-checks had
    // two blind spots nobody could see.
    ...block.matchAll(/new SettingFieldDefinition\(\s*"([^"]+)"([^)]*)\)/g),
  ].map((m) => ({ name: m[1], sensitive: m[2].includes("Sensitive: true") }))
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

  it.each(registrySections())(
    "%s has a translated title and description",
    (section) => {
      const key = SECTION_I18N[section]
      if (!key) return // reported by the test above

      const block = (en.systemSettings as Tree)[key] as Tree | undefined
      expect(block, `systemSettings.${key} is missing from en.ts`).toBeDefined()
      expect(
        block?.title,
        `systemSettings.${key}.title is missing`
      ).toBeTruthy()
      expect(
        block?.description,
        `systemSettings.${key}.description is missing`
      ).toBeTruthy()
    }
  )
})

describe("every editable field has a label", () => {
  const cases = registrySections().flatMap((section) =>
    registryFields(section).map((field) => ({
      section,
      field: field.name,
      sensitive: field.sensitive,
    }))
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

  /**
   * A label alone says what a setting is called, never what it does. Every row
   * the console renders carries a hint under it, and eight of them did not,
   * because the check above only ever asked for the label.
   *
   * Sensitive fields are the one exception, and a real one rather than a
   * concession: `SecretFieldRow` replaces the hint with "managed in Secret
   * management" and a link, so a hint written for one would never be rendered.
   */
  it.each(cases.filter((c) => !c.sensitive))(
    "$section.$field has a hint",
    ({ section, field }) => {
      const key = SECTION_I18N[section]
      if (!key) return

      const block = (en.systemSettings as Tree)[key] as Tree | undefined
      if (!block) return

      expect(
        block[`${fieldI18nKey(field)}Hint`],
        `systemSettings.${key}.${fieldI18nKey(field)}Hint is missing — the field renders with a name and no explanation`
      ).toBeTruthy()
    }
  )
})

/**
 * The two path registries are the console's own opinion about settings the
 * backend describes. Their entries are plain strings, so a renamed field or a
 * mistyped path costs nothing at build time and everything at runtime: a
 * confirmation that never appears, or a value that stays pinned left-to-right
 * while somebody types Arabic into it. Neither failure raises anything.
 *
 * So the same C# file the tests above read is read once more, and every claim
 * is held against it.
 */
describe("the path registries name settings that exist", () => {
  // The two registries are iterated SEPARATELY, not merged with a spread.
  // They share no section key today, but a spread would silently drop one
  // side's entries the day they do — a guard test that quietly stops guarding.
  const entries = [
    ...Object.entries(HIGH_IMPACT_PATHS).flatMap(([section, paths]) =>
      paths.map((path) => ({ registry: "HIGH_IMPACT_PATHS", section, path }))
    ),
    ...Object.entries(PROSE_VALUE_PATHS).flatMap(([section, paths]) =>
      paths.map((path) => ({ registry: "PROSE_VALUE_PATHS", section, path }))
    ),
  ]

  it("finds entries at all", () => {
    // Self-guard: a registry emptied by a refactor must not turn this file
    // into a green no-op. 23 today.
    expect(entries.length).toBeGreaterThan(20)
  })

  it.each(entries)(
    "$registry $section:$path exists in the registry",
    ({ section, path }) => {
      expect(
        registrySections(),
        `${section} is not a section the API sends`
      ).toContain(section)
      expect(
        registryFields(section).map((f) => f.name),
        `${section}:${path} is not a field the API sends — a typo here is a silently dead entry`
      ).toContain(path)
    }
  )

  it.each(entries)("$registry $section:$path is editable", ({ section, path }) => {
    // A confirmation or a direction rule on a field the form cannot write is
    // pure noise: sensitive fields render as a link to Secret management.
    const field = registryFields(section).find((f) => f.name === path)
    expect(
      field?.sensitive,
      `${section}:${path} is sensitive — the form never writes it`
    ).toBe(false)
  })

  it("claims no path twice within a section", () => {
    for (const registry of [HIGH_IMPACT_PATHS, PROSE_VALUE_PATHS]) {
      for (const [section, paths] of Object.entries(registry)) {
        expect(new Set(paths).size, `${section} lists a path twice`).toBe(
          paths.length
        )
      }
    }
  })
})

describe("isHighImpactPath / isProseValuePath", () => {
  it("does not match a path from another section", () => {
    // `Enabled` is declared by three sections and `RegisterPermitLimit` by two:
    // a bare-path registry would have gated the wrong rows.
    expect(isHighImpactPath("RateLimiting", "RegisterPermitLimit")).toBe(false)
    expect(isProseValuePath("Email", "SmtpHost")).toBe(false)
    expect(isProseValuePath("DataController", "LegalName")).toBe(true)
    expect(isHighImpactPath("Session", "MaxConcurrentSessions")).toBe(true)
  })

  it("defaults to ltr for an unknown section", () => {
    expect(valueDirection(undefined, "LegalName")).toBe("ltr")
    expect(valueDirection("Jwt", "Issuer")).toBe("ltr")
    expect(valueDirection("DataController", "Address")).toBe("auto")
  })
})
