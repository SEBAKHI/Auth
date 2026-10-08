import { Navigate, Outlet, useLocation, useMatches } from "react-router-dom"

import { useAuth } from "./auth-context"

/** The path of the page that enrols, steps up or signs in again. */
export const TWO_FACTOR_REQUIRED_PATH = "/two-factor/required"

/**
 * Route metadata: a page a platform administrator may open while their session
 * has not yet proved a second factor. Only the profile carries it — enrolment and
 * the security tab live there.
 */
export interface MfaExemptHandle {
  mfaExempt?: boolean
}

function isExempt(handle: unknown): boolean {
  return (
    typeof handle === "object" &&
    handle !== null &&
    (handle as MfaExemptHandle).mfaExempt === true
  )
}

/**
 * Route guard for the signed-in shell: while the session's platform authority is
 * withheld (S08: `TwoFactor:EnforceForPlatformAdmins`, and this session has not
 * proved a second factor), every page but an exempt one sends the user to
 * {@link TWO_FACTOR_REQUIRED_PATH}, carrying where they were going.
 *
 * Cosmetic, like every console guard: the server already refuses the requests
 * (403 TwoFactor.RequiredByPolicy). It exists so the user meets a page that says
 * what to do instead of a screen of refusals, and it holds on reload, on a typed
 * address and on a deep link — which the sign-in completion alone would not.
 */
export function RequireMfaSatisfied() {
  const { mfaRequirement } = useAuth()
  const location = useLocation()
  const matches = useMatches()

  if (mfaRequirement !== "none" && !matches.some((match) => isExempt(match.handle))) {
    return (
      <Navigate to={TWO_FACTOR_REQUIRED_PATH} replace state={{ from: location }} />
    )
  }

  return <Outlet />
}
