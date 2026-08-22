import { describe, expect, it } from "vitest"

import {
  CREDENTIAL_KEY_URL_FILTERS,
  credentialColumnFilters,
  credentialFiltersFromColumns,
  type CredentialKeyListFilters,
} from "./credential-key-list-url"

const EMPTY: CredentialKeyListFilters = {
  applicationId: "",
  environments: [],
  statuses: [],
  expiry: [],
}

describe("credential key list URL state", () => {
  it("widens the dashboard's 'expiring soon' handoff to include what already expired", () => {
    // The alert is a cohort, not an instant: a key that crossed its expiry
    // while the dashboard was open still belongs to the urgency it named.
    expect(CREDENTIAL_KEY_URL_FILTERS.expiry.parse("soon")).toEqual([
      "soon",
      "expired",
    ])
  })

  it("keeps every other expiry selection literal", () => {
    expect(CREDENTIAL_KEY_URL_FILTERS.expiry.parse("expired,later")).toEqual([
      "expired",
      "later",
    ])
    expect(CREDENTIAL_KEY_URL_FILTERS.expiry.parse("sepia")).toEqual([])
    expect(CREDENTIAL_KEY_URL_FILTERS.expiry.parse(null)).toEqual([])
  })

  it("names the table columns each filter drives", () => {
    expect(
      credentialColumnFilters({
        ...EMPTY,
        environments: ["Production"],
        statuses: ["revoked"],
        expiry: ["soon"],
      })
    ).toEqual([
      { id: "environment", value: ["Production"] },
      { id: "status", value: ["revoked"] },
      { id: "expiresAt", value: ["soon"] },
    ])
  })

  it("emits nothing for an unfiltered list, so the URL stays bare", () => {
    expect(credentialColumnFilters(EMPTY)).toEqual([])
  })

  it("reads the table's own filter state back", () => {
    expect(
      credentialFiltersFromColumns([
        { id: "environment", value: ["Staging"] },
        { id: "status", value: ["active"] },
        { id: "expiresAt", value: ["later"] },
      ])
    ).toEqual({
      environments: ["Staging"],
      statuses: ["active"],
      expiry: ["later"],
    })
  })

  it("answers with empty selections when the table cleared them", () => {
    expect(credentialFiltersFromColumns([])).toEqual({
      environments: [],
      statuses: [],
      expiry: [],
    })
  })

  it("round-trips a selection through both directions", () => {
    const filters: CredentialKeyListFilters = {
      ...EMPTY,
      environments: ["Production"],
      statuses: ["active"],
      expiry: ["expired"],
    }
    expect(
      credentialFiltersFromColumns(credentialColumnFilters(filters))
    ).toEqual({
      environments: filters.environments,
      statuses: filters.statuses,
      expiry: filters.expiry,
    })
  })
})
