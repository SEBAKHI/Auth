import { beforeEach, describe, expect, it } from "vitest"

import { setAccessToken } from "@authsystem/api/token-store"

import {
  canSkipSignIn,
  clearSignInMarker,
  SIGN_IN_MARKER_KEY,
  SIGN_IN_MARKER_MAX_AGE_MS,
  writeSignInMarker,
} from "./sign-in-marker"
import type { SignInReason } from "./sign-in-reason"

/**
 * OI-275: the marker a sign-in leaves when it resumes a pending request, and
 * the one rule that reads it. Tab storage only, every access wrapped, and
 * nothing in it but the request, the account, the session, the time and the
 * proof.
 */

const REQUEST = `https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${"c".repeat(64)}`
const ENROLL_PASSWORD: SignInReason = { reason: "mfa_enroll", method: "password" }
const ENROLL_CODE: SignInReason = { reason: "mfa_enroll", method: "code" }
const NOW = 1_800_000_000_000

function tokenWith(claims: Record<string, unknown>): string {
  const payload = btoa(JSON.stringify(claims)).replace(/=+$/, "")
  return `h.${payload}.s`
}

const stored = () => window.sessionStorage.getItem(SIGN_IN_MARKER_KEY)

/** Makes the tab's storage throw; returns the undo. */
function blockSessionStorage(): () => void {
  const own = Object.getOwnPropertyDescriptor(window, "sessionStorage")
  Object.defineProperty(window, "sessionStorage", {
    configurable: true,
    get() {
      throw new DOMException("blocked", "SecurityError")
    },
  })
  return () => {
    if (own) Object.defineProperty(window, "sessionStorage", own)
  }
}

beforeEach(() => {
  window.sessionStorage.clear()
  window.localStorage.clear()
  setAccessToken(tokenWith({ sub: "user-1", sid: "session-1", email: "jane@one.example", name: "Jane" }))
})

describe("writing", () => {
  it("keeps the request, the account, the session, the time and the proof, in this tab only", () => {
    writeSignInMarker(REQUEST, "password", NOW)

    expect(JSON.parse(stored() ?? "null")).toEqual({
      request: REQUEST,
      sub: "user-1",
      sid: "session-1",
      at: NOW,
      proof: "password",
    })
    // Nothing else of the token: no address, no name, no token.
    expect(stored()).not.toContain("jane")
    expect(stored()).not.toContain("h.")
    expect(window.localStorage.length).toBe(0)
  })

  it("records an unknown proof as null", () => {
    writeSignInMarker(REQUEST, undefined, NOW)
    expect(JSON.parse(stored() ?? "null")).toMatchObject({ proof: null })
  })

  it("leaves no marker for a token without sub or sid, nor without a token", () => {
    window.sessionStorage.setItem(SIGN_IN_MARKER_KEY, "older")
    setAccessToken(tokenWith({ sid: "session-1" }))
    writeSignInMarker(REQUEST, "password", NOW)
    expect(stored()).toBeNull()

    setAccessToken(tokenWith({ sub: "user-1" }))
    writeSignInMarker(REQUEST, "password", NOW)
    expect(stored()).toBeNull()

    setAccessToken(null)
    writeSignInMarker(REQUEST, "password", NOW)
    expect(stored()).toBeNull()
  })

  it("overwrites a marker for another request", () => {
    writeSignInMarker("https://api.example.com/other", "code", NOW - 1000)
    writeSignInMarker(REQUEST, "password", NOW)
    expect(JSON.parse(stored() ?? "null")).toMatchObject({ request: REQUEST, proof: "password" })
  })

  it("does not throw where storage is blocked", () => {
    const undo = blockSessionStorage()
    try {
      expect(() => writeSignInMarker(REQUEST, "password", NOW)).not.toThrow()
      expect(() => clearSignInMarker()).not.toThrow()
      expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW)).toBe(false)
    } finally {
      undo()
    }
  })
})

