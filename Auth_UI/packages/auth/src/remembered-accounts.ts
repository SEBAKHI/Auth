import { decodeJwt } from "@authsystem/api/jwt"
import { getAccessToken } from "@authsystem/api/token-store"

/**
 * The accounts this browser signed in with, for the sign-in page's "Pick an
 * account" list (owner, 2026-11-22, reversing D-215-4).
 *
 * WHAT IS KEPT. For each account: its id, its address and its name, as the
 * server's access token states them, never as the address field read. Only a
 * sign-in that ended in a session adds one, and only one whose first step was
 * the address (a password, a code or a passkey): an address typed by someone
 * who never got in is never kept, and an account that signs in with Google or
 * Apple keeps its one button (owner, 2026-11-22, Q1 (a)).
 *
 * WHERE. `localStorage` of this origin: the console and the accounts app each
 * keep their own list. A sign-out leaves it, as Microsoft's sign-in does; the
 * list belongs to the browser, not to the person signed in, which is why it is
 * not scoped by user as the table layouts are (`dt:`). On a computer that
 * several people share, the next one sees who signed in here: the price of the
 * reversal, under the private-device default (owner, 2026-10-19). Each entry
 * can be removed from the list, which costs only typing the address again.
 *
 * HOW MANY. Five, newest first. Five rows fit a phone's first screen with the
 * title and "Use a different account" under them; a sixth account pushes out
 * the one used longest ago.
 *
 * Every read and write is wrapped: a browser that blocks storage keeps no list,
 * and the page asks for the address as before. What is read is checked entry
 * by entry, so a damaged value costs the entries it damaged, nothing more.
 */

export const REMEMBERED_ACCOUNTS_KEY = "auth.signIn.accounts"
export const MAX_REMEMBERED_ACCOUNTS = 5

export interface RememberedAccount {
  /** The account's id (the token's `sub`). */
  id: string
  /** The address, as the server holds it. */
  email: string
  /** The account's name, when it has one. */
  name: string | null
}

// Bounds that no real value reaches (an address is at most 254 characters, a
// name part 100), so that a planted or damaged value cannot fill the page.
const MAX_ID = 64
const MAX_EMAIL = 320
const MAX_NAME = 256
const EMAIL_SHAPE = /^[^\s@]+@[^\s@]+$/

function sameId(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase()
}

function sameEmail(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase()
}

/** One entry as read, or null for anything that is not one. */
function toAccount(value: unknown): RememberedAccount | null {
  if (typeof value !== "object" || value === null) return null
  const { id, email, name } = value as Record<string, unknown>
  if (typeof id !== "string" || id.length === 0 || id.length > MAX_ID)
    return null
  if (
    typeof email !== "string" ||
    email.length > MAX_EMAIL ||
    !EMAIL_SHAPE.test(email)
  ) {
    return null
  }
  const cleanName =
    typeof name === "string" &&
    name.trim().length > 0 &&
    name.length <= MAX_NAME
      ? name.trim()
      : null
  return { id, email, name: cleanName }
}

/** The list, newest first: valid entries only, each account once, five at most. */
function normalize(values: readonly unknown[]): RememberedAccount[] {
  const accounts: RememberedAccount[] = []
  for (const value of values) {
    const account = toAccount(value)
    if (!account) continue
    if (
      accounts.some(
        (kept) =>
          sameId(kept.id, account.id) || sameEmail(kept.email, account.email)
      )
    ) {
      continue
    }
    accounts.push(account)
    if (accounts.length === MAX_REMEMBERED_ACCOUNTS) break
  }
  return accounts
}

export function readRememberedAccounts(): RememberedAccount[] {
  try {
    const raw = window.localStorage.getItem(REMEMBERED_ACCOUNTS_KEY)
    if (!raw) return []
    const parsed: unknown = JSON.parse(raw)
    return Array.isArray(parsed) ? normalize(parsed) : []
  } catch {
    return []
  }
}

function write(accounts: readonly RememberedAccount[]): void {
  try {
    if (accounts.length === 0) {
      window.localStorage.removeItem(REMEMBERED_ACCOUNTS_KEY)
      return
    }
    window.localStorage.setItem(
      REMEMBERED_ACCOUNTS_KEY,
      JSON.stringify(accounts)
    )
  } catch {
    // Not kept: the next visit asks for the address, as before the list.
  }
}

/**
 * Puts `account` first: an entry for the same account, or for the same address
 * (an account deleted and made again), gives way to it.
 */
export function rememberAccount(account: RememberedAccount): void {
  const entry = toAccount(account)
  if (!entry) return
  write(normalize([entry, ...readRememberedAccounts()]))
}

/**
 * The account of the session this tab holds right now, as the access token
 * just stored names it. Called by the completion rule when a sign-in ends; a
 * token without an id or an address adds nothing.
 */
export function rememberSignedInAccount(): void {
  const token = getAccessToken()
  const claims = token ? decodeJwt(token) : null
  if (
    !claims ||
    typeof claims.sub !== "string" ||
    typeof claims.email !== "string"
  )
    return
  rememberAccount({
    id: claims.sub,
    email: claims.email,
    name: typeof claims.name === "string" ? claims.name : null,
  })
}

/** Removes the account from the list (no request); returns the list left. */
export function forgetAccount(id: string): RememberedAccount[] {
  const left = readRememberedAccounts().filter(
    (account) => !sameId(account.id, id)
  )
  write(left)
  return left
}

/**
 * The account as the server describes it now (its profile): an entry for it
 * takes the address and the name it has today, where it stands in the list.
 * Never adds one: a person who removed an account from the list does not see
 * it come back by opening the application, and only a sign-in adds.
 */
export function refreshRememberedAccount(account: RememberedAccount): void {
  const entry = toAccount(account)
  if (!entry) return
  const accounts = readRememberedAccounts()
  const index = accounts.findIndex((kept) => sameId(kept.id, entry.id))
  if (index === -1) return
  const kept = accounts[index]
  if (kept.email === entry.email && kept.name === entry.name) return
  const updated = accounts.map((current, at) =>
    at === index ? entry : current
  )
  // An older entry that held the new address (another account, since gone)
  // gives way, as when a sign-in writes.
  write(
    normalize(
      updated.filter(
        (current, at) => at === index || !sameEmail(current.email, entry.email)
      )
    )
  )
}

/** The name of an account as the server forms it: its parts, trimmed, joined by a space. */
export function accountName(
  firstName: string | null | undefined,
  lastName: string | null | undefined
): string | null {
  const name = [firstName, lastName]
    .map((part) => part?.trim() ?? "")
    .filter((part) => part.length > 0)
    .join(" ")
  return name.length > 0 ? name : null
}
