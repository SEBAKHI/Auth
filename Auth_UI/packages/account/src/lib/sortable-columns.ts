/**
 * Which column the organization members list may order by.
 *
 * The value travels to the API as `sortBy`, and the endpoint rejects anything
 * outside `SortFields.OrganizationMembers` with a 400. The console's
 * `server-sort-contract.test.ts` holds this to that C# list; it lives here
 * because the page is shared by both host apps.
 */
export const ORGANIZATION_MEMBER_SORT_COLUMNS = [
  "name",
  "roleName",
  "joinedAt",
] as const

/**
 * Which column an organization's invitation list may order by: a subset of
 * `SortFields.OrganizationInvitations`, held to it by the same contract test.
 */
export const ORGANIZATION_INVITATION_SORT_COLUMNS = [
  "email",
  "roleName",
  "status",
  "acceptedAt",
  "expiresAt",
] as const
