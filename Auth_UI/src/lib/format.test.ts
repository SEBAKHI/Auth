import { describe, expect, it } from "vitest"

import {
  formatDate,
  fullName,
  initials,
  secretStatusMeta,
  userStatusMeta,
} from "./format"

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
