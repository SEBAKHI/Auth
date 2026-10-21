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
 * One organization endpoint's gate, as the server applies it: the code its
 * `[RequirePermission]` names, and, for the reads that widen for a platform
 * administrator, the platform permission they widen on (`PlatformScope`).
 */
export interface OrganizationGate {
  code: string
  /**
   * Set only for an endpoint whose handler refuses a platform caller who is not
   * a member unless this platform permission is held too. A mutation has none:
   * the permission gate alone decides it, so the platform code is enough there,
   * and asking for a scope would hide a control the API lets through.
   */
  platformScope?: string
}

/**
 * Whether the signed-in person may use each organization endpoint in `gates`,
 * as the server decides it: the code inside that organization, or the code on
 * the platform claim (with the endpoint's `platformScope`, where it has one).
 *
 * The page reads its permissions here once: the token is decoded once per
 * render, and a token with no claim at all for the organization is renewed at
 * most once per mount, whatever the number of gates. The server falls back to
 * the live membership in that case; without the renewal, the owner of an
 * organization created a minute ago would not see its controls until the
 * access token expired. Nothing is renewed when the platform claim already
 * grants every gate. Pass a module constant: the gates are the page's, not the
 * render's.
 */
export function useOrganizationPermissions<Name extends string>(
  organizationId: string | undefined,
  gates: Readonly<Record<Name, OrganizationGate>>
): Record<Name, boolean> {
  const { status, hasPermission } = useAuth()
  const [renewed, setRenewed] = React.useState<string>()

  const scoped = organizationId
    ? tokenOrganizationPermissions(organizationId)
    : undefined
  const names = Object.keys(gates) as Name[]
  const platform = (gate: OrganizationGate) =>
    hasPermission(gate.code) &&
    (gate.platformScope === undefined || hasPermission(gate.platformScope))
  const mustRenew =
    status === "authenticated" &&
    Boolean(organizationId) &&
    names.some((name) => !platform(gates[name])) &&
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

  const granted = {} as Record<Name, boolean>
  for (const name of names) {
    const gate = gates[name]
    granted[name] =
      platform(gate) || permissionMatches(scoped ?? [], gate.code)
  }
  return granted
}
