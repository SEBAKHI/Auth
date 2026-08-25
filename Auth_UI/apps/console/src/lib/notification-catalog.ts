import type { TFunction } from "i18next"

import { codeI18nKey } from "./i18n-key"

/**
 * The console's mirror of `Auth.Domain.Constants.NotificationTypeCodes`.
 *
 * Not fetched from the API, deliberately — the same reasoning as
 * `audit-catalog.ts`. The API does return a `Name` per type, but it is one
 * column holding one string: it cannot change when the reader switches the
 * console to another language, which is the entire point of showing a name
 * rather than a code. The names live in this app's locale files instead, and
 * `notification-catalog.test.ts` reads the C# file to keep the two lists from
 * drifting apart.
 *
 * Types are seed-only — the API exposes GET and PUT and no way to create one
 * (`NotificationTypesController`) — so this list is closed in practice. A code
 * this build has never heard of still renders: it falls back to itself.
 */
export const NOTIFICATION_TYPE_CODES: readonly string[] = [
  "email-verification",
  "password-reset",
  "organization-invitation",
  "welcome-email",
  "ownership-transfer-code",
  "ownership-transferred",
  "account-deletion-requested",
  "account-deletion-verification",
  "account-deletion-cancelled",
  "account-deletion-completed",
  "account-deleted-by-admin",
  "privacy-policy-updated",
  "new-device-sign-in",
  "sessions-revoked-token-reuse",
  "session-limit-enforced",
  "secret-operation-challenge",
  "password-created",
  "password-changed",
]

/**
 * i18n key for a type code: `new-device-sign-in` becomes `newDeviceSignIn`,
 * read under `notifications.types.*`.
 */
export function notificationTypeI18nKey(code: string): string {
  return codeI18nKey(code)
}

/**
 * The name a type is read under, in the console's current language. Shared by
 * the delivery-log table and its detail sheet so the two can never disagree —
 * including on the fallback, which is the code itself: a name this build does
 * not carry is better shown as the string the server actually stored than as a
 * blank or an invented one.
 */
export function notificationTypeLabel(t: TFunction, code: string): string {
  return t(`notifications.types.${notificationTypeI18nKey(code)}`, {
    defaultValue: code,
  })
}
