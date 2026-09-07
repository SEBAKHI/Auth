import { describe, expect, it } from "vitest"

import {
  SECTION_FIELD_CATEGORIES,
  categorizeFields,
  requiredCredentials,
} from "./field-categories"
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

describe("categorizeFields", () => {
  it("puts each provider's settings behind its own switch", () => {
    const { categories } = categorizeFields("ExternalAuth", externalAuth)

    expect(categories.map((c) => c.category.prefix)).toEqual(["Google", "Apple"])
    expect(categories[0].switchField.path).toBe("Google:Enabled")
    expect(categories[0].fields.map((f) => f.path)).toEqual(["Google:ClientId"])
    expect(categories[1].fields.map((f) => f.path)).toEqual([
      "Apple:ServicesId",
      "Apple:TeamId",
      "Apple:KeyId",
      "Apple:PrivateKeyPem",
    ])
  })

  it("leaves everything that belongs to no provider in the general list", () => {
    const { general } = categorizeFields("ExternalAuth", externalAuth)

    expect(general.map((f) => f.path)).toEqual([
      "AvatarImport:Enabled",
      "AvatarImport:TimeoutMs",
      "AvatarImport:MaxBytes",
      "RequireNonce",
    ])
  })

  it("adopts a newly declared provider field without a code change", () => {
    const { categories } = categorizeFields("ExternalAuth", [
      ...externalAuth,
      field("Google:HostedDomain"),
    ])

    expect(categories[0].fields.map((f) => f.path)).toEqual([
      "Google:ClientId",
      "Google:HostedDomain",
    ])
  })

  // A console one release behind its backend must degrade to the flat list it
  // rendered before categories existed, never to a panel with no way to turn
  // the provider on.
  it("drops a category whose switch the payload does not carry", () => {
    const withoutSwitch = externalAuth.filter((f) => f.path !== "Apple:Enabled")
    const { categories, general } = categorizeFields("ExternalAuth", withoutSwitch)

    expect(categories.map((c) => c.category.prefix)).toEqual(["Google"])
    expect(general.map((f) => f.path)).toContain("Apple:ServicesId")
  })

  it("leaves a section with no declared categories untouched", () => {
    const fields = [field("Enabled", { kind: "bool" }), field("Host")]
    const { categories, general } = categorizeFields("Email", fields)

    expect(categories).toEqual([])
    expect(general).toEqual(fields)
  })

  it("declares a switch that is inside its own category's prefix", () => {
    for (const [, categories] of Object.entries(SECTION_FIELD_CATEGORIES)) {
      for (const category of categories) {
        expect(category.switchPath.startsWith(`${category.prefix}:`)).toBe(true)
      }
    }
  })
})

describe("requiredCredentials", () => {
  it("counts only the values this form can read", () => {
    const { categories } = categorizeFields("ExternalAuth", externalAuth)
    const apple = requiredCredentials(categories[1].fields)

    // PrivateKeyPem is secret-owned: its value lives in Secret management, so
    // whether it is set is not knowable from here.
    expect(apple.map((f) => f.path)).toEqual([
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
