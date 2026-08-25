import { codeI18nKey } from "./i18n-key"

/**
 * The console's mirror of `Auth.Domain.Constants.AuditActions`.
 *
 * Not fetched from the API, deliberately. The names these codes are shown under
 * live in this app's locale files, so an action the server knows and this build
 * does not would render as its raw code either way — an endpoint would buy a
 * round trip and a gateway route for nothing. `audit-catalog.test.ts` reads the
 * C# file and holds the two lists together, the same way `sections.test.ts`
 * holds the settings registry and this app together.
 */

/** Every category, in the order the console presents them. */
export const AUDIT_ACTION_TYPES = [
  "Authentication",
  "Authorization",
  "Security",
  "UserManagement",
  "Administration",
  "Application",
  "OrganizationManagement",
  "ApiKeyManagement",
  "System",
] as const

export interface AuditActionEntry {
  /** The value stored in `AuditLogs.Action` and sent as the `action` filter. */
  code: string
  /** The value stored in `AuditLogs.ActionType`. */
  actionType: string
}

/** Every action this system records, with the category it is filed under. */
export const AUDIT_ACTIONS: readonly AuditActionEntry[] = [
  { code: "user.login", actionType: "Authentication" },
  { code: "user.logout", actionType: "Authentication" },
  { code: "user.logout.all", actionType: "Authentication" },
  { code: "permission.granted", actionType: "Authorization" },
  { code: "permission.revoked", actionType: "Authorization" },
  { code: "role.assigned", actionType: "Authorization" },
  { code: "role.removed", actionType: "Authorization" },
  { code: "role.created", actionType: "Authorization" },
  { code: "role.updated", actionType: "Authorization" },
  { code: "role.deleted", actionType: "Authorization" },
  { code: "role.permission.granted", actionType: "Authorization" },
  { code: "role.permission.revoked", actionType: "Authorization" },
  { code: "password.created", actionType: "Security" },
  { code: "password.changed", actionType: "Security" },
  { code: "twofactor.enabled", actionType: "Security" },
  { code: "twofactor.disabled", actionType: "Security" },
  { code: "user.locked", actionType: "Security" },
  { code: "user.unlocked", actionType: "Security" },
  { code: "external-login.linked", actionType: "Security" },
  { code: "session.ended", actionType: "Security" },
  { code: "webhookkey.created", actionType: "Security" },
  { code: "webhookkey.revoked", actionType: "Security" },
  { code: "user.created", actionType: "UserManagement" },
  { code: "user.deleted", actionType: "UserManagement" },
  { code: "user.harddeleted", actionType: "UserManagement" },
  { code: "user.deletion_requested", actionType: "UserManagement" },
  { code: "user.deletion_cancelled", actionType: "UserManagement" },
  { code: "user.deletion_completed", actionType: "UserManagement" },
  { code: "user.deletion_reapplied", actionType: "UserManagement" },
  { code: "system-settings.updated", actionType: "Administration" },
  { code: "platform-settings.updated", actionType: "Administration" },
  { code: "notification-template.published", actionType: "Administration" },
  { code: "notification-template.unpublished", actionType: "Administration" },
  { code: "notification-template.rolled-back", actionType: "Administration" },
  { code: "secrets.value.changed", actionType: "Administration" },
  {
    code: "secrets.operation.confirmation-requested",
    actionType: "Administration",
  },
  { code: "secrets.operation.executed", actionType: "Administration" },
  { code: "application.access.granted", actionType: "Application" },
  { code: "application.access.revoked", actionType: "Application" },
  { code: "application.activated", actionType: "Application" },
  { code: "application.deactivated", actionType: "Application" },
  {
    code: "organization.ownership_transfer_initiated",
    actionType: "OrganizationManagement",
  },
  {
    code: "organization.ownership_transferred",
    actionType: "OrganizationManagement",
  },
  { code: "apikey.created", actionType: "ApiKeyManagement" },
  { code: "apikey.revoked", actionType: "ApiKeyManagement" },
  { code: "system.privacy_policy_content_saved", actionType: "System" },
  { code: "system.privacy_policy_published", actionType: "System" },
  { code: "system.policy_notification_sent", actionType: "System" },
  { code: "system.retention_sweep", actionType: "System" },
]

/**
 * i18n key for an action code: `external-login.linked` becomes
 * `externalLoginLinked`, read under `auditLogs.actions.*`. The transform itself
 * is shared with the notification-type catalogue — see `codeI18nKey`.
 */
export function auditActionI18nKey(code: string): string {
  return codeI18nKey(code)
}

/** i18n key for a category: `UserManagement` becomes `userManagement`. */
export function auditActionTypeI18nKey(actionType: string): string {
  return actionType.charAt(0).toLowerCase() + actionType.slice(1)
}

/** The category an action is filed under, or undefined for an unknown code. */
export function auditActionType(code: string): string | undefined {
  return AUDIT_ACTIONS.find((entry) => entry.code === code)?.actionType
}
