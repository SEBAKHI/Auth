/**
 * Telling the browser which passkeys the account still has (S20 PR C, owner
 * decision D-S20-7).
 *
 * A passkey removed from the account — on the security page, or by a reset, a
 * lock or a password change — stays in the person's password manager, which
 * keeps offering it at sign-in, and the sign-in then fails. After every fresh
 * read of the passkey list the security page calls
 * `PublicKeyCredential.signalAllAcceptedCredentials`, so a password manager
 * that supports it can hide or drop the ones the account no longer accepts.
 *
 * On every device but Apple's. One vendor's password manager has an open defect
 * (last checked 2026-09) by which this signal also deletes the passkey of
 * another account of the same person on the same site, silently: for a person
 * whose only second factor is a passkey, that is a lock-out. Until the defect is
 * verified fixed (OI-279), its devices are told nothing, and the page's own text
 * asks the person to delete a removed passkey from the device.
 *
 * A hint, never a step: nothing waits on it, and every failure is silent.
 */

/** What the call needs: the RP ID, the account's user handle and the accepted ids, base64url. */
export interface AcceptedPasskeys {
  rpId: string
  userHandle: string | null | undefined
  credentialIds: readonly string[]
}

interface NavigatorWithClientHints {
  userAgent: string
  userAgentData?: { platform?: string }
}

/**
 * Whether this device is Apple's (iPhone, iPad, Mac), in any browser: decided
 * here alone. The client-hints platform when the browser gives one, else the
 * user agent, where an iPad in desktop mode says `Macintosh`. A wrong "not
 * Apple" only exposes that device to the defect; a wrong "Apple" only skips the
 * clean-up.
 */
export function isApplePlatform(nav: NavigatorWithClientHints = navigator): boolean {
  const platform = nav.userAgentData?.platform
  if (typeof platform === "string" && platform.trim().length > 0) {
    return platform === "macOS" || platform === "iOS"
  }
  return /\b(iPhone|iPad|iPod|Macintosh)\b/.test(nav.userAgent)
}

type SignalAllAccepted = (options: {
  rpId: string
  userId: string
  allAcceptedCredentialIds: string[]
}) => Promise<void>

/**
 * Tells the browser the passkeys the account accepts, when all hold: the list
 * was just read from the server (the caller's promise), the account has a user
 * handle, the browser has the call, and the device is not Apple's. An empty
 * list says the account has no passkey left, on purpose.
 */
export function signalAcceptedPasskeys(
  accepted: AcceptedPasskeys,
  nav: NavigatorWithClientHints = navigator
): void {
  if (!accepted.userHandle || !accepted.rpId) return
  if (isApplePlatform(nav)) return

  const credential = (globalThis as { PublicKeyCredential?: unknown }).PublicKeyCredential as
    | { signalAllAcceptedCredentials?: SignalAllAccepted }
    | undefined
  const signal = credential?.signalAllAcceptedCredentials
  if (typeof signal !== "function") return

  try {
    void signal
      .call(credential, {
        rpId: accepted.rpId,
        userId: accepted.userHandle,
        allAcceptedCredentialIds: [...accepted.credentialIds],
      })
      .catch(() => {
        // A hint the browser declined: nothing to do.
      })
  } catch {
    // A browser that throws instead of rejecting: the same.
  }
}
