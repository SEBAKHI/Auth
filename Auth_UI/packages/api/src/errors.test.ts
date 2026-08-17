import { describe, expect, it } from "vitest"

import { getErrorCodes, getErrorMessage, getFieldErrors } from "./errors"
import { en } from "@authsystem/i18n/locales/en"

/**
 * The exact body Auth_API returns for a single Forbidden error.
 *
 * ApiController.Problem sets `Title = firstError.Code` and puts the LOCALIZED
 * text in `Detail`; the `errors` extension is attached only when more than one
 * error is present. Nothing rewrites it — there is no AddProblemDetails,
 * CustomizeProblemDetails or ProblemDetailsFactory anywhere in the API — and
 * `JsonIgnoreCondition.WhenWritingNull` drops the null `type`.
 *
 * This shape is what every pending-deletion recovery route depends on: three
 * separate branches decide whether to offer recovery or to dead-end on a toast
 * by asking getErrorCodes for this code. If Title ever became a localized
 * sentence, every one of them would silently fall through to the toast, on both
 * the password and the provider paths at once, and no other test would notice.
 */
const PENDING_DELETION_403 = {
  title: "User.AccountPendingDeletion",
  status: 403,
  detail:
    "This account is deactivated and scheduled for deletion on 2026-09-14 23:29:34Z. It can be restored until then.",
  instance: "/api/v1/Auth/external-login",
}

describe("getErrorCodes against the API's real ProblemDetails", () => {
  it("recovers the code from a single-error 403", () => {
    expect(getErrorCodes(PENDING_DELETION_403)).toContain(
      "User.AccountPendingDeletion"
    )
  })

  it("still surfaces the localized sentence as the message", () => {
    expect(getErrorMessage(PENDING_DELETION_403)).toBe(
      PENDING_DELETION_403.detail
    )
  })

  it("does not mistake a localized title for a code", () => {
    // The title branch is guarded on the absence of a space, which is the only
    // thing separating an ErrorOr code from a human sentence.
    expect(getErrorCodes({ title: "This account is deactivated" })).toEqual([])
  })
})

describe("getErrorMessage", () => {
  it("reads an ErrorOr-style errors array", () => {
    expect(
      getErrorMessage({
        errors: [{ code: "X", description: "Invalid credentials" }],
      })
    ).toBe("Invalid credentials")
  })

  it("falls back to detail then title", () => {
    expect(getErrorMessage({ detail: "Detailed" })).toBe("Detailed")
    expect(getErrorMessage({ title: "Titled" })).toBe("Titled")
  })

  it("handles strings and Errors", () => {
    expect(getErrorMessage("boom")).toBe("boom")
    expect(getErrorMessage(new Error("nope"))).toBe("nope")
  })

  it("uses the fallback for empty input", () => {
    expect(getErrorMessage(null, "fallback")).toBe("fallback")
  })

  it("localizes the default fallback via i18n", () => {
    expect(getErrorMessage(null)).toBe(en.errors.generic)
  })
})

describe("getFieldErrors", () => {
  it("maps a validation dictionary to camelCase fields", () => {
    expect(
      getFieldErrors({
        errors: { Email: ["Required"], Password: ["Too short"] },
      })
    ).toEqual({ email: "Required", password: "Too short" })
  })

  it("returns empty for an errors array", () => {
    expect(getFieldErrors({ errors: [{ code: "X" }] })).toEqual({})
  })
})
