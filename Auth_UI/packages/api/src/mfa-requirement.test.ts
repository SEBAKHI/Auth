import { afterEach, describe, expect, it } from "vitest"

import { currentMfaRequirement, readMfaRequirement } from "@authsystem/api/mfa-requirement"
import { setAccessToken } from "@authsystem/api/token-store"

function tokenWith(claims: Record<string, unknown>): string {
  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "")
  return `header.${payload}.signature`
}

describe("readMfaRequirement", () => {
  it.each([undefined, null, ""])("reads an absent value (%s) as none", (value) => {
    // An API built before the field, and every token minted without the claim.
    expect(readMfaRequirement(value)).toBe("none")
  })

  it.each(["none", "enroll", "step_up", "reauthenticate"] as const)(
    "reads %s as itself",
    (value) => {
      expect(readMfaRequirement(value)).toBe(value)
    }
  )

  it.each(["step-up", "STEP_UP", "passkey", 1, true, ["enroll"], { value: "enroll" }])(
    "fails closed on a value it does not know (%s): sign in again",
    (value) => {
      expect(readMfaRequirement(value)).toBe("reauthenticate")
    }
  )
})

describe("currentMfaRequirement", () => {
  afterEach(() => setAccessToken(null))

  it("reads the mfa_req claim of the token this tab holds", () => {
    setAccessToken(tokenWith({ sub: "u", mfa_req: "enroll" }))
    expect(currentMfaRequirement()).toBe("enroll")
  })

  it("is none for a token without the claim, and with no token", () => {
    setAccessToken(tokenWith({ sub: "u", permissions: ["*"] }))
    expect(currentMfaRequirement()).toBe("none")

    setAccessToken(null)
    expect(currentMfaRequirement()).toBe("none")
  })
})
