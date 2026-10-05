import { describe, expect, it } from "vitest"

import { validateReturnToUrl } from "@authsystem/auth/return-to"
import { API_BASE_URL } from "@authsystem/api/env"

import { signInPath, withoutOrganizationRequest } from "./create-organization-flow"

const authorize = (query: string) => `${API_BASE_URL}/api/v1/auth/authorize?${query}`

describe("continuing without an organization", () => {
  const returnTo = authorize(
    "response_type=code&client_id=EDIS&redirect_uri=https%3A%2F%2Fedis.example.com%2Fcb" +
      "&code_challenge=abc&code_challenge_method=S256&state=s1&prompt=create&create_organization=true"
  )

  it("removes create_organization and keeps every other parameter", () => {
    const result = new URL(withoutOrganizationRequest(returnTo))
    const original = new URL(returnTo)

    expect(result.searchParams.has("create_organization")).toBe(false)
    original.searchParams.delete("create_organization")
    expect([...result.searchParams.entries()]).toEqual([...original.searchParams.entries()])
    expect([...result.searchParams.keys()].length).toBeGreaterThan(0)
  })

  it("is still a returnTo the shared rule accepts", () => {
    expect(validateReturnToUrl(withoutOrganizationRequest(returnTo))).not.toBeNull()
  })

  it("signs in again for the same request", () => {
    expect(signInPath(returnTo)).toBe(`/login?returnTo=${encodeURIComponent(returnTo)}`)
  })
})
