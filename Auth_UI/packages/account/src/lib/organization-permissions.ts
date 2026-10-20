/**
 * The permission codes the shared organization page gates on.
 *
 * `inviteMembers` is organization-scoped: it is read from the token's
 * `org_perm` claims for the organization on screen (`useOrganizationPermission`),
 * as the API's gate reads it. `platformScope` is the platform permission that
 * widens an organization endpoint for a platform administrator
 * (`PlatformScope` in the controllers). Both mirror
 * `Auth/Auth.Domain/Constants/PermissionCodes.cs`, and
 * `invitations-access.test.tsx` holds them to it.
 */
export const ORGANIZATION_GATES = {
  inviteMembers: "org:members:invite",
  platformScope: "organizations:read",
} as const
