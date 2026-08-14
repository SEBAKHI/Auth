import { describe, expect, it } from "vitest"
import type { TFunction } from "i18next"

import type { SystemSettingsSection } from "@/pages/system-settings/lib/sections"
import {
  MAX_FIELDS_PER_GROUP,
  buildSearchIndex,
  pathKeywords,
  rankRecords,
  searchIndex,
  type RecordEntry,
} from "./build-index"

/** Stands in for the active locale bundle. */
const LABELS: Record<string, string> = {
  "systemSettings.password.title": "Password",
  "systemSettings.password.description": "Password rules and hashing.",
  "systemSettings.groups.security": "Security",
  "nav.systemSettings": "System Settings",
  "nav.platform": "Platform",
  "nav.profile": "Profile",
  "nav.notifications": "Notifications",
  "systemSettings.password.minimumLength": "Minimum length",
  "systemSettings.password.minimumLengthHint":
    "The fewest characters a new password may have.",
  "systemSettings.password.argon2MemorySize": "Argon2 memory size",
  // A label that shares no word with its config path, so a hit on the path is
  // provably a hit on the path.
  "systemSettings.password.maxAgeDays": "Expiry",
  "platformSettings.title": "Platform settings",
  "platformSettings.subtitle": "Control the platform branding.",
  "secrets.title": "Secrets",
  "secrets.subtitle": "Manage signing keys.",
  "profile.accountDetails": "Account details",
  "profile.accountDetailsSubtitle": "Update your name.",
  "profile.sessions": "Sessions",
  "profile.security": "Security",
  "notifications.title": "Notification Templates",
  "notifications.subtitle": "Message content lives in the database.",
  "notifications.layoutsTitle": "Notification Layouts",
  "notifications.layoutsSubtitle": "The shared visual identity.",
  "notifications.outboxTitle": "Delivery Log",
  "notifications.outboxSubtitle": "Every queued message.",
}

const t = ((key: string, options?: { defaultValue?: string }) =>
  LABELS[key] ?? options?.defaultValue ?? "") as unknown as TFunction

const sections: SystemSettingsSection[] = [
  {
    key: "Password",
    group: "security",
    editable: true,
    fields: [
      { path: "MinimumLength", kind: "int" },
      { path: "Argon2MemorySize", kind: "int" },
      { path: "MaxAgeDays", kind: "int" },
    ],
  } as SystemSettingsSection,
]

const allow = () => true
const deny = (permission: string | undefined) => permission === undefined

describe("pathKeywords", () => {
  it("splits a config path into words so key names are searchable", () => {
    // The point: only the active locale's labels are loaded, so an admin on an
    // Arabic console can still find a setting by its key name.
    expect(pathKeywords("Password:Argon2MemorySize")).toBe(
      "password argon2 memory size"
    )
  })
})

describe("buildSearchIndex", () => {
  it("indexes sections as pages and their settings as fields", () => {
    const index = buildSearchIndex(sections, t, allow)

    const section = index.find((e) => e.id === "section:Password")
    expect(section).toMatchObject({
      kind: "surface",
      title: "Password",
      route: "/admin/system-settings/Password",
    })

    const field = index.find((e) => e.id === "Password:MinimumLength")
    expect(field).toMatchObject({
      kind: "field",
      title: "Minimum length",
      description: "The fewest characters a new password may have.",
      sectionTitle: "Password",
      route: "/admin/system-settings/Password?field=MinimumLength",
    })
  })

  it("tells same-named results apart by where they live", () => {
    // "Sessions" is both a profile tab and a system-settings section. Without
    // the trail the two rows are identical and there is no way to pick.
    const sessionsSection: SystemSettingsSection = {
      key: "Session",
      group: "security",
      editable: true,
      fields: [],
    } as SystemSettingsSection
    const index = buildSearchIndex([sessionsSection], t, allow)

    const profileTab = index.find((e) => e.id === "profile-sessions")
    const settingsSection = index.find((e) => e.id === "section:Session")

    expect(profileTab).toMatchObject({ title: "Sessions", trail: ["Profile"] })
    expect(settingsSection).toMatchObject({
      trail: ["System Settings", "Security"],
    })
  })

  it("keeps the trail as crumbs so an all-Latin path cannot flip in Arabic", () => {
    // Joined into one string, a trail of Latin crumbs resolves LTR inside an
    // RTL row and renders back to front. Separate elements each get their own
    // bidi paragraph.
    const index = buildSearchIndex(sections, t, allow)
    const section = index.find((e) => e.id === "section:Password")

    expect(Array.isArray((section as { trail: string[] }).trail)).toBe(true)
  })

  it("heads each field group with the trail to its section", () => {
    const index = buildSearchIndex(sections, t, allow)
    const field = index.find((e) => e.id === "Password:MinimumLength")

    expect(field).toMatchObject({
      sectionTrail: ["System Settings", "Password"],
    })
  })

  it("omits surfaces the user has no permission for", () => {
    const index = buildSearchIndex([], t, deny)

    // Profile surfaces carry no permission and stay; the admin ones go.
    expect(index.map((e) => e.id)).toContain("profile-sessions")
    expect(index.map((e) => e.id)).not.toContain("secrets")
    expect(index.map((e) => e.id)).not.toContain("platform-settings")
  })

  it("still lists the static surfaces before the settings payload arrives", () => {
    expect(buildSearchIndex([], t, allow).length).toBeGreaterThan(0)
  })

  it("gives a section with a companion page no row of its own", () => {
    // Secret management's real controls are on the keys page, which has its own
    // static surface. Indexing the section too would put two near-identical rows
    // one click apart — the thing the trail exists to prevent.
    const secretManagement: SystemSettingsSection = {
      key: "SecretManagement",
      group: "infrastructure",
      editable: false,
      fields: [{ path: "StorageMode", kind: "string" }],
    } as SystemSettingsSection
    const index = buildSearchIndex([secretManagement], t, allow)
    const ids = index.map((e) => e.id)

    expect(ids).not.toContain("section:SecretManagement")
    // Its settings stay findable and still route into the section card.
    expect(ids).toContain("SecretManagement:StorageMode")
    // And the page itself is still one row.
    expect(index.find((e) => e.id === "secrets")).toMatchObject({
      route: "/admin/system-settings/SecretManagement/keys",
    })
  })
})

