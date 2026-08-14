import { afterEach, describe, expect, it } from "vitest"

import {
  daysUntil,
  formatDate,
  formatDateTime,
  fullName,
  initials,
  secretStatusMeta,
  userStatusMeta,
} from "./format"
import { setActiveTimeZone } from "@authsystem/i18n/timezone"

describe("daysUntil", () => {
  it("returns null for empty or unparseable values", () => {
    expect(daysUntil(null)).toBeNull()
    expect(daysUntil(undefined)).toBeNull()
    expect(daysUntil("")).toBeNull()
    expect(daysUntil("not-a-date")).toBeNull()
  })

  it("counts forward to a future instant", () => {
    const future = new Date(Date.now() + 10 * 86_400_000).toISOString()
    const days = daysUntil(future)
    expect(days).toBeGreaterThanOrEqual(9)
    expect(days).toBeLessThanOrEqual(10)
  })

  it("goes negative once the instant is past", () => {
    // The sign is what separates "expiring soon" from "already expired" on both
    // key pages, so it has to survive.
    const past = new Date(Date.now() - 3 * 86_400_000).toISOString()
    expect(daysUntil(past)).toBeLessThan(0)
  })

  it("reads an offset-less datetime as UTC", () => {
    // The API emits UTC with a Z. A payload that lost it must not be read as
    // local time: at a 14-day horizon that shift flips the bucket for edge rows.
    const future = new Date(Date.now() + 5 * 86_400_000)
      .toISOString()
      .replace("Z", "")
    expect(daysUntil(future)).toBeGreaterThanOrEqual(4)
  })
})

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

  // The API serializes enums as string NAMES, which is the only form the
  // secrets page ever receives. The original lookup ran Number("Configured")
  // -> NaN and fell through to "unknown", so every row on the page showed
  // "Unknown" regardless of the real state — and this test suite missed it by
  // only ever passing numbers, a shape production never produces.
  it("maps the serialized enum names the API actually sends", () => {
    expect(secretStatusMeta("Configured").key).toBe("configured")
    expect(secretStatusMeta("NotConfigured").key).toBe("notConfigured")
    expect(secretStatusMeta("Empty").key).toBe("empty")
  })

  it("is insensitive to casing", () => {
    expect(secretStatusMeta("configured").key).toBe("configured")
    expect(secretStatusMeta("NOTCONFIGURED").key).toBe("notConfigured")
  })

  it("still accepts a numeric string", () => {
    expect(secretStatusMeta("1").key).toBe("configured")
  })

  it("falls back to unknown for absent or unrecognised values", () => {
    expect(secretStatusMeta(undefined).key).toBe("unknown")
    expect(secretStatusMeta("Nonsense").key).toBe("unknown")
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
