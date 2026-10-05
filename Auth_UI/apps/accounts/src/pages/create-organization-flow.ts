/** The authorize parameter that asks for the organization-creation page. */
export const CREATE_ORGANIZATION_PARAM = "create_organization"

/**
 * The same authorize request without `create_organization`: still an authorize
 * URL on the API origin, so the shared returnTo rule accepts it, and the
 * authorize endpoint then issues the code without asking for an organization.
 * Everything else the relying party sent is kept.
 */
export function withoutOrganizationRequest(returnTo: string): string {
  const url = new URL(returnTo)
  url.searchParams.delete(CREATE_ORGANIZATION_PARAM)
  return url.toString()
}

/** Sign in again for the same pending request, when the SSO session is gone. */
export function signInPath(returnTo: string): string {
  return `/login?returnTo=${encodeURIComponent(returnTo)}`
}
