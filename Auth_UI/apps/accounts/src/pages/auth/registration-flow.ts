/**
 * What the sign-up screens share, and where each part of it lives.
 *
 * Sign-up starts with an address (`/register`) and a code (`/register/verify`);
 * once the code matched, the same screen asks for what the deployment wants of a
 * new account, or signs in the account the address already has (OI-215). The
 * server creates nothing until the last request.
 *
 * The IDENTITY — the pending handle, the address it was issued for, its masked
 * form and its expiry — lives in `sessionStorage`. It has to survive a reload:
 * a person who refreshes the code screen while the message is still arriving
 * must land back on the code screen, not at the start with a new code on the
 * way. The handle alone opens nothing (every follow-up request presents the
 * code again), and session storage is per tab and gone when the tab closes.
 *
 * The CODE is never stored: it is the proof of the mailbox, and it lives only
 * in the code flow's component state until it is spent. A reload after it
 * matched asks for it again, which is the intended trade: retyping six digits
 * costs seconds, a stored proof costs a sign-up under someone else's name.
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

/** Records a code having been issued (the server rotates in place). */
export function savePendingRegistration(pending: PendingRegistration): void {
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

/** Forgets everything: the sign-up finished, or the person chose another address. */
export function clearRegistrationFlow(): void {
  try {
    window.sessionStorage.removeItem(PENDING_REGISTRATION_STORAGE_KEY)
  } catch {
    // Nothing to remove where nothing could be stored.
  }
}
