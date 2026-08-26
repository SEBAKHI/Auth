/**
 * Which column each server-sorted list may order by.
 *
 * These are not display choices: the value travels to the API as `sortBy`, and
 * the endpoint rejects anything outside its own allow-list with a 400 that the
 * table renders as an error state. They live in one file, apart from the pages,
 * because they are one half of a contract whose other half is C# - and because
 * they drifted while spread across eleven page files.
 *
 * `server-sort-contract.test.ts` reads `Auth/Auth.Domain/Constants/SortFields.cs`
 * and fails when either side changes alone. A column the API cannot order by
 * must also carry `enableSorting: false`, or its header offers a click that
 * breaks the list.
 */
export const SORTABLE_COLUMNS = {
  users: [
    "name",
    "displayName",
    "email",
    "status",
    "lastLoginAt",
    "createdAt",
  ],
  applications: ["name", "status", "contactEmail", "createdAt"],
  organizations: ["name", "memberCount", "isActive", "createdAt"],
  // One list for one endpoint. There were two — a shorter one for the copy of
  // this table on a user's page — and they had already drifted: `actionType`
  // and the two person columns were orderable on one screen and not on the
  // other, for rows the same query returns. Both screens now render the same
  // columns, so both order by the same fields.
  auditLogs: [
    "action",
    "actionType",
    "entityType",
    "actor",
    "subject",
    "applicationName",
    "timestamp",
  ],
  notificationTemplates: [
    "typeName",
    "applicationName",
    "channel",
    "defaultLanguage",
    "modifiedAt",
  ],
  notificationOutbox: [
    "typeCode",
    "recipient",
    "languageCode",
    "status",
    "sentAt",
    "createdAt",
  ],
  applicationUsers: ["email", "firstName", "status", "lastLoginAt"],
  applicationOrganizations: ["name", "memberCount", "enabledAt", "isActive"],
  permissionUsers: ["email", "firstName", "status"],
  roleUsers: ["email", "firstName", "status", "lastLoginAt"],
} as const
