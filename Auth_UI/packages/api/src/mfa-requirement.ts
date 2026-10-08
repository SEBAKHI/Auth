import { decodeJwt } from "@authsystem/api/jwt"
import { getAccessToken } from "@authsystem/api/token-store"

/**
 * What a platform administrator's session still has to prove before its token
 * carries platform authority (S08, `TwoFactor:EnforceForPlatformAdmins`):
 *
 * - `none` — nothing; every account that is not a platform administrator, and
 *   every administrator whose session proved two factors;
 * - `enroll` — the account has no second factor yet;
 * - `step_up` — a code from the authenticator app (or a recovery code) upgrades
 *   this session;
 * - `reauthenticate` — the session's first factor is unknown: sign in again.
 *
 * The server enforces it — the token simply lacks the permissions — so this only
 * decides which page the console shows. It arrives as the token's `mfa_req` claim
 * and as `mfaRequirement` in the user info a sign-in and `/me` return.
 */
export type MfaRequirement = "none" | "enroll" | "step_up" | "reauthenticate"

const KNOWN = new Set<MfaRequirement>(["none", "enroll", "step_up", "reauthenticate"])

/**
 * The one reader of the requirement, from either source.
 *
 * Absent means `none`: an API built before the field, and a token minted without
 * the claim (which is every token unless authority is withheld). Anything else it
 * does not recognise means `reauthenticate` — fail closed: the server withheld
 * something this client cannot name, and signing in again is the path that
 * always works.
 */
export function readMfaRequirement(value: unknown): MfaRequirement {
  if (value === undefined || value === null || value === "") return "none"
  return typeof value === "string" && KNOWN.has(value as MfaRequirement)
    ? (value as MfaRequirement)
    : "reauthenticate"
}

/**
 * The requirement the access token this tab holds right now carries. Read at the
 * moment a sign-in completes, when the token has just been stored and the auth
 * context has not re-rendered yet.
 */
export function currentMfaRequirement(): MfaRequirement {
  const token = getAccessToken()
  return readMfaRequirement(token ? decodeJwt(token)?.mfa_req : undefined)
}
