import { afterEach, describe, expect, it } from "vitest"

import {
  formatDate,
  formatDateTime,
  fullName,
  initials,
  secretStatusMeta,
  userStatusMeta,
} from "./format"
import { setActiveTimeZone } from "./timezone"

describe("userStatusMeta", () => {
  it("maps known statuses", () => {
    expect(userStatusMeta(1)).toEqual({ key: "active", variant: "default" })
    expect(userStatusMeta(3)).toEqual({ key: "locked", variant: "destructive" })
  })
  it("falls back for unknown", () => {
    expect(userStatusMeta(99).key).toBe("unknown")
  })
})

describe("secretStatusMeta", () => {
  it("maps known statuses", () => {
    expect(secretStatusMeta(0).key).toBe("notConfigured")
    expect(secretStatusMeta(1).key).toBe("configured")
  })
})

describe("formatDate", () => {
  it("renders an em dash for empty values", () => {
    expect(formatDate(undefined)).toBe("—")
    expect(formatDate(null)).toBe("—")
  })

  it("never zone-shifts pure calendar dates", () => {
    setActiveTimeZone("Pacific/Kiritimati") // UTC+14 — would flip the day if shifted
    expect(formatDate("2026-07-04")).toBe("04 Jul 2026")
    setActiveTimeZone(null)
  })
})

describe("formatDateTime with an explicit profile time zone", () => {
  afterEach(() => setActiveTimeZone(null))

  it("renders UTC instants in the active zone", () => {
    setActiveTimeZone("Asia/Riyadh") // UTC+3, no DST
    expect(formatDateTime("2026-07-04T22:01:00Z")).toBe("05 Jul 2026, 01:01")
  })

  it("treats offset-less datetimes as UTC (legacy payload guard)", () => {
    setActiveTimeZone("Asia/Riyadh")
    expect(formatDateTime("2026-07-04T22:01:00")).toBe("05 Jul 2026, 01:01")
  })

  it("treats the stored 'UTC' default as automatic (browser zone)", () => {
    setActiveTimeZone("Asia/Riyadh")
    setActiveTimeZone("UTC") // profile default — should fall back to browser zone
    const browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone
    setActiveTimeZone(browserZone)
    const expected = formatDateTime("2026-07-04T22:01:00Z")
    setActiveTimeZone("UTC")
    expect(formatDateTime("2026-07-04T22:01:00Z")).toBe(expected)
  })

  it("supports literal UTC via Etc/UTC", () => {
    setActiveTimeZone("Etc/UTC")
    expect(formatDateTime("2026-07-04T22:01:00Z")).toBe("04 Jul 2026, 22:01")
  })

  it("falls back to automatic for invalid legacy values", () => {
    setActiveTimeZone("UTC+3:00") // free-text garbage from the old input
    expect(formatDateTime("2026-07-04T22:01:00Z")).not.toBe("—")
  })
})

describe("fullName / initials", () => {
  it("joins names with a fallback", () => {
    expect(fullName("John", "Doe")).toBe("John Doe")
    expect(fullName(undefined, undefined, "fallback")).toBe("fallback")
  })
  it("derives initials", () => {
    expect(initials("John Doe")).toBe("JD")
    expect(initials(undefined)).toBe("?")
  })
})
