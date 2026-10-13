import { toAccountsSearch } from "@authsystem/auth/return-to"

/**
 * The same pending request without the organization step. The shape belongs
 * to `return-to.ts`, which knows both: a resume URL gains
 * `skip_organization=true`, a legacy authorize URL loses `create_organization`.
 */
export { withoutOrganizationRequest } from "@authsystem/auth/return-to"

/** Sign in again for the same pending request, when the SSO session is gone. */
export function signInPath(returnTo: string): string {
  return `/login${toAccountsSearch(returnTo)}`
}
