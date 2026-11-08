import { describe, expect, it } from "vitest"

import { parseApiInstant, withUtcOffset } from "./api-instant"

/**
 * An API timestamp as an instant, without the formatting library (S20: the
 * sign-in page counts down to its passkey options' expiry and must not carry
 * date-fns into its first load). The same rule as every date here: a value
 * without a zone is UTC.
 */
describe("parseApiInstant", () => {
  it.each([
    ["with Z", "2026-11-08T02:20:00Z", Date.UTC(2026, 10, 8, 2, 20, 0)],
    ["with an offset", "2026-11-08T05:20:00+03:00", Date.UTC(2026, 10, 8, 2, 20, 0)],
    ["without a zone, as UTC", "2026-11-08T02:20:00", Date.UTC(2026, 10, 8, 2, 20, 0)],
    ["with .NET's seven fraction digits", "2026-11-08T02:20:00.1234567Z", Date.UTC(2026, 10, 8, 2, 20, 0, 123)],
    ["with seven digits and no zone", "2026-11-08T02:20:00.1234567", Date.UTC(2026, 10, 8, 2, 20, 0, 123)],
  ])("reads a value %s", (_label, value, expected) => {
    expect(parseApiInstant(value)?.getTime()).toBe(expected)
  })

  it.each([null, undefined, "", "soon", "2026-13-45T99:00:00Z"])("answers null for %j", (value) => {
    expect(parseApiInstant(value)).toBeNull()
  })
})

describe("withUtcOffset", () => {
  it("adds Z only where the value has no zone", () => {
    expect(withUtcOffset("2026-11-08T02:20:00")).toBe("2026-11-08T02:20:00Z")
    expect(withUtcOffset("2026-11-08T02:20:00Z")).toBe("2026-11-08T02:20:00Z")
    expect(withUtcOffset("2026-11-08T02:20:00-05:00")).toBe("2026-11-08T02:20:00-05:00")
    // A calendar date is left alone: the formatters render it as it is.
    expect(withUtcOffset("2026-07-04")).toBe("2026-07-04")
  })
})