describe("searchIndex", () => {
  const index = buildSearchIndex(sections, t, allow)

  it("ranks a label match above a hint match", () => {
    const { fieldGroups } = searchIndex(index, "minimum")
    expect(fieldGroups[0].fields[0].title).toBe("Minimum length")
  })

  it("finds a setting by a word from its raw config path", () => {
    const { fieldGroups } = searchIndex(index, "argon2")
    expect(fieldGroups[0].fields.map((f) => f.title)).toContain(
      "Argon2 memory size"
    )
  })

  it("finds a setting by a word only present in its hint", () => {
    const { fieldGroups } = searchIndex(index, "characters")
    expect(fieldGroups[0].fields[0].title).toBe("Minimum length")
  })

  it("separates pages from the settings inside them", () => {
    const { surfaces, fieldGroups } = searchIndex(index, "password")

    expect(surfaces.map((s) => s.title)).toContain("Password")
    expect(fieldGroups[0].sectionTitle).toBe("Password")
  })

  it("returns nothing for a blank or unmatched query", () => {
    const empty = {
      surfaces: [],
      totalSurfaces: 0,
      fieldGroups: [],
      totalFieldGroups: 0,
      total: 0,
    }
    expect(searchIndex(index, "")).toEqual(empty)
    expect(searchIndex(index, "zzzzz")).toEqual(empty)
  })

  it("says which visible text produced the hit", () => {
    // A row that matched only on the invisible config key has to be able to
    // explain itself, or it reads as noise in a list of highlighted rows.
    const { fieldGroups } = searchIndex(index, "days")
    const field = fieldGroups[0].fields.find((f) => f.title === "Expiry")
    expect(field?.via).toBe("keywords")

    expect(searchIndex(index, "characters").fieldGroups[0].fields[0].via).toBe(
      "description"
    )
    expect(searchIndex(index, "minimum").fieldGroups[0].fields[0].via).toBe(
      "title"
    )
  })
})

describe("searchIndex caps", () => {
  const many: SystemSettingsSection[] = [
    {
      key: "Password",
      group: "security",
      editable: true,
      fields: Array.from({ length: 9 }, (_, i) => ({
        path: `Widget${i}`,
        kind: "int",
      })),
    } as SystemSettingsSection,
  ]
  const index = buildSearchIndex(many, t, allow)

  it("caps a group but reports what it left out", () => {
    const { fieldGroups } = searchIndex(index, "widget")

    expect(fieldGroups[0].fields).toHaveLength(MAX_FIELDS_PER_GROUP)
    // The count is the point: a silently truncated list is indistinguishable
    // from an exhaustive one.
    expect(fieldGroups[0].totalFields).toBe(9)
  })

  it("lifts the cap for a group the user expanded", () => {
    const { fieldGroups } = searchIndex(
      index,
      "widget",
      new Set(["Password"])
    )

    expect(fieldGroups[0].fields).toHaveLength(9)
  })
})

describe("rankRecords", () => {
  const record = (
    id: string,
    title: string,
    description = ""
  ): RecordEntry => ({
    kind: "record",
    id,
    sourceKey: "user",
    title,
    description,
    route: `/users/${id}`,
    keywords: "",
  })

  const rows = [
    record("1", "Zara Ahmed", "zara@example.com"),
    record("2", "Ahmed Salem", "ahmed@example.com"),
    record("3", "Nobody", "nobody@example.com"),
  ]

  it("keeps a server-filtered source in the order the server returned", () => {
    // The endpoint ranked against the whole table. Re-sorting six rows here
    // would override that with a judgement made on a sample of it — and would
    // drop the third row, which matched a column the palette does not show.
    const ranked = rankRecords(rows, "ahmed", "remote")

    expect(ranked.map((entry) => entry.id)).toEqual(["1", "2", "3"])
  })

  it("filters and ranks a source that was fetched whole", () => {
    // A prefix match on the name beats the same word buried mid-title, and the
    // row that matches nothing is not a result at all.
    const ranked = rankRecords(rows, "ahmed", "local")

    expect(ranked.map((entry) => entry.title)).toEqual([
      "Ahmed Salem",
      "Zara Ahmed",
    ])
  })

  it("matches a record on the line under its name", () => {
    // An address is what an admin usually types, and it is never the title.
    const ranked = rankRecords(rows, "nobody@", "local")

    expect(ranked.map((entry) => entry.title)).toEqual(["Nobody"])
    expect(ranked[0].via).toBe("description")
  })
})
