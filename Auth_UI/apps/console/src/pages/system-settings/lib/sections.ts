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
  Registration: "registration",
  Organizations: "organizations",
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
  /**
   * Drops the section's own row from the command palette, because the companion
   * page's row already names that destination and two near-identical rows one
   * click apart is what the palette's trail exists to prevent.
   *
   * Opt-in rather than implied by having a companion page, and the distinction
   * is not cosmetic: SecretManagement has no editable setting of its own, so its
   * card IS the link. DataRetention has five — including how long the audit
   * record is kept — and suppressing it would delete those five settings' own
   * section from the only search that finds them.
   */
  suppressPaletteRow?: boolean
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
    suppressPaletteRow: true,
  },
  // The audit catalogue hangs off the section that decides how long the audit
  // record is kept: that is the only settings section about audit logs at all,
  // and a reference list of what gets recorded belongs beside the setting for
  // how long it survives. It reads rather than writes, so its gate is
  // auditlogs:read and not the section's system-settings:manage.
  DataRetention: {
    route: "/admin/system-settings/audit-catalog",
    actionLabelKey: "systemSettings.openAuditCatalog",
    permission: PERMISSIONS.auditLogs.read,
  },
}

/**
 * Settings whose value decides who can sign in — the ones where a value the
 * server accepts can still lock every administrator out of the console.
 * Saving one goes through a confirmation, so a wrong number is a decision
 * rather than a keystroke.
 *
 * Keyed by SECTION KEY, not by bare path: seven field paths repeat across
 * sections (RegisterPermitLimit, Enabled, WorkerPollMinutes, BatchSize,
 * PublicBaseUrl, OtpExpirationMinutes, RegisterWindowSeconds), so a flat set
 * of paths would gate the wrong section's row. Values are section-relative
 * paths exactly as the backend registry declares them; matching is exact,
 * with no prefix claims.
 *
 * This list belongs on SettingFieldDefinition in
 * Auth/Auth.Application/SystemSettings/SystemSettingsRegistry.cs — the server
 * is what knows a setting's blast radius, and a console a release behind would
 * then still confirm a newly dangerous field. It lives here because this change
 * ships no backend, and it is deliberately shaped like SECTION_COMPANION_PAGES
 * so moving it later is a deletion on this side, not a redesign.
 *
 * A section or path absent from here saves exactly as it does today.
 */
export const HIGH_IMPACT_PATHS: Record<string, string[]> = {
  Jwt: ["Issuer", "Audience"],
  Password: ["MaxFailedAttempts", "LockoutDurationMinutes", "Argon2MemorySize"],
  Session: ["MaxConcurrentSessions", "TerminateOldestOnMax"],
  Gateway: ["ValidationEnabled"],
  Cors: ["AllowedOrigins", "AllowCredentials"],
  RateLimiting: ["LoginPermitLimit"],
  GatewayRateLimiting: [
    "GlobalPermitLimit",
    "AuthPermitLimit",
    "AdminPermitLimit",
  ],
  ExternalAuth: ["RequireNonce"],
  IdentityProvider: ["IdpSessionCookieName"],
}

/**
 * Settings whose VALUE is human language rather than a machine identifier: a
 * legal name, a postal address, a person. The operator types those in their own
 * script, so the input takes its direction from what was typed (`dir="auto"`)
 * instead of being pinned left-to-right.
 *
 * Direction is a property of the value, never of the kind: `Email:SmtpHost` and
 * `Email:SenderName` are both strings, and only one of them is prose. The three
 * stringArray fields hold origins, URL prefixes and MIME types, so they stay
 * pinned — they are absent from here on purpose.
 *
 * Same eventual home as HIGH_IMPACT_PATHS: SettingFieldDefinition on the server.
 */
export const PROSE_VALUE_PATHS: Record<string, string[]> = {
  DataController: [
    "LegalName",
    "Address",
    "EmailProvider",
    "HostingProvider",
    "HostingCountry",
    "DpoContact",
  ],
  Email: ["SenderName"],
}

/** Whether saving this field needs the "you can lock yourself out" confirmation. */
export function isHighImpactPath(sectionKey: string, path: string): boolean {
  return (HIGH_IMPACT_PATHS[sectionKey] ?? []).includes(path)
}

/** Whether this field's value is prose, and so takes its direction from itself. */
export function isProseValuePath(sectionKey: string, path: string): boolean {
  return (PROSE_VALUE_PATHS[sectionKey] ?? []).includes(path)
}

/** The direction an editable value is typed and read in. */
export function valueDirection(
  sectionKey: string | undefined,
  path: string
): "ltr" | "auto" {
  return sectionKey && isProseValuePath(sectionKey, path) ? "auto" : "ltr"
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
      index === 0 ? segment.charAt(0).toLowerCase() + segment.slice(1) : segment
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
