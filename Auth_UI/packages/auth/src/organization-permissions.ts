import * as React from "react"

import { refreshSessionNow } from "@authsystem/api/client"
import { claimToArray, decodeJwt } from "@authsystem/api/jwt"
import { getAccessToken } from "@authsystem/api/token-store"

import { useAuth } from "./auth-context"
import { permissionMatches } from "./permission-matching"

/**
 * Organization-scoped permissions, read from the same access token as the
 * platform ones.
 *
 * The platform `permissions` claim says what a person may do anywhere; the
 * `org_perm` claims say what they may do inside one organization, one claim per
 * organization and code: `{organizationId}:{code}` (`JwtClaimNames.OrgPermissions`).
 * The API's permission gate reads both, platform first, for every `org:` code
 * on a route that names an organization (`PermissionRequirementHandler`), and
 * this is its client twin. The server stays the authority: a token minted
 * before a change of role answers late here, and the server answers 403.
 */

/**
 * The codes the current access token grants inside one organization, or
 * undefined when it carries none for it. Undefined is not "nothing granted":
 * a token minted before the membership existed (the organization was created,
 * or an invitation accepted, in this session) has no claim for it yet, and the
 * server then checks the live membership instead.
 */
export function tokenOrganizationPermissions(
  organizationId: string
): string[] | undefined {
  const token = getAccessToken()
  const claims = token ? decodeJwt(token) : null
  const prefix = `${organizationId.toLowerCase()}:`
  const codes = claimToArray(
    claims?.org_perm as string | string[] | undefined
  )
    .filter((value) => value.toLowerCase().startsWith(prefix))
    .map((value) => value.slice(prefix.length))
  return codes.length > 0 ? codes : undefined
}

/** Whether the current token grants `permission` inside the organization. */
export function hasOrganizationPermission(
  organizationId: string,
  permission: string
): boolean {
  return permissionMatches(
    tokenOrganizationPermissions(organizationId) ?? [],
    permission
  )
}

/**
 * Whether the signed-in person may use an organization endpoint gated on
 * `permission`, as the server decides it: the code inside that organization,
 * or, for a platform administrator, the code on the platform claim together
 * with `platformScope`, the platform permission the endpoint widens on (a
 * member's request and an operator's differ by that one claim).
 *
 * A token with no claim at all for the organization is renewed once, as the
 * server falls back to the live membership in that case; without it, the owner
 * of an organization created a minute ago would not see its controls until the
 * access token expired.
 */
export function useOrganizationPermission(
  organizationId: string | undefined,
  permission: string,
  platformScope: string
): boolean {
  const { status, hasPermission } = useAuth()
  const [renewed, setRenewed] = React.useState<string>()

  const platform = hasPermission(permission) && hasPermission(platformScope)
  const scoped = organizationId
    ? tokenOrganizationPermissions(organizationId)
    : undefined
  const mustRenew =
    status === "authenticated" &&
    Boolean(organizationId) &&
    !platform &&
    scoped === undefined &&
    renewed !== organizationId

  React.useEffect(() => {
    if (!mustRenew || !organizationId) return
    let cancelled = false
    void refreshSessionNow().finally(() => {
      if (!cancelled) setRenewed(organizationId)
    })
    return () => {
      cancelled = true
    }
  }, [mustRenew, organizationId])

  return platform || permissionMatches(scoped ?? [], permission)
}
