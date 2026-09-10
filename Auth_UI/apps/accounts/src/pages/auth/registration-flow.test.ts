import { beforeEach, describe, expect, it } from "vitest"

import {
  clearRegistrationFlow,
  clearVerifiedCode,
  getVerifiedCode,
  PENDING_REGISTRATION_STORAGE_KEY,
  readPendingRegistration,
  savePendingRegistration,
  setVerifiedCode,
} from "./registration-flow"

/** Values only: the test stub for localStorage is not JSON-serialisable. */
function everyStoredValue(storage: Storage): string[] {
  const values: string[] = []
  for (let index = 0; index < storage.length; index += 1) {
    const key = storage.key(index)
    if (key !== null) values.push(storage.getItem(key) ?? "")
  }
  return values
}

const PENDING = {
  pendingId: "handle-1",
  email: "jane@one.example",
  maskedEmail: "j***@one.example",
  expiresAt: "2026-09-10T10:05:00.000Z",
}

describe("registration flow store", () => {
  beforeEach(() => {
    clearRegistrationFlow()
    window.sessionStorage.clear()
  })

  it("keeps the identity in session storage and the code in memory only", () => {
    savePendingRegistration(PENDING)
    setVerifiedCode("123456")

    expect(readPendingRegistration()).toEqual(PENDING)
    expect(
      window.sessionStorage.getItem(PENDING_REGISTRATION_STORAGE_KEY)
    ).toContain("handle-1")
    // The proof never touches storage of either kind: a reload forgets it by
    // design, and another tab must never find it.
    expect(JSON.stringify(window.sessionStorage)).not.toContain("123456")
    expect(everyStoredValue(window.localStorage).join("|")).not.toContain("123456")
    expect(getVerifiedCode()).toBe("123456")
  })

  it("drops the code whenever a new one is issued", () => {
    savePendingRegistration(PENDING)
    setVerifiedCode("123456")

    savePendingRegistration({ ...PENDING, expiresAt: "2026-09-10T10:10:00.000Z" })

    expect(getVerifiedCode()).toBeNull()
  })

  it("forgets the code but keeps the identity when only the proof is withdrawn", () => {
    savePendingRegistration(PENDING)
    setVerifiedCode("123456")

    clearVerifiedCode()

    expect(getVerifiedCode()).toBeNull()
    expect(readPendingRegistration()).toEqual(PENDING)
  })

  it("forgets both when the flow ends", () => {
    savePendingRegistration(PENDING)
    setVerifiedCode("123456")

    clearRegistrationFlow()

    expect(getVerifiedCode()).toBeNull()
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
