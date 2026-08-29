import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import { PERMISSIONS } from "./permissions"

/**
 * The console does not decide what the API demands — `PermissionCodes.cs` does.
 * This test reads that C# file and holds the two sides together.
 *
 * The seam is invisible from inside either half. The backend suite only knows
 * about C#, the frontend suite only about TypeScript, and a permission renamed
 * server-side compiles, deploys, and makes a control quietly disappear for
 * everyone — no error, no failing test, no log line. Until this file existed the
 * two lists were kept equal by hand and by memory.
 *
 * What this proves, and what it does not: the two halves know the same codes,
 * and every gate in the console goes through the map. It does NOT prove that a
 * control is gated on the code its own endpoint demands — that relation runs
 * button → endpoint → code, and nothing here follows it. A green run here is not
 * a statement that the gates are right.
 */
function catalogueSource(): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(dir, "Auth/Auth.Domain/Constants/PermissionCodes.cs")
    if (existsSync(candidate)) {
      // The repository has no .gitattributes and mixes line endings, and C#
      // files are commonly written with a BOM. Both would survive into a
      // captured group and turn `apiKeys` into `apiKeys\r`.
      return readFileSync(candidate, "utf8")
        .replace(/^\uFEFF/, "")
        .replace(/\r\n/g, "\n")
    }
    dir = dirname(dir)
  }
  throw new Error("PermissionCodes.cs not found above " + process.cwd())
}

const SOURCE = catalogueSource()

/** `MembersRead` becomes `membersRead`; the catalogue supplies the casing. */
const camel = (name: string) => name[0].toLowerCase() + name.slice(1)

/**
 * Every constant in the catalogue as `[keyPath, code]`, where the key path is
 * the one the console's map is expected to use.
 *
 * Sliced between `public static class` headers rather than by counting braces.
 * A doc comment holding a route template in curly braces unbalances any brace
 * counter, and the catalogue carries such comments; the header pattern is
 * anchored to four spaces of indentation, which a `///` line can never match.
 */
function cataloguePairs(): [string, string][] {
  const headers = [...SOURCE.matchAll(/^ {4}public static class (\w+)$/gm)]

  return headers.flatMap((header, index) => {
    const start = header.index + header[0].length
    const end = headers[index + 1]?.index ?? SOURCE.length
    const body = SOURCE.slice(start, end)

    return [...body.matchAll(/public const string (\w+) = "([^"]+)";/g)].map(
      (constant) =>
        [`${camel(header[1])}.${camel(constant[1])}`, constant[2]] as [
          string,
          string,
        ]
    )
  })
}

/**
 * Organization-scoped codes, which the console deliberately does not mirror.
 *
 * They are satisfied from the `org_perm` claim — see
 * `PermissionRequirementHandler` — and this client reads only the platform
 * `permissions` claim. A gate keyed on one of them would evaluate false for the
 * organization owner who actually holds it, and hide the control from the one
 * person entitled to it. Mirroring them is blocked on the client learning to
 * read that claim, and on answering what it does when the claim is absent
 * because the token predates the membership.
 *
 * Named rather than counted, deliberately: a count teaches whoever meets the
 * failure to raise a number, and the decision this asks for is not a number.
 */
const ORG_SCOPED = [
  "org:apps:manage",
  "org:apps:read",
  "org:members:invite",
  "org:members:manage",
  "org:members:read",
  "org:permissions:manage",
  "org:permissions:read",
  "org:update",
]

/** The console's map, flattened the same way. */
function mirrorPairs(): [string, string][] {
  const map = PERMISSIONS as unknown as Record<string, Record<string, string>>
  return Object.entries(map).flatMap(([group, entries]) =>
    Object.entries(entries).map(
      ([key, code]) => [`${group}.${key}`, code] as [string, string]
    )
  )
}

const byKeyPath = (a: [string, string], b: [string, string]) =>
  a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0

/**
 * Console and shared-package source, as text. The i18n package is excluded: its
 * locale files carry `permission: "Permission"` as a translated label, which the
 * literal scan would otherwise read as an ungated permission string.
 */
const gateSources = import.meta.glob(
  [
    "../../../../apps/**/*.{ts,tsx}",
    "../../../../packages/**/*.{ts,tsx}",
    "!../../../../packages/i18n/**",
    "!../../../../**/*.test.{ts,tsx}",
    "!../../../../**/*.d.ts",
  ],
  { eager: true, query: "?raw", import: "default" }
) as Record<string, string>

const GATE_SOURCE_ENTRIES = Object.entries(gateSources).filter(
  ([path]) => !path.endsWith("/lib/permissions.ts")
)

