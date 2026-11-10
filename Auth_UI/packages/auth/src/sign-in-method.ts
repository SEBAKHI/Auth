import type { SignInMethod, SignInOptions } from "@authsystem/ui/branding"

/**
 * What the sign-in page remembers between visits, and what it carries across a
 * "sign in again" (OI-215).
 *
 * THE LAST METHOD (D-215-4). One word in `localStorage`, "code" or "password",
 * written only once a sign-in ended in a session: "code" when the emailed code
 * was the first proof, "password" when a password was typed. It decides which
 * method the page puts first, and nothing else: the other method stays a visible
 * link, and a word the current options do not offer is ignored. Never the
 * address, nor anything about the account: the next person at this browser
 * learns only how the last sign-in here was made.
 *
 * THE FULL-PROOF REASON. The "sign in again" dialog signs out before a security
 * change; the route guard then brings the person to the sign-in page. A sign-in
 * by code from there asks the server for full proof, so an account with a
 * password proves it too — otherwise the fresh session would prove only the
 * mailbox and the change would ask again, forever. The reason lives in
 * `sessionStorage` (this tab only) so that it survives the sign-out and the
 * redirect, and it can only make a sign-in harder: a forged one costs its
 * forger a password prompt, nothing else.
 *
 * THE PASSKEY REASON (S20, T-C7). The same slot, with another word: the account's
 * qualifying second factor is a passkey, and the change asked for a session that
 * proved two factors. Neither the password nor the emailed code can give one,
 * only the passkey, so the page offers the passkey first. A code sign-in from
 * there still proves the password too. A forged one costs its forger a
 * reordered page.
 *
 * A PASSKEY SIGN-IN writes no method and leaves no proof (T-C6): it answers the
 * reason in the slot, and the next visit opens as the last method decided.
 *
 * Every read and write is wrapped: a browser that blocks storage falls back to
 * the deployment's setting and to an ordinary sign-in.
 */

export const LAST_SIGN_IN_METHOD_KEY = "auth.signIn.lastMethod"
export const FULL_PROOF_KEY = "auth.signIn.fullProof"

export function readLastSignInMethod(): SignInMethod | null {
  try {
    const value = window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)
    return value === "code" || value === "password" ? value : null
  } catch {
    return null
  }
}

function writeLastSignInMethod(method: SignInMethod): void {
  try {
    window.localStorage.setItem(LAST_SIGN_IN_METHOD_KEY, method)
  } catch {
    // Not remembered: the next visit opens with the deployment's setting.
  }
}

/**
 * The method the page puts first: the one the server named for the account
 * when an application's two-step requirement sent the person back (S23,
 * D-S23-3 (c)), else the remembered one, else the deployment's — each only
 * while the options offer code sign-in at all. The person's own click, which
 * beats all three, is the caller's.
 */
export function primarySignInMethod(
  options: SignInOptions,
  remembered: SignInMethod | null,
  serverNamed: SignInMethod | null = null
): SignInMethod {
  if (!options.codeSignInOffered) return "password"
  return serverNamed ?? remembered ?? options.primaryMethod
}

const FULL_PROOF = "1"
const PASSKEY_FIRST = "passkey"

export function requestFullProof(): void {
  try {
    window.sessionStorage.setItem(FULL_PROOF_KEY, FULL_PROOF)
  } catch {
    // Not carried: the code sign-in then proves the mailbox only, and the
    // change asks for a fresh sign-in again, which the password answers.
  }
}

/** The "sign in again" dialog of an account whose qualifying factor is a passkey. */
export function requestPasskeySignIn(): void {
  try {
    window.sessionStorage.setItem(FULL_PROOF_KEY, PASSKEY_FIRST)
  } catch {
    // Not carried: the page opens as usual, with the passkey button in its place.
  }
}

function readReason(): string | null {
  try {
    return window.sessionStorage.getItem(FULL_PROOF_KEY)
  } catch {
    return null
  }
}

export function isFullProofRequested(): boolean {
  const reason = readReason()
  return reason === FULL_PROOF || reason === PASSKEY_FIRST
}

/** Whether the page should offer the passkey first (T-C7). */
export function isPasskeySignInRequested(): boolean {
  return readReason() === PASSKEY_FIRST
}

function clearFullProofRequest(): void {
  try {
    window.sessionStorage.removeItem(FULL_PROOF_KEY)
  } catch {
    // Nothing to clear where nothing could be written.
  }
}

/**
 * A sign-in from the sign-in page ended in a session: remember how, and drop
 * the reason that asked for it.
 */
export function signInEnded(method: SignInMethod): void {
  writeLastSignInMethod(method)
  clearFullProofRequest()
}

/**
 * A passkey sign-in ended in a session: the reason that asked for it is
 * answered, and no method is remembered (T-C6), so the next visit opens with
 * the method the last password or code sign-in left.
 */
export function passkeySignInEnded(): void {
  clearFullProofRequest()
}

/**
 * A sign-in that ends at the second step ends on another screen. The method
 * rides with its challenge, in memory only, and is written when that challenge
 * ends in a session (`completeTwoFactor`). One slot: a newer challenge replaces
 * an abandoned one.
 */
let pendingChallengeMethod: { challengeToken: string; method: SignInMethod } | null =
  null

export function rememberMethodForChallenge(
  challengeToken: string,
  method: SignInMethod
): void {
  pendingChallengeMethod = { challengeToken, method }
}

/**
 * The method the sign-in page recorded for this challenge, without spending it;
 * null when the challenge did not start there (OI-275: the proof the
 * completion keeps with its marker).
 */
export function methodForChallenge(challengeToken: string): SignInMethod | null {
  return pendingChallengeMethod?.challengeToken === challengeToken
    ? pendingChallengeMethod.method
    : null
}

export function challengeEnded(challengeToken: string): void {
  if (pendingChallengeMethod?.challengeToken !== challengeToken) return
  const { method } = pendingChallengeMethod
  pendingChallengeMethod = null
  signInEnded(method)
}