describe("the rule", () => {
  beforeEach(() => writeSignInMarker(REQUEST, "password", NOW))

  it("skips for the same request, account and session within two minutes", () => {
    expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW)).toBe(true)
    expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW + SIGN_IN_MARKER_MAX_AGE_MS)).toBe(true)
  })

  it("does not skip past two minutes, nor for a marker from the future", () => {
    expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW + SIGN_IN_MARKER_MAX_AGE_MS + 1)).toBe(false)
    expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW - 1)).toBe(false)
  })

  it("does not skip for another request, or without one", () => {
    expect(canSkipSignIn(`${REQUEST}0`, ENROLL_PASSWORD, NOW)).toBe(false)
    expect(canSkipSignIn(null, ENROLL_PASSWORD, NOW)).toBe(false)
  })

  it("does not skip for any reason but mfa_enroll, nor without one", () => {
    expect(canSkipSignIn(REQUEST, { reason: "mfa_step_up", method: "password" }, NOW)).toBe(false)
    expect(canSkipSignIn(REQUEST, { reason: "mfa_reauthenticate", method: "password" }, NOW)).toBe(false)
    expect(canSkipSignIn(REQUEST, null, NOW)).toBe(false)
  })

  it("does not skip once another account or another session holds the tab", () => {
    setAccessToken(tokenWith({ sub: "user-2", sid: "session-1" }))
    expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW)).toBe(false)
    setAccessToken(tokenWith({ sub: "user-1", sid: "session-2" }))
    expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW)).toBe(false)
    setAccessToken(null)
    expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW)).toBe(false)
  })

  it("does not skip once spent", () => {
    clearSignInMarker()
    expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW)).toBe(false)
  })

  it("does not skip for a marker it cannot read", () => {
    for (const raw of ["{", "null", "42", JSON.stringify({ request: REQUEST, sub: "user-1", sid: "session-1", at: "now" })]) {
      window.sessionStorage.setItem(SIGN_IN_MARKER_KEY, raw)
      expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW), raw).toBe(false)
    }
  })
})

describe("the first step the set-up will accept (T5)", () => {
  const cases: Array<[string, "password" | "code" | "external" | undefined, SignInReason, boolean]> = [
    ["a typed password", "password", ENROLL_PASSWORD, true],
    ["an external identity", "external", ENROLL_PASSWORD, true],
    ["an emailed code alone, for an account with a password", "code", ENROLL_PASSWORD, false],
    ["a sign-in the screen did not name, for an account with a password", undefined, ENROLL_PASSWORD, false],
    ["an emailed code, for an account without a password", "code", ENROLL_CODE, true],
    ["a sign-in the screen did not name, for an account without a password", undefined, ENROLL_CODE, true],
    ["an emailed code, with no method word", "code", { reason: "mfa_enroll", method: null }, false],
    ["a typed password, with no method word", "password", { reason: "mfa_enroll", method: null }, true],
  ]

  for (const [name, proof, reason, skips] of cases) {
    it(`${skips ? "skips" : "does not skip"} after ${name}`, () => {
      writeSignInMarker(REQUEST, proof, NOW)
      expect(canSkipSignIn(REQUEST, reason, NOW)).toBe(skips)
    })
  }

  it("reads an unknown proof word as unknown", () => {
    window.sessionStorage.setItem(
      SIGN_IN_MARKER_KEY,
      JSON.stringify({ request: REQUEST, sub: "user-1", sid: "session-1", at: NOW, proof: "passkey" })
    )
    expect(canSkipSignIn(REQUEST, ENROLL_PASSWORD, NOW)).toBe(false)
  })
})

/**
 * The provider buttons end their sign-ins through `complete` without a page of
 * their own to test it on (the provider libraries are not loaded under test),
 * so the source is the test: every completion they make names its proof.
 */
describe("the provider buttons name their proof", () => {
  const sources = import.meta.glob(["./external/google-sign-in.tsx", "./external/apple-sign-in.tsx"], {
    eager: true,
    query: "?raw",
    import: "default",
  }) as Record<string, string>

  it("reads both buttons", () => {
    expect(Object.keys(sources).sort()).toEqual([
      "./external/apple-sign-in.tsx",
      "./external/google-sign-in.tsx",
    ])
  })

  for (const [file, source] of Object.entries(sources)) {
    it(`${file} completes with the external proof every time`, () => {
      const completions = source.match(/\bcomplete\(/g) ?? []
      const withProof = source.match(/\bcomplete\(result, \{ proof: "external" \}\)/g) ?? []
      // A floor: a button that no longer completes cannot pass as compliant.
      expect(completions.length).toBeGreaterThan(0)
      expect(withProof.length).toBe(completions.length)
    })
  }
})
