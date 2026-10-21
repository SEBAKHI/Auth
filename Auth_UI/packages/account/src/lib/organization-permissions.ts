import type { OrganizationGate } from "@authsystem/auth/organization-permissions"

/**
 * The organization page's controls, each with the gate of the endpoint it
 * calls.
 *
 * Every code is organization-scoped: it is read from the token's `org_perm`
 * claims for the organization on screen (`useOrganizationPermissions`), as the
 * API's gate reads it. `platformScope` is set only where the endpoint widens
 * for a platform administrator (`PlatformScope` in the controller); the
 * mutations have none, so the platform code alone opens them. Every code
 * mirrors `Auth/Auth.Domain/Constants/PermissionCodes.cs`, and
 * `invitations-access.test.tsx` holds each gate to its endpoints' attribute and
 * scope.
 */
export const ORGANIZATION_GATES = {
  /** The invitations list, Invite and Resend. */
  inviteMembers: {
    code: "org:members:invite",
    platformScope: "organizations:read",
  },
  /** Change role and Remove member. */
  manageMembers: { code: "org:members:manage" },
  /** Manage application roles: the member's assignments, read. */
  readMemberAppRoles: { code: "org:permissions:read" },
  /** Inside that dialog: the assignable roles, add and remove. */
  manageMemberAppRoles: { code: "org:permissions:manage" },
  /** The header's Edit and the logo's change and remove. */
  updateOrganization: { code: "org:update" },
  /** Enable application, Edit subscription and Remove. */
  manageApplications: { code: "org:apps:manage" },
} as const satisfies Record<string, OrganizationGate>
