/**
 * Reading an API timestamp as an instant, without the date-formatting library.
 *
 * The rule every date in this client follows: the API emits UTC with "Z", and a
 * value that carries neither a "Z" nor an offset is UTC too (a guard for older
 * cached payloads). `format.ts` reads its dates through `withUtcOffset`; a page
 * that only counts down to a deadline uses `parseApiInstant`, so it does not
 * carry the formatting library into its first load (S20: the passkey options'
 * expiry on the sign-in page; `login-payload.spec.ts` measures it).
 */

// Datetime string carrying neither a "Z" nor a numeric offset.
const DATETIME_WITHOUT_OFFSET = /^\d{4}-\d{2}-\d{2}T[\d:.]+$/

/** The value with "Z" appended when it carries no zone of its own. */
export function withUtcOffset(value: string): string {
  return DATETIME_WITHOUT_OFFSET.test(value) ? `${value}Z` : value
}

/**
 * An API timestamp as an instant; null when absent or unreadable. For a
 * deadline the client counts down to, not for display. Fractions beyond the
 * millisecond (.NET sends up to seven digits) are cut, which every engine reads.
 */
export function parseApiInstant(value: string | null | undefined): Date | null {
  if (!value) return null
  const date = new Date(withUtcOffset(value).replace(/(\.\d{3})\d+/, "$1"))
  return Number.isNaN(date.getTime()) ? null : date
}
