import { afterEach, beforeEach, describe, expect, it } from "vitest"

import { setAccessToken } from "@authsystem/api/token-store"

import {
  accountName,
  forgetAccount,
  MAX_REMEMBERED_ACCOUNTS,
  readRememberedAccounts,
  refreshRememberedAccount,
  rememberAccount,
  rememberSignedInAccount,
  REMEMBERED_ACCOUNTS_KEY,
  type RememberedAccount,
} from "./remembered-accounts"

/**
 * The browser's list of accounts that signed in here (owner, 2026-11-22,
 * reversing D-215-4): newest first, each account once, five at most; removed
 * locally; refreshed from the profile, never added by it.
 */

/** A token the client can decode: the signature is never checked here. */
function tokenFor(claims: Record<string, unknown>): string {
  const encode = (value: unknown) =>
    btoa(JSON.stringify(value))
      .replace(/\+/g, "-")
      .replace(/\//g, "_")
      .replace(/=+$/, "")
  return `${encode({ alg: "none" })}.${encode(claims)}.sig`
}

function account(
  n: number,
  name: string | null = `Person ${n}`
): RememberedAccount {
  return { id: `id-${n}`, email: `person${n}@example.com`, name }
}

function emails(): string[] {
  return readRememberedAccounts().map((entry) => entry.email)
}

/** Makes the storage accessor throw; returns the undo. */
function blockLocalStorage(): () => void {
  const own = Object.getOwnPropertyDescriptor(window, "localStorage")
  Object.defineProperty(window, "localStorage", {
    configurable: true,
    get() {
      throw new DOMException("blocked", "SecurityError")
    },
  })
  return () => {
    if (own) Object.defineProperty(window, "localStorage", own)
  }
}

beforeEach(() => {
  window.localStorage.clear()
  setAccessToken(null)
})

afterEach(() => {
  setAccessToken(null)
})

describe("the remembered accounts", () => {
  it("puts the latest sign-in first, and an account signing in again moves up", () => {
    rememberAccount(account(1))
    rememberAccount(account(2))
    rememberAccount(account(1))

    expect(emails()).toEqual(["person1@example.com", "person2@example.com"])
  })

  it("keeps five, and a sixth pushes out the one used longest ago", () => {
    for (let n = 1; n <= MAX_REMEMBERED_ACCOUNTS + 1; n += 1)
      rememberAccount(account(n))

    expect(emails()).toEqual([
      "person6@example.com",
      "person5@example.com",
      "person4@example.com",
      "person3@example.com",
      "person2@example.com",
    ])
  })

  it("keeps one entry per address: a new account with an old address replaces its entry", () => {
    rememberAccount(account(1))
    rememberAccount({ id: "id-new", email: "PERSON1@example.com", name: null })

    expect(readRememberedAccounts()).toEqual([
      { id: "id-new", email: "PERSON1@example.com", name: null },
    ])
  })

  it("remembers the account of the token just stored: its id, address and name", () => {
    setAccessToken(
      tokenFor({ sub: "id-9", email: "nine@example.com", name: "Nine Person" })
    )

    rememberSignedInAccount()

    expect(readRememberedAccounts()).toEqual([
      { id: "id-9", email: "nine@example.com", name: "Nine Person" },
    ])
  })

  it("remembers an account without a name by its address alone", () => {
    setAccessToken(tokenFor({ sub: "id-9", email: "nine@example.com" }))

    rememberSignedInAccount()

    expect(readRememberedAccounts()).toEqual([
      { id: "id-9", email: "nine@example.com", name: null },
    ])
  })

  it("remembers nothing without a token, or from one without an id or an address", () => {
    rememberSignedInAccount()
    setAccessToken(tokenFor({ email: "nine@example.com" }))
    rememberSignedInAccount()
    setAccessToken(tokenFor({ sub: "id-9" }))
    rememberSignedInAccount()

    expect(window.localStorage.getItem(REMEMBERED_ACCOUNTS_KEY)).toBeNull()
  })

  it("forgets one account without touching the others, and leaves no key once empty", () => {
    rememberAccount(account(1))
    rememberAccount(account(2))

    expect(forgetAccount("id-2").map((entry) => entry.id)).toEqual(["id-1"])
    expect(emails()).toEqual(["person1@example.com"])

    expect(forgetAccount("id-1")).toEqual([])
    expect(window.localStorage.getItem(REMEMBERED_ACCOUNTS_KEY)).toBeNull()
  })

  it("takes a changed address and name from the profile, where the entry stands", () => {
    rememberAccount(account(1))
    rememberAccount(account(2))

    refreshRememberedAccount({
      id: "ID-1",
      email: "renamed@example.com",
      name: "Renamed",
    })

    expect(readRememberedAccounts()).toEqual([
      account(2),
      { id: "ID-1", email: "renamed@example.com", name: "Renamed" },
    ])
  })

  it("never adds an account from the profile: a removed one does not come back", () => {
    rememberAccount(account(1))
    forgetAccount("id-1")

    refreshRememberedAccount(account(1))

    expect(readRememberedAccounts()).toEqual([])
  })

  it("reads anything but a list of entries as no list, and drops entries that are not ones", () => {
    window.localStorage.setItem(REMEMBERED_ACCOUNTS_KEY, "{not json")
    expect(readRememberedAccounts()).toEqual([])

    window.localStorage.setItem(
      REMEMBERED_ACCOUNTS_KEY,
      JSON.stringify({ id: "x" })
    )
    expect(readRememberedAccounts()).toEqual([])

    window.localStorage.setItem(
      REMEMBERED_ACCOUNTS_KEY,
      JSON.stringify([
        "person@example.com",
        { id: "", email: "a@example.com" },
        { id: "id-1", email: "not an address" },
        { id: "id-2", email: `${"a".repeat(400)}@example.com` },
        { id: "id-3", email: "ok@example.com", name: 42 },
      ])
    )
    expect(readRememberedAccounts()).toEqual([
      { id: "id-3", email: "ok@example.com", name: null },
    ])
  })

  it("keeps no list and throws nothing where storage cannot be reached", () => {
    const undo = blockLocalStorage()
    try {
      setAccessToken(tokenFor({ sub: "id-9", email: "nine@example.com" }))
      expect(() => rememberSignedInAccount()).not.toThrow()
      expect(() => forgetAccount("id-9")).not.toThrow()
      expect(() => refreshRememberedAccount(account(9))).not.toThrow()
      expect(readRememberedAccounts()).toEqual([])
    } finally {
      undo()
    }
  })

  it("forms the name as the server does: the parts that are not blank, joined by a space", () => {
    expect(accountName(" Ada ", " Lovelace ")).toBe("Ada Lovelace")
    expect(accountName("Ada", "  ")).toBe("Ada")
    expect(accountName("", null)).toBeNull()
  })
})