describe("the console mirror matches the catalogue it mirrors", () => {
  it("reads the C# catalogue at all", () => {
    // Guards the slicing and the file walk. Every assertion below is a
    // comparison of two derived lists, and two empty lists agree perfectly: a
    // pattern that stopped matching would make this file green and prove
    // nothing. `it.each` over an empty array registers no tests and says so to
    // no one, which is the same failure wearing a different hat.
    const pairs = cataloguePairs()
    expect(pairs.length).toBeGreaterThan(45)
    expect(
      new Set(pairs.map(([path]) => path.split(".")[0])).size
    ).toBeGreaterThan(12)
    expect(pairs.map(([, code]) => code)).toContain("users:read")
    expect(mirrorPairs().length).toBeGreaterThan(40)
    expect(GATE_SOURCE_ENTRIES.length).toBeGreaterThan(50)
  })

  it("maps every key to the code the catalogue gives it", () => {
    // Pairs, not two independent sets. Swapping the values of `users.read` and
    // `users.create` leaves both the set of key paths and the set of codes
    // identical, so a set comparison passes while every read gate in the
    // console has quietly started demanding create.
    const expected = cataloguePairs()
      .filter(([path]) => !path.startsWith("org."))
      .sort(byKeyPath)

    expect(mirrorPairs().sort(byKeyPath)).toEqual(expected)
  })

  it("files every code under a group whose other codes share its prefix", () => {
    const groups = new Map<string, string[]>()
    for (const [path, code] of mirrorPairs()) {
      const group = path.split(".")[0]
      groups.set(group, [...(groups.get(group) ?? []), code])
    }

    for (const [group, codes] of groups) {
      const prefix = codes[0].split(/[:.]/)[0]
      const strays = codes.filter((code) => !code.startsWith(prefix))
      expect(
        strays,
        `${group} holds ${strays.join(", ")}, which do not share the group's "${prefix}" prefix — a constant filed under the wrong group reads as correct at every call site`
      ).toEqual([])
    }
  })
})

describe("organization-scoped codes stay out, on purpose", () => {
  it("mirrors no org-scoped code", () => {
    const leaked = mirrorPairs()
      .filter(([, code]) => code.startsWith("org:"))
      .map(([path, code]) => `${path} → ${code}`)

    expect(
      leaked,
      "the client reads only the platform `permissions` claim, so gating on an org-scoped code hides the control from the organization owner who holds it"
    ).toEqual([])
  })

  it("accounts for every org-scoped code the catalogue declares", () => {
    const declared = cataloguePairs()
      .map(([, code]) => code)
      .filter((code) => code.startsWith("org:"))
      .sort()

    expect(
      declared,
      "an org-scoped code appeared that this file does not account for. Decide, do not renumber: mirror it (which needs the client to read the `org_perm` claim first) or list it here with the reason it stays out"
    ).toEqual([...ORG_SCOPED].sort())
  })
})

describe("nothing routes around the map", () => {
  it("gates on no permission the map does not define", () => {
    // The comparison above is blind to this. `hasPermission("users:read")`
    // written as a literal is a gate the map never saw, so a rename that breaks
    // it leaves both lists in perfect agreement and the control still broken.
    const literalGate = [
      /\bhasPermission\(\s*["'`]/,
      /\bhasAnyPermission\(\s*\[?\s*["'`]/,
      /\bpermission\b\s*[:=]\s*\{?\s*["'`]/,
      /\bdeniedPermission\b\s*[:=]\s*\{?\s*["'`]/,
    ]

    const offenders: string[] = []
    for (const [path, source] of GATE_SOURCE_ENTRIES) {
      source.split("\n").forEach((line, index) => {
        if (literalGate.some((pattern) => pattern.test(line))) {
          offenders.push(`${path}:${index + 1} ${line.trim()}`)
        }
      })
    }

    expect(
      offenders,
      "every gate must name its permission through PERMISSIONS, or the guard in this file cannot see it"
    ).toEqual([])
  })

  it("defines no code that gates nothing", () => {
    // The other direction. A mirrored code no control uses is either a feature
    // that was never wired up or a constant left behind by one that was
    // removed, and both read as deliberate.
    const unused = mirrorPairs()
      .filter(
        ([path]) =>
          !GATE_SOURCE_ENTRIES.some(([, source]) =>
            new RegExp(`PERMISSIONS\\.${path.replace(".", "\\.")}\\b`).test(
              source
            )
          )
      )
      .map(([path]) => path)

    expect(
      unused,
      "these codes are mirrored but gate nothing — wire them up or drop them"
    ).toEqual([])
  })
})
