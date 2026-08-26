import {
  dateUrlFilter,
  stringUrlFilter,
} from "@authsystem/ui/hooks/use-search-query"

/**
 * What an audit list can be narrowed by, and how that narrowing reaches the API.
 *
 * The controls are the visible half and the least important one. The half that
 * matters is {@link toAuditLogQuery} — one object spread into BOTH the list
 * request and the export body, so a filter cannot be applied to what a reader
 * sees and dropped from the file they download. Writing that mapping out per
 * screen is how a surface forgets a pin, and on a person's page a forgotten pin
 * is the whole platform's history under one person's name.
 */
export type AuditLogFilters = {
  applicationId: string
  action: string
  actionType: string
  /**
   * Two values as a string rather than a boolean, because the choice has three
   * states and a boolean carries two: "" is "do not narrow", which is not the
   * same as "show me the failures". Anything else in the URL canonicalizes to
   * "", so a hand-edited link widens rather than narrows.
   */
  result: string
  from: string
  to: string
}

/** The URL codecs, identical on every surface that offers these filters. */
export const AUDIT_LOG_FILTER_SCHEMA = {
  applicationId: stringUrlFilter({ maxLength: 128 }),
  action: stringUrlFilter({ maxLength: 100 }),
  actionType: stringUrlFilter({ maxLength: 100 }),
  result: stringUrlFilter({ pattern: /^(true|false)$/, maxLength: 5 }),
  from: dateUrlFilter(),
  to: dateUrlFilter(),
}

/** Empty on every axis — the state a "clear" returns the row to. */
export const EMPTY_AUDIT_LOG_FILTERS: AuditLogFilters = {
  applicationId: "",
  action: "",
  actionType: "",
  result: "",
  from: "",
  to: "",
}

/** True when any control is narrowing, which is what a Clear affordance needs. */
export function hasAuditLogFilters(filters: AuditLogFilters): boolean {
  return Object.values(filters).some((value) => value !== "")
}

/**
 * URL state as the API's query parameters.
 *
 * `undefined` and not `""` for an unset value: an empty string is a value the
 * model binder will bind, and `isSuccess: false` would ask for the failures
 * rather than for everything.
 */
export function toAuditLogQuery(filters: AuditLogFilters) {
  return {
    applicationId: filters.applicationId || undefined,
    action: filters.action || undefined,
    actionType: filters.actionType || undefined,
    isSuccess: filters.result === "" ? undefined : filters.result === "true",
    // A date in the URL is a day; the API takes an instant. Widening each end to
    // the whole day is what makes "from the 3rd to the 3rd" hold that day's rows
    // rather than only its first moment.
    fromDate: filters.from ? startOfDay(filters.from) : undefined,
    toDate: filters.to ? endOfDay(filters.to) : undefined,
  }
}

function startOfDay(date: string): string {
  return new Date(`${date}T00:00:00`).toISOString()
}

function endOfDay(date: string): string {
  return new Date(`${date}T23:59:59`).toISOString()
}
