import { existsSync, readFileSync } from "node:fs"
import path from "node:path"
import { describe, expect, it } from "vitest"

import { ORGANIZATION_MEMBER_SORT_COLUMNS } from "@authsystem/account/lib/sortable-columns"

import { SORTABLE_COLUMNS } from "@/lib/sortable-columns"

/**
 * Every list that sorts on the server must offer only the fields that server
 * accepts.
 *
 * These lists are hand-written on both sides of the wire, and they drifted:
 * `/users` offered a "Roles" header, `/notifications/templates` a "Status" one,
 * and neither id is in the endpoint's allow-list - so the click returned 400 and
 * the table rendered its error state. Once list state moved into the URL the bad
 * value also survived a reload and travelled in a shared link.
 *
 * The server list is read from the C# constants rather than copied here, so this
 * fails when either side changes alone.
 */
/** Walk up to the repository root, so the path holds wherever vitest is started. */
function repositoryRoot(): string {
  let directory = process.cwd()
  for (let depth = 0; depth < 10; depth += 1) {
    if (existsSync(path.join(directory, "Auth", "Auth.sln"))) return directory
    const parent = path.dirname(directory)
    if (parent === directory) break
    directory = parent
  }
  throw new Error("Auth/Auth.sln not found above " + process.cwd())
}

function serverAllowLists(): Map<string, Set<string>> {
  const source = readFileSync(
    path.join(repositoryRoot(), "Auth/Auth.Domain/Constants/SortFields.cs"),
    "utf8"
  )
  const groups = new Map<string, Set<string>>()
  const classes = source.matchAll(
    /public static class (\w+)\s*\{([\s\S]*?)\n {4}\}/g
  )
  for (const [, name, body] of classes) {
    const constants = new Map<string, string>()
    for (const [, key, value] of body.matchAll(
      /public const string (\w+)\s*=\s*"([^"]+)"/g
    )) {
      constants.set(key, value)
    }
    const allowed = body.match(/Allowed\s*=\s*\[([\s\S]*?)\]/)
    if (!allowed) continue
    groups.set(
      name,
      new Set(
        allowed[1]
          .split(",")
          .map((token) => token.trim())
          .filter(Boolean)
          .map((token) => constants.get(token) ?? token)
      )
    )
  }
  // The outer `SortFields` class matches first and swallows `Users`.
  const outer = groups.get("SortFields")
  if (outer) {
    groups.set("Users", outer)
    groups.delete("SortFields")
  }
  return groups
}

const LISTS = [
  ["users", SORTABLE_COLUMNS.users, "Users"],
  ["applications", SORTABLE_COLUMNS.applications, "Applications"],
  ["organizations", SORTABLE_COLUMNS.organizations, "Organizations"],
  ["audit logs", SORTABLE_COLUMNS.auditLogs, "AuditLogs"],
  ["a user's audit log", SORTABLE_COLUMNS.userAuditLog, "AuditLogs"],
  [
    "notification templates",
    SORTABLE_COLUMNS.notificationTemplates,
    "NotificationTemplates",
  ],
  [
    "the delivery log",
    SORTABLE_COLUMNS.notificationOutbox,
    "NotificationOutbox",
  ],
  [
    "an application's users",
    SORTABLE_COLUMNS.applicationUsers,
    "ApplicationUsers",
  ],
  [
    "an application's organizations",
    SORTABLE_COLUMNS.applicationOrganizations,
    "ApplicationOrganizations",
  ],
  ["a permission's users", SORTABLE_COLUMNS.permissionUsers, "PermissionUsers"],
  ["a role's users", SORTABLE_COLUMNS.roleUsers, "RoleUsers"],
  [
    "an organization's members",
    ORGANIZATION_MEMBER_SORT_COLUMNS,
    "OrganizationMembers",
  ],
] as const

describe("server-sorted lists offer only fields the API accepts", () => {
  const groups = serverAllowLists()

  it("finds the server allow-lists", () => {
    // If the parse silently returns nothing, every case below passes vacuously.
    expect(groups.size).toBeGreaterThan(20)
    expect(groups.get("Users")).toContain("email")
  })

  it.each(LISTS)("%s", (_name, columns, group) => {
    const allowed = groups.get(group)
    expect(allowed, `SortFields.${group} not found`).toBeDefined()

    const rejected = columns.filter((column) => !allowed?.has(column))
    expect(rejected).toEqual([])
  })
})
