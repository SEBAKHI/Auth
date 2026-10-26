import type { Schemas } from "@authsystem/api/types"

/**
 * Whether the profile offers to remove the password (D-215-3), from what the
 * page knows: the account has one, email sending is on, and the address is
 * confirmed. The server checks all of it again, and what the page cannot know —
 * whether this session's sign-in is recent and proved the password — it answers
 * with a fresh sign-in.
 */
export function removePasswordOffer(
  me: Schemas["UserDto"],
  codeSignInOffered: boolean
): "offered" | "blockedByEmailCode" | "none" {
  if (me.hasPassword !== true || !codeSignInOffered || me.emailConfirmed !== true) {
    return "none"
  }
  // The emailed code would follow a sign-in by emailed code: refused until the
  // emailed second step is switched off or the authenticator app is bound.
  return me.emailCodeEnabled === true ? "blockedByEmailCode" : "offered"
}
