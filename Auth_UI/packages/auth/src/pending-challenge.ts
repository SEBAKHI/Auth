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
/**
 * The ways a sign-in's second step may be completed, as the server names them
 * (S24: `twoFactorMethods` on a challenged sign-in). Absent means a server that
 * predates the list: the screen then offers what it always did.
 */
export type TwoFactorMethod = "totp" | "recovery" | "email"

let pendingTwoFactorChallenge: string | null = null
let pendingReturnTo: string | null = null
let pendingStaySignedIn = false
let pendingMethods: readonly TwoFactorMethod[] | null = null

export function getPendingTwoFactorChallenge(): string | null {
  return pendingTwoFactorChallenge
}

export function getPendingReturnTo(): string | null {
  return pendingReturnTo
}

/**
 * Remembers a challenge, and the "Stay signed in" choice made on the sign-in
 * page that opened it (OI-154), so the second factor completes the sign-in the
 * person asked for. In memory only, like the challenge: a reload of the
 * second-factor page loses it, and false is the safe answer.
 */
export function setPendingTwoFactorChallenge(
  challengeToken: string,
  staySignedIn = false,
  methods: readonly TwoFactorMethod[] | null = null
): void {
  pendingTwoFactorChallenge = challengeToken
  pendingStaySignedIn = staySignedIn
  pendingMethods = methods
}

/**
 * The methods the server offered for `challengeToken` (S24), only when this
 * document opened that very challenge; null otherwise, which means the screen
 * every account saw before the emailed code existed.
 */
export function getPendingTwoFactorMethods(
  challengeToken: string
): readonly TwoFactorMethod[] | null {
  return pendingTwoFactorChallenge === challengeToken ? pendingMethods : null
}

/**
 * The "Stay signed in" choice for `challengeToken`: the one made on the sign-in
 * page only when this document opened that very challenge; false otherwise, as
 * after a reload or on a direct load of the second-factor page.
 */
export function getPendingStaySignedIn(challengeToken: string): boolean {
  return pendingTwoFactorChallenge === challengeToken && pendingStaySignedIn
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
  pendingStaySignedIn = false
  pendingMethods = null
}
