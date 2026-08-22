import type { ColumnFiltersState } from "@tanstack/react-table"

import {
  enumArrayUrlFilter,
  stringArrayUrlFilter,
  stringUrlFilter,
  type ListUrlFilter,
} from "@authsystem/ui/hooks/use-search-query"

export type CredentialExpiryState = "expired" | "soon" | "later" | "none"

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

export const CREDENTIAL_KEY_URL_FILTERS = {
  applicationId: stringUrlFilter({ maxLength: 128 }),
  environments: stringArrayUrlFilter({
    param: "environment",
    maxItems: 10,
    maxValueLength: 50,
  }),
  statuses: enumArrayUrlFilter(["active", "revoked"], "status"),
  expiry: expiryUrlFilter,
}

export function credentialColumnFilters(
  filters: CredentialKeyListFilters
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
): Pick<CredentialKeyListFilters, "environments" | "statuses" | "expiry"> {
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
