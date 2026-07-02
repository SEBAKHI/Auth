import { describe, expect, it } from "vitest"

import { getErrorMessage, getFieldErrors } from "./errors"

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
