import type { ColumnFiltersState } from "@tanstack/react-table"

import { daysUntil } from "@authsystem/ui/format"
import {
  enumArrayUrlFilter,
  stringArrayUrlFilter,
  stringUrlFilter,
  type ListUrlFilter,
} from "@authsystem/ui/hooks/use-search-query"

export type CredentialExpiryState = "expired" | "soon" | "later" | "none"

/**
 * Matches the server's default credential-expiry horizon, so the rows a key
 * list shows are the rows the dashboard counted.
 */
const EXPIRY_SOON_DAYS = 14

export function credentialExpiryState(
  iso: string | null | undefined
): CredentialExpiryState {
  const remaining = daysUntil(iso)
  if (remaining === null) return "none"
  if (remaining < 0) return "expired"
  return remaining <= EXPIRY_SOON_DAYS ? "soon" : "later"
}

export type CredentialKeyListFilters = {
  applicationId: string
  environments: string[]
  statuses: Array<"active" | "revoked">
  expiry: CredentialExpiryState[]
}

const expiryCodec = enumArrayUrlFilter(
  ["expired", "soon", "later", "none"] as const,
  "expiry"
)

// Dashboard handoff `expiry=soon` means the urgency cohort, which includes a
// credential that crossed its expiry boundary while the alert was open.
const expiryUrlFilter: ListUrlFilter<CredentialExpiryState[]> = {
  ...expiryCodec,
  parse: (raw) =>
    raw === "soon" ? ["soon", "expired"] : expiryCodec.parse(raw),
}

/** The filters a key table owns: the in-table chips, on every surface. */
export type CredentialKeyColumnFilters = Pick<
  CredentialKeyListFilters,
  "environments" | "statuses" | "expiry"
>

/**
 * The chips' URL filters alone, for an application's tab: the application is
 * fixed there, so it has no picker and no parameter.
 */
export const CREDENTIAL_KEY_COLUMN_URL_FILTERS = {
  environments: stringArrayUrlFilter({
    param: "environment",
    maxItems: 10,
    maxValueLength: 50,
  }),
  statuses: enumArrayUrlFilter(["active", "revoked"], "status"),
  expiry: expiryUrlFilter,
}

export const CREDENTIAL_KEY_URL_FILTERS = {
  applicationId: stringUrlFilter({ maxLength: 128 }),
  ...CREDENTIAL_KEY_COLUMN_URL_FILTERS,
}

export function credentialColumnFilters(
  filters: CredentialKeyColumnFilters
): ColumnFiltersState {
  return [
    ...(filters.environments.length
      ? [{ id: "environment", value: filters.environments }]
      : []),
    ...(filters.statuses.length
      ? [{ id: "status", value: filters.statuses }]
      : []),
    ...(filters.expiry.length
      ? [{ id: "expiresAt", value: filters.expiry }]
      : []),
  ]
}

export function credentialFiltersFromColumns(
  next: ColumnFiltersState
): CredentialKeyColumnFilters {
  return {
    environments:
      (next.find((filter) => filter.id === "environment")?.value as
        | string[]
        | undefined) ?? [],
    statuses:
      (next.find((filter) => filter.id === "status")?.value as
        | CredentialKeyListFilters["statuses"]
        | undefined) ?? [],
    expiry:
      (next.find((filter) => filter.id === "expiresAt")?.value as
        | CredentialExpiryState[]
        | undefined) ?? [],
  }
}
