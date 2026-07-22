import { describe, expect, it } from "vitest"

import { getTimeZoneOffsetLabel, isValidTimeZone } from "./timezone"

describe("time-zone helpers", () => {
  it("reports the current offset for an IANA zone at the supplied instant", () => {
    expect(
      getTimeZoneOffsetLabel(
        "Europe/Istanbul",
        new Date("2026-07-22T00:00:00Z")
      )
    ).toBe("UTC+03:00")
  })

  it("rejects invalid identifiers without throwing", () => {
    expect(isValidTimeZone("Not/A_Real_Zone")).toBe(false)
    expect(getTimeZoneOffsetLabel("Not/A_Real_Zone")).toBe("")
  })
})
