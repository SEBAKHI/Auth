import { describe, expect, it } from "vitest"

import {
  SECTION_BLOCKS,
  requiredCredentials,
  resolveSectionLayout,
} from "./section-layout"
import type { SystemSettingsField } from "./sections"

function field(
  path: string,
  extra: Partial<SystemSettingsField> = {}
): SystemSettingsField {
  return { path, kind: "string", ...extra } as SystemSettingsField
}

/** The ExternalAuth payload, in the order the backend registry declares it. */
const externalAuth = [
  field("Google:Enabled", { kind: "bool" }),
  field("Google:ClientId"),
  field("Apple:Enabled", { kind: "bool" }),
  field("Apple:ServicesId"),
  field("Apple:TeamId"),
  field("Apple:KeyId"),
  field("Apple:PrivateKeyPem", { sensitive: true }),
  field("AvatarImport:Enabled", { kind: "bool" }),
  field("AvatarImport:TimeoutMs", { kind: "int" }),
  field("AvatarImport:MaxBytes", { kind: "int" }),
  field("RequireNonce", { kind: "bool" }),
]

/** The Password payload, in the order the backend registry declares it. */
const password = [
  field("MinimumLength", { kind: "int" }),
  field("RequireUppercase", { kind: "bool" }),
  field("RequireLowercase", { kind: "bool" }),
  field("RequireDigit", { kind: "bool" }),
  field("RequireSpecialCharacter", { kind: "bool" }),
  field("HistoryCount", { kind: "int" }),
  field("MaxFailedAttempts", { kind: "int" }),
  field("LockoutDurationMinutes", { kind: "int" }),
  field("Argon2MemorySize", { kind: "int" }),
  field("Argon2Iterations", { kind: "int" }),
  field("Argon2Parallelism", { kind: "int" }),
  field("SaltSize", { kind: "int", readOnly: true }),
  field("HashSize", { kind: "int", readOnly: true }),
  field("Pepper:Enabled", { kind: "bool" }),
  field("BreachedPasswordCheck:Enabled", { kind: "bool" }),
  field("BreachedPasswordCheck:Mode", { kind: "enum" }),
  field("BreachedPasswordCheck:FailOpen", { kind: "bool" }),
  field("BreachedPasswordCheck:RejectThreshold", { kind: "int" }),
  field("BreachedPasswordCheck:TimeoutMs", { kind: "int" }),
]

const shape = (sectionKey: string, fields: SystemSettingsField[]) =>
  resolveSectionLayout(sectionKey, fields).blocks.map((b) => ({
    kind: b.block.kind,
    name: b.block.kind === "category" ? b.block.switchPath : b.block.key,
    fields: b.fields.map((f) => f.path),
  }))

