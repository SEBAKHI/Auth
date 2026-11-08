import { render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import {
  PasskeyRequiredProvider,
  passkeyRequiredNotice,
  usePasskeyRequiredReporter,
} from "./passkey-required"

/**
 * S20 PR C, IN 5 (D-S20-4 (ii-s)): an account that administers the platform and
 * whose only usable second factor is a passkey signs in with that passkey alone.
 * Every other door answers `Auth.PasskeyRequired`, and every page behind such a
 * door shows the API's own sentence where the person is looking and offers the
 * passkey. One reader decides «this answer asks for the passkey»; the guard
 * below fails for a sign-in or recovery caller that does not ask it.
 */

const PASSKEY_REQUIRED = {
  status: 403,
  code: "Auth.PasskeyRequired",
  detail: "This account signs in with its passkey.",
}

describe("passkeyRequiredNotice", () => {
  it("reads the API's sentence for Auth.PasskeyRequired", () => {
    expect(passkeyRequiredNotice(PASSKEY_REQUIRED)).toBe(PASSKEY_REQUIRED.detail)
  })

  it.each([
    ["another refusal", { status: 403, code: "User.EmailNotConfirmed", detail: "Confirm it." }],
    ["a status alone", { status: 403 }],
    ["a network failure", new TypeError("Failed to fetch")],
    ["nothing", undefined],
  ])("answers null for %s", (_label, error) => {
    expect(passkeyRequiredNotice(error)).toBeNull()
  })
})

describe("usePasskeyRequiredReporter", () => {
  function Door({ error, onHanded }: { error: unknown; onHanded: (handed: boolean) => void }) {
    const report = usePasskeyRequiredReporter()
    return (
      <button type="button" onClick={() => onHanded(report(error))}>
        sign in
      </button>
    )
  }

  it("hands the sentence to the page that offers the passkey, and says it did", () => {
    const onPasskeyRequired = vi.fn()
    const onHanded = vi.fn()
    render(
      <PasskeyRequiredProvider onPasskeyRequired={onPasskeyRequired}>
        <Door error={PASSKEY_REQUIRED} onHanded={onHanded} />
      </PasskeyRequiredProvider>
    )

    screen.getByRole("button", { name: "sign in" }).click()

    expect(onPasskeyRequired).toHaveBeenCalledWith(PASSKEY_REQUIRED.detail)
    expect(onHanded).toHaveBeenCalledWith(true)
  })

  it("leaves any other refusal to the door, as before", () => {
    const onPasskeyRequired = vi.fn()
    const onHanded = vi.fn()
    render(
      <PasskeyRequiredProvider onPasskeyRequired={onPasskeyRequired}>
        <Door error={{ status: 401, code: "User.InvalidCredentials" }} onHanded={onHanded} />
      </PasskeyRequiredProvider>
    )

    screen.getByRole("button", { name: "sign in" }).click()

    expect(onPasskeyRequired).not.toHaveBeenCalled()
    expect(onHanded).toHaveBeenCalledWith(false)
  })

  it("leaves it to the door on a page that offers no passkey", () => {
    const onHanded = vi.fn()
    render(<Door error={PASSKEY_REQUIRED} onHanded={onHanded} />)

    screen.getByRole("button", { name: "sign in" }).click()

    expect(onHanded).toHaveBeenCalledWith(false)
  })
})

// ── every sign-in and recovery caller asks the one reader ──────────────────────

const sources = import.meta.glob(
  [
    "../../../../apps/*/src/**/*.{ts,tsx}",
    "../../../*/src/**/*.{ts,tsx}",
    "!../../../../**/*.test.{ts,tsx}",
  ],
  { eager: true, query: "?raw", import: "default" }
) as Record<string, string>

const HERE = import.meta.url
const WORKSPACE_ROOT = HERE.slice(0, HERE.lastIndexOf("/packages/auth/src/") + 1)
const files = Object.entries(sources).map(([key, source]) => ({
  path: new URL(key, HERE).href.slice(WORKSPACE_ROOT.length),
  source,
}))

/**
 * The context's doors that can answer `Auth.PasskeyRequired` (PR B IN 6): the
 * password, a provider, the emailed code, the three recovery entries, the sign-in
 * after a verified address and the second step. The two passkey methods are the
 * way the sentence points to, and cannot answer it.
 */
const DOORS = [
  "login",
  "loginExternal",
  "recoverAccount",
  "recoverAccountExternal",
  "completeTwoFactor",
  "completeEmailVerification",
  "signInWithEmailCode",
  "recoverWithEmailCode",
]

/**
 * A caller that cannot meet the answer, with the reason. Accepting an invitation
 * signs in the account it has just registered: no passkey can exist for it yet.
 */
const EXEMPT: Record<string, string> = {
  "packages/auth/src/pages/accept-invitation.tsx": "signs in an account registered a moment before",
}

const READERS = /\b(passkeyRequiredNotice|usePasskeyRequiredReporter)\b/

/** The doors a file takes from `useAuth()`. */
function doorsOf(source: string): string[] {
  const found = new Set<string>()
  for (const match of source.matchAll(/const\s*\{([^}]*)\}\s*=\s*useAuth\(\)/g)) {
    for (const part of match[1].split(",")) {
      const name = part.trim().split(":")[0].trim()
      if (DOORS.includes(name)) found.add(name)
    }
  }
  for (const match of source.matchAll(/useAuth\(\)\.(\w+)/g)) {
    if (DOORS.includes(match[1])) found.add(match[1])
  }
  return [...found]
}

const callers = files.filter((file) => doorsOf(file.source).length > 0)

describe("every sign-in and recovery caller asks the one reader (IN 5)", () => {
  it("finds the callers at all", () => {
    expect(files.length).toBeGreaterThan(100)
    expect(callers.map((file) => file.path)).toEqual(
      expect.arrayContaining([
        "apps/accounts/src/pages/account-recovery.tsx",
        "packages/auth/src/external/apple-sign-in.tsx",
        "packages/auth/src/external/google-sign-in.tsx",
        "packages/auth/src/pages/email-code-flow.tsx",
        "packages/auth/src/pages/login.tsx",
        "packages/auth/src/pages/two-factor-verify.tsx",
        "packages/auth/src/pages/verify-email-page.tsx",
      ])
    )
  })

  it.each(callers.map((file) => [file.path, file] as const))("%s", (path, file) => {
    if (EXEMPT[path]) {
      expect(EXEMPT[path].length, "an exemption says why").toBeGreaterThan(0)
      return
    }
    expect(
      READERS.test(file.source),
      `${path} takes ${doorsOf(file.source).join(", ")} from useAuth() without asking passkeyRequiredNotice or usePasskeyRequiredReporter`
    ).toBe(true)
  })

  it("keeps each exemption to a file that still calls a door", () => {
    for (const path of Object.keys(EXEMPT)) {
      expect(callers.map((file) => file.path)).toContain(path)
    }
  })
})
