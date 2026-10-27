import { beforeEach, describe, expect, it } from "vitest"

import {
  clearRegistrationFlow,
  PENDING_REGISTRATION_STORAGE_KEY,
  readPendingRegistration,
  savePendingRegistration,
} from "./registration-flow"

const PENDING = {
  pendingId: "handle-1",
  email: "jane@one.example",
  maskedEmail: "j***@one.example",
  expiresAt: "2026-09-10T10:05:00.000Z",
}

describe("registration flow store", () => {
  beforeEach(() => {
    window.sessionStorage.clear()
  })

  it("keeps the identity in session storage", () => {
    savePendingRegistration(PENDING)

    expect(readPendingRegistration()).toEqual(PENDING)
    expect(
      window.sessionStorage.getItem(PENDING_REGISTRATION_STORAGE_KEY)
    ).toContain("handle-1")
  })

  it("replaces the identity when a new code is issued", () => {
    savePendingRegistration(PENDING)

    savePendingRegistration({ ...PENDING, expiresAt: "2026-09-10T10:10:00.000Z" })

    expect(readPendingRegistration()?.expiresAt).toBe("2026-09-10T10:10:00.000Z")
  })

  it("forgets the identity when the flow ends", () => {
    savePendingRegistration(PENDING)

    clearRegistrationFlow()

    expect(readPendingRegistration()).toBeNull()
    expect(
      window.sessionStorage.getItem(PENDING_REGISTRATION_STORAGE_KEY)
    ).toBeNull()
  })

  it.each([
    ["garbage", "not json"],
    ["a missing handle", JSON.stringify({ ...PENDING, pendingId: "" })],
    ["an unparseable expiry", JSON.stringify({ ...PENDING, expiresAt: "soon" })],
    ["a number for the address", JSON.stringify({ ...PENDING, email: 7 })],
  ])("reads %s in storage as no pending sign-up", (_label, raw) => {
    window.sessionStorage.setItem(PENDING_REGISTRATION_STORAGE_KEY, raw)

    expect(readPendingRegistration()).toBeNull()
  })
})
