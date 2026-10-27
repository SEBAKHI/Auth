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
):
  | "offered"
  | "blockedByEmailCode"
  | "blockedByPlatformAuthority"
  | "none" {
  if (me.hasPassword !== true || !codeSignInOffered || me.emailConfirmed !== true) {
    return "none"
  }
  // The emailed code would follow a sign-in by emailed code: refused until the
  // emailed second step is switched off or the authenticator app is bound.
  if (me.emailCodeEnabled === true) return "blockedByEmailCode"
  // Platform authority keeps a second proof the mailbox cannot give (D-110-1):
  // refused until the authenticator app is bound. `permissions` is the
  // account's platform authority, the server's own reading of it.
  if ((me.permissions?.length ?? 0) > 0 && me.totpEnabled !== true) {
    return "blockedByPlatformAuthority"
  }
  return "offered"
}
