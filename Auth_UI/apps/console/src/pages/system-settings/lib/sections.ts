import type { Schemas } from "@authsystem/api/types"

import { PERMISSIONS } from "@/lib/constants"

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
  GeoIp: "geoIp",
  Cors: "cors",
  RateLimiting: "rateLimiting",
  GatewayRateLimiting: "gatewayRateLimiting",
  ExternalAuth: "externalAuth",
  IdentityProvider: "identityProvider",
  Email: "email",
  Notifications: "notificationsSection",
  ImageStorage: "imageStorage",
  AccountDeletion: "accountDeletionSection",
  DataRetention: "dataRetention",
  ExpiredDataCleanup: "expiredDataCleanup",
  DataController: "dataController",
  Maintenance: "maintenance",
  HealthChecks: "healthChecks",
  Serilog: "serilog",
  DataProtection: "dataProtection",
  SecretManagement: "secretManagement",
  ConnectionStrings: "connectionStrings",
}

/**
 * A section whose real controls live on a page of its own rather than in the
 * section card — because they are operations, not settings.
 */
export interface SectionCompanionPage {
  /** Absolute route of the companion page. */
  route: string
  /** i18n key under `systemSettings.*` for the card's footer button. */
  actionLabelKey: string
  /** Permission the companion page requires — its own, not the section's. */
  permission: string
}

/**
 * One declaration drives three things that would otherwise drift apart: the
 * button on the section card, the deep link on every setting the section owns
 * the value of, and the suppression of the section's generic command-palette
 * row (the companion page's own row already names that destination, and two
 * rows one click apart is what the palette's trail exists to avoid).
 *
 * A section absent from here — DataProtection, ConnectionStrings — renders
 * exactly as it does today.
 */
export const SECTION_COMPANION_PAGES: Record<string, SectionCompanionPage> = {
  SecretManagement: {
    route: "/admin/system-settings/SecretManagement/keys",
    actionLabelKey: "systemSettings.openSecrets",
    permission: PERMISSIONS.secrets.manage,
  },
}

/**
 * Deterministic field-path → i18n key mapping so labels/hints need no
 * per-field table: "BreachedPasswordCheck:Mode" → "breachedPasswordCheckMode",
 * key = label, key + "Hint" = hint.
 */
/**
 * react-hook-form reads "." (and brackets) in a field name as a nested path,
 * so a config path like "MinimumLevel:Override:Microsoft.Hosting.Lifetime"
 * would register a nested object that never matches its flat default value —
 * the form would report itself dirty on mount and submit an empty value.
 * Config paths never contain "-", so it is a safe stand-in.
 */
export function formFieldName(path: string): string {
  return path.replace(/\./g, "-")
}

/**
 * DOM id of a setting's row, so the settings search can land on one setting
 * rather than just its section. Read by `section-form`'s anchor effect.
 */
export function settingAnchorId(path: string): string {
  return `setting-${formFieldName(path)}`
}

export function fieldI18nKey(path: string): string {
  const joined = path
    .split(":")
    .map((segment, index) =>
      index === 0
        ? segment.charAt(0).toLowerCase() + segment.slice(1)
        : segment
    )
    .join("")
  // Dots (Serilog override namespaces) would read as nested i18n lookups.
  return joined.replace(/\./g, "")
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