describe("resolveSectionLayout", () => {
  it("puts each provider's settings behind its own switch", () => {
    expect(shape("ExternalAuth", externalAuth)).toEqual([
      {
        kind: "category",
        name: "Google:Enabled",
        fields: ["Google:ClientId"],
      },
      {
        kind: "category",
        name: "Apple:Enabled",
        fields: [
          "Apple:ServicesId",
          "Apple:TeamId",
          "Apple:KeyId",
          "Apple:PrivateKeyPem",
        ],
      },
    ])
  })

  it("leaves everything that belongs to no block in the general list", () => {
    const { general } = resolveSectionLayout("ExternalAuth", externalAuth)

    expect(general.map((f) => f.path)).toEqual([
      "AvatarImport:Enabled",
      "AvatarImport:TimeoutMs",
      "AvatarImport:MaxBytes",
      "RequireNonce",
    ])
  })

  it("splits the password section into its four questions", () => {
    expect(shape("Password", password)).toEqual([
      {
        kind: "group",
        name: "composition",
        fields: [
          "MinimumLength",
          "RequireUppercase",
          "RequireLowercase",
          "RequireDigit",
          "RequireSpecialCharacter",
          "HistoryCount",
        ],
      },
      {
        kind: "group",
        name: "lockout",
        fields: ["MaxFailedAttempts", "LockoutDurationMinutes"],
      },
      {
        kind: "category",
        name: "BreachedPasswordCheck:Enabled",
        fields: [
          "BreachedPasswordCheck:Mode",
          "BreachedPasswordCheck:FailOpen",
          "BreachedPasswordCheck:RejectThreshold",
          "BreachedPasswordCheck:TimeoutMs",
        ],
      },
      {
        kind: "group",
        name: "hashing",
        fields: [
          "Argon2MemorySize",
          "Argon2Iterations",
          "Argon2Parallelism",
          "SaltSize",
          "HashSize",
          "Pepper:Enabled",
        ],
      },
    ])
  })

  // The password blocks account for all nineteen fields, so an empty general
  // list is the assertion that nothing was dropped on the way.
  it("leaves no password setting unplaced", () => {
    const layout = resolveSectionLayout("Password", password)
    const placed = layout.blocks.flatMap((b) => [
      ...(b.switchField ? [b.switchField] : []),
      ...b.fields,
    ])

    expect(placed).toHaveLength(password.length)
    expect(layout.general).toEqual([])
  })

  it("adopts a newly declared field that falls under a prefix claim", () => {
    const blocks = shape("ExternalAuth", [
      ...externalAuth,
      field("Google:HostedDomain"),
    ])

    expect(blocks[0].fields).toEqual(["Google:ClientId", "Google:HostedDomain"])
  })

  // A console one release behind its backend must degrade to the flat list it
  // rendered before blocks existed, never to a panel with no way to turn its
  // subject on.
  it("drops a category whose switch the payload does not carry", () => {
    const withoutSwitch = externalAuth.filter((f) => f.path !== "Apple:Enabled")
    const { blocks, general } = resolveSectionLayout(
      "ExternalAuth",
      withoutSwitch
    )

    expect(blocks.map((b) => b.block.kind)).toEqual(["category"])
    expect(general.map((f) => f.path)).toContain("Apple:ServicesId")
  })

  it("drops a group left holding nothing rather than drawing an empty heading", () => {
    const onlyLockout = password.filter((f) =>
      ["MaxFailedAttempts", "LockoutDurationMinutes"].includes(f.path ?? "")
    )

    expect(shape("Password", onlyLockout).map((b) => b.name)).toEqual(["lockout"])
  })

  // Session is four settings about one thing; it is on the list of sections
  // that stay flat on purpose, because a heading there would be decoration.
  it("leaves a section with no declared blocks untouched", () => {
    const fields = [
      field("MaxConcurrentSessions", { kind: "int" }),
      field("TerminateOldestOnMax", { kind: "bool" }),
    ]
    const { blocks, general } = resolveSectionLayout("Session", fields)

    expect(blocks).toEqual([])
    expect(general).toEqual(fields)
  })
})

describe("SECTION_BLOCKS", () => {
  it("declares a switch that its own category also claims", () => {
    for (const blocks of Object.values(SECTION_BLOCKS)) {
      for (const block of blocks) {
        if (block.kind !== "category") continue
        const claims = block.claims.some((claim) =>
          claim.endsWith(":")
            ? block.switchPath.startsWith(claim)
            : claim === block.switchPath
        )
        expect(claims, `${block.switchPath} is outside its own claims`).toBe(true)
      }
    }
  })

  // Two blocks reaching for the same path is not a crash - the first simply
  // wins and the second silently loses a row. Only a test can see it.
  it("never lets two blocks in a section claim the same path", () => {
    for (const [section, blocks] of Object.entries(SECTION_BLOCKS)) {
      const seen = new Set<string>()
      for (const block of blocks) {
        for (const claim of block.claims) {
          expect(seen.has(claim), `${section}: ${claim} claimed twice`).toBe(false)
          seen.add(claim)
        }
      }
    }
  })
})

describe("requiredCredentials", () => {
  it("counts only the values this form can read", () => {
    const apple = resolveSectionLayout("ExternalAuth", externalAuth).blocks[1]

    // PrivateKeyPem is secret-owned: its value lives in Secret management, so
    // whether it is set is not knowable from here.
    expect(requiredCredentials(apple.fields).map((f) => f.path)).toEqual([
      "Apple:ServicesId",
      "Apple:TeamId",
      "Apple:KeyId",
    ])
  })

  it("excludes booleans, for which empty is a real value", () => {
    expect(
      requiredCredentials([field("A", { kind: "bool" }), field("B")]).map(
        (f) => f.path
      )
    ).toEqual(["B"])
  })
})
