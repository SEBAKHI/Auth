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
