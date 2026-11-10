import { describe, expect, it } from "vitest"

import {
  readSignInReason,
  SIGN_IN_REASONS,
  signInReasonQuery,
  validateSignInReason,
} from "./sign-in-reason"

describe("readSignInReason", () => {
  it.each(SIGN_IN_REASONS)("reads %s with its method word", (word) => {
    expect(readSignInReason(`?reason=${word}&method=password`)).toEqual({
      reason: word,
      method: "password",
    })
    expect(readSignInReason(`?client_id=app&reason=${word}&method=code`)).toEqual({
      reason: word,
      method: "code",
    })
  })

  it("allows exactly four reasons", () => {
    expect(SIGN_IN_REASONS).toEqual([
      "mfa_step_up",
      "mfa_enroll",
      "mfa_reauthenticate",
      "mfa_passkey",
    ])
  })

  it("reads mfa_passkey now that the passkey button exists (S20 PR C, row 285 (a))", () => {
    expect(readSignInReason("?reason=mfa_passkey&method=password")).toEqual({
      reason: "mfa_passkey",
      method: "password",
    })
    expect(readSignInReason("?reason=mfa_passkey")).toEqual({
      reason: "mfa_passkey",
      method: null,
    })
  })

  it.each(["mfa_other", "MFA_STEP_UP", "mfa_step_up ", "", "login"])(
    "ignores the unknown word %j",
    (word) => {
      expect(readSignInReason(`?reason=${encodeURIComponent(word)}`)).toBeNull()
    }
  )

  it("ignores a method word without a reason", () => {
    expect(readSignInReason("?method=code")).toBeNull()
    expect(readSignInReason("")).toBeNull()
  })

  it("keeps the reason and drops an unknown method word", () => {
    expect(readSignInReason("?reason=mfa_enroll&method=passkey")).toEqual({
      reason: "mfa_enroll",
      method: null,
    })
    expect(readSignInReason("?reason=mfa_enroll")).toEqual({
      reason: "mfa_enroll",
      method: null,
    })
  })
})

describe("validateSignInReason", () => {
  it("re-checks what router state or the store hold", () => {
    expect(validateSignInReason({ reason: "mfa_step_up", method: "code" })).toEqual({
      reason: "mfa_step_up",
      method: "code",
    })
    expect(validateSignInReason({ reason: "mfa_passkey", method: "code" })).toEqual({
      reason: "mfa_passkey",
      method: "code",
    })
    expect(validateSignInReason({ reason: "MFA_PASSKEY", method: "code" })).toBeNull()
    expect(validateSignInReason("mfa_step_up")).toBeNull()
    expect(validateSignInReason(null)).toBeNull()
    expect(validateSignInReason(undefined)).toBeNull()
    expect(validateSignInReason({ method: "password" })).toBeNull()
  })
})

describe("signInReasonQuery", () => {
  it("writes the reason, then the method word", () => {
    expect(signInReasonQuery({ reason: "mfa_enroll", method: "code" })).toBe(
      "&reason=mfa_enroll&method=code"
    )
    expect(signInReasonQuery({ reason: "mfa_step_up", method: null })).toBe(
      "&reason=mfa_step_up"
    )
  })

  it("writes nothing for no reason, or one the allow-list refuses", () => {
    expect(signInReasonQuery(null)).toBe("")
    expect(signInReasonQuery(undefined)).toBe("")
    expect(
      signInReasonQuery({ reason: "mfa_otp", method: "code" } as never)
    ).toBe("")
    expect(signInReasonQuery({ reason: "mfa_passkey", method: "code" })).toBe(
      "&reason=mfa_passkey&method=code"
    )
  })
})

// ── one module owns the words, and every link that holds a reason passes it ──

const sources = import.meta.glob(
  [
    "../../../apps/*/src/**/*.{ts,tsx}",
    "../../*/src/**/*.{ts,tsx}",
    "!../../../**/*.test.{ts,tsx}",
  ],
  { eager: true, query: "?raw", import: "default" }
) as Record<string, string>

const HERE = import.meta.url
const WORKSPACE_ROOT = HERE.slice(0, HERE.lastIndexOf("/packages/auth/src/") + 1)
const files = Object.entries(sources).map(([key, source]) => ({
  path: new URL(key, HERE).href.slice(WORKSPACE_ROOT.length),
  source,
}))

// The pages that read the reason and link on with the pending request (S23).
const CARRIERS = [
  "packages/auth/src/pages/login.tsx",
  "packages/auth/src/pages/two-factor-verify.tsx",
  "packages/auth/src/pages/verify-email-page.tsx",
  "apps/accounts/src/pages/auth/two-factor.tsx",
]

// The words written or read by hand: as a query parameter, or by name.
const WORD_LITERAL = /[?&](reason|method)=|\.get\((["'])(reason|method)\2\)/

describe("one module owns the sign-in reason words", () => {
  it("scans the sources at all", () => {
    expect(files.length).toBeGreaterThan(100)
    expect(files.map((file) => file.path)).toEqual(expect.arrayContaining(CARRIERS))
    expect(files.map((file) => file.path)).toContain("packages/auth/src/sign-in-reason.ts")
  })

  it("finds no reason or method literal outside sign-in-reason.ts", () => {
    const offenders = files
      .filter((file) => file.path !== "packages/auth/src/sign-in-reason.ts")
      .filter((file) => WORD_LITERAL.test(file.source))
      .map((file) => file.path)

    expect(offenders).toEqual([])
  })

  it.each(CARRIERS)("%s passes the reason to every toAccountsSearch", (path) => {
    const source = files.find((file) => file.path === path)?.source ?? ""
    const calls = source.match(/\btoAccountsSearch\([^)]*\)/g) ?? []

    expect(calls.length).toBeGreaterThan(0)
    expect(calls.filter((call) => !/,\s*reason\s*\)$/.test(call))).toEqual([])
  })
})
