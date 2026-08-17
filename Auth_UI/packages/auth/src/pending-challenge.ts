/**
 * In-memory fallbacks for a sign-in that was interrupted by a 2FA challenge,
 * so a lost navigation state (e.g. a re-render race) doesn't strand the verify
 * page. Never persisted — a page refresh intentionally sends the user back to
 * /login.
 *
 * The challenge token and the pending authorize request live together because
 * they are worthless apart: a surviving challenge that forgot where to resume
 * lets the user verify successfully while the relying party's request dies
 * anyway. They are set together and cleared together, and nothing else may
 * write to either.
 *
 * Kept out of the auth context on purpose. This is the one piece the completion
 * rule needs, and pulling the context in for it would drag the API client, the
 * query cache and the whole session lifecycle behind it.
 */
let pendingTwoFactorChallenge: string | null = null
let pendingReturnTo: string | null = null

export function getPendingTwoFactorChallenge(): string | null {
  return pendingTwoFactorChallenge
}

export function getPendingReturnTo(): string | null {
  return pendingReturnTo
}

export function setPendingTwoFactorChallenge(challengeToken: string): void {
  pendingTwoFactorChallenge = challengeToken
}

/** Remembers where to resume once the challenge is satisfied. */
export function setPendingReturnTo(returnTo: string | null): void {
  pendingReturnTo = returnTo
}

/**
 * Drops the pending challenge when it is settled, or when the user abandons the
 * 2FA step to sign in as someone else. Without this the verify page would still
 * find a challenge for the previous account and send them straight back to it,
 * and a settled request could resume a later, unrelated sign-in.
 */
export function clearPendingTwoFactorChallenge(): void {
  pendingTwoFactorChallenge = null
  pendingReturnTo = null
}
