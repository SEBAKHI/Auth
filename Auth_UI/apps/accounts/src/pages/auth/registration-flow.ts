/**
 * What the three sign-up screens share, and where each part of it lives.
 *
 * Verify-first sign-up is three requests over three screens: an address
 * (`/register`), a code (`/register/verify`), then a name and a password
 * (`/register/complete`). The server creates nothing until the third; until
 * then the browser holds two things of different worth, and they are kept in
 * different places on purpose.
 *
 * The IDENTITY — the pending handle, the address it was issued for, its masked
 * form and its expiry — lives in `sessionStorage`. It has to survive a reload:
 * a person who refreshes the code screen while the message is still arriving
 * must land back on the code screen, not at the start with a new code on the
 * way. The handle alone opens nothing (every follow-up request presents the
 * code again), and session storage is per tab and gone when the tab closes.
 *
 * The CODE lives in module memory only, like the 2FA challenge in
 * `pending-challenge.ts`. It is the proof of the mailbox, and the completion
 * request spends it; writing it to storage would leave a live proof lying next
 * to the handle for the life of the tab. A reload of the password screen
 * therefore forgets the code and returns to the code screen, which is the
 * intended trade: retyping six digits costs seconds, a stored proof costs a
 * sign-up under someone else's name. Never carry it in router state either — a
 * `navigate(..., { state })` value is written to the history entry, which
 * persists across reloads and can be read from `history.state`.
 *
 * The pending OAuth request (`returnTo`) is stored NOWHERE here: it travels in
 * the query string from screen to screen, and `useLoginCompletion` re-validates
 * it at every hop against the same allow-list as the sign-in screens.
 */

export const PENDING_REGISTRATION_STORAGE_KEY = "auth.registration.pending"

export interface PendingRegistration {
  /** The opaque handle the server issued; it names the row, it opens nothing. */
  pendingId: string
  /** The address as typed, needed to request a fresh code and to prefill. */
  email: string
  /** The address as the server shows it back, for the code screen's subtitle. */
  maskedEmail: string
  /** ISO instant the current code stops being accepted. */
  expiresAt: string
}

let verifiedCode: string | null = null

function isPendingRegistration(value: unknown): value is PendingRegistration {
  if (!value || typeof value !== "object") return false
  const record = value as Record<string, unknown>
  return (
    typeof record.pendingId === "string" &&
    record.pendingId.length > 0 &&
    typeof record.email === "string" &&
    record.email.length > 0 &&
    typeof record.maskedEmail === "string" &&
    typeof record.expiresAt === "string" &&
    !Number.isNaN(Date.parse(record.expiresAt))
  )
}

/** The pending sign-up this tab is in the middle of, or null. */
export function readPendingRegistration(): PendingRegistration | null {
  try {
    const raw = window.sessionStorage.getItem(PENDING_REGISTRATION_STORAGE_KEY)
    if (!raw) return null
    const parsed: unknown = JSON.parse(raw)
    return isPendingRegistration(parsed) ? parsed : null
  } catch {
    // Storage may be unavailable or the value may be garbage; either way the
    // flow starts over at the address, which is the safe direction.
    return null
  }
}

/**
 * Records a code having been issued. Every issue voids the code before it —
 * the server rotates in place — so the proof held in memory is dropped too.
 */
export function savePendingRegistration(pending: PendingRegistration): void {
  verifiedCode = null
  try {
    window.sessionStorage.setItem(
      PENDING_REGISTRATION_STORAGE_KEY,
      JSON.stringify(pending)
    )
  } catch {
    // Without storage the flow still works within the page; only a reload
    // would send the person back to the address.
  }
}

/** The code the server accepted on the code screen, held for the completion. */
export function getVerifiedCode(): string | null {
  return verifiedCode
}

export function setVerifiedCode(code: string): void {
  verifiedCode = code
}

/**
 * Forgets the proof but keeps the identity: the completion step was refused
 * on the code (rotated, expired, spent), and the person goes back to the code
 * screen for the same pending sign-up.
 */
export function clearVerifiedCode(): void {
  verifiedCode = null
}

/** Forgets everything: the sign-up finished, or the person chose another address. */
export function clearRegistrationFlow(): void {
  verifiedCode = null
  try {
    window.sessionStorage.removeItem(PENDING_REGISTRATION_STORAGE_KEY)
  } catch {
    // Nothing to remove where nothing could be stored.
  }
}
