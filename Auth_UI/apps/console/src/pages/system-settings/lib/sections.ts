import type { Schemas } from "@authsystem/api/types"

export type SystemSettingsDto = Schemas["SystemSettingsDto"]
export type SystemSettingsSection = Schemas["SystemSettingsSectionDto"]
export type SystemSettingsField = Schemas["SystemSettingsFieldDto"]

export const SETTINGS_QUERY_KEY = ["system-settings"] as const

/** Display order of the setup navigation groups. */
export const GROUP_ORDER = [
  "security",
  "access",
  "communication",
  "storage",
  "operations",
  "infrastructure",
] as const

/**
 * i18n namespace per backend section key. A section missing here still
 * renders (labels fall back to raw field paths), so a backend-first deploy
 * cannot blank the page.
 */
export const SECTION_I18N: Record<string, string> = {
  Jwt: "jwt",
  Password: "password",
  Session: "session",
  Gateway: "gateway",
}

/**
 * Deterministic field-path → i18n key mapping so labels/hints need no
 * per-field table: "BreachedPasswordCheck:Mode" → "breachedPasswordCheckMode",
 * key = label, key + "Hint" = hint.
 */
export function fieldI18nKey(path: string): string {
  const joined = path
    .split(":")
    .map((segment, index) =>
      index === 0
        ? segment.charAt(0).toLowerCase() + segment.slice(1)
        : segment
    )
    .join("")
  return joined
}

/** Groups the sections for the setup navigation, in stable order. */
export function groupSections(
  sections: SystemSettingsSection[]
): { group: string; sections: SystemSettingsSection[] }[] {
  return GROUP_ORDER.map((group) => ({
    group,
    sections: sections.filter((s) => s.group === group),
  })).filter((g) => g.sections.length > 0)
}
