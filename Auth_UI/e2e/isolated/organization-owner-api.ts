import type { Route } from "@playwright/test"

import { fulfillJson } from "./mock-authenticated-api"
import { fulfillProblem, problem } from "./problem"

/**
 * The API as an organization's owner sees it: no platform permission at all, so
 * the platform-wide role lists answer 403 exactly as the API does, and the
 * organization's own lists answer. Every read of a platform list and every
 * invitation sent is recorded, so a test asserts what the page ASKED and SENT.
 * Shared by the console and accounts projects: the organization page is one page.
 */

export const ORG = "11111111-1111-1111-1111-111111111111"
export const ADMIN_ROLE = "22222222-2222-2222-2222-222222222221"
export const MEMBER_ROLE = "22222222-2222-2222-2222-222222222222"
export const APP = "33333333-3333-3333-3333-333333333333"
export const APP_CODE = "CRM"
export const REP_ROLE = "44444444-4444-4444-4444-444444444444"
export const MEMBER_USER = "55555555-5555-5555-5555-555555555555"

/** The owner's organization-scoped claims, as the API mints them (`org_perm`). */
export const OWNER_ORG_PERMISSIONS = [`${ORG}:org:*`]

export interface OwnerApiRecord {
  platformListReads: string[]
  invitations: unknown[]
}

export function ownerApiRecord(): OwnerApiRecord {
  return { platformListReads: [], invitations: [] }
}

/** Answers the organization page's requests; false for anything else. */
export async function answerOrganizationOwner(
  route: Route,
  url: URL,
  recorded: OwnerApiRecord,
  options: { membershipRolesFail?: boolean } = {}
): Promise<boolean> {
  // The client sends /api/v1/Organizations/…: compare case-insensitively.
  const path = url.pathname.toLowerCase()
  const method = route.request().method()
  const org = `/api/v1/organizations/${ORG}`

  if (
    path === "/api/v1/roles" ||
    /^\/api\/v1\/applications\/[^/]+\/roles$/.test(path)
  ) {
    recorded.platformListReads.push(path)
    await fulfillProblem(route, problem(403, "Auth.PermissionDenied"))
    return true
  }
  if (path === org && method === "GET") {
    await fulfillJson(route, {
      id: ORG,
      code: "acme",
      name: "Acme Corporation",
      contactEmail: "owner@example.test",
      ownerId: "99999999-9999-9999-9999-999999999999",
      isActive: true,
      memberCount: 2,
      enabledAppCount: 1,
    })
    return true
  }
  if (path === `${org}/membership-roles`) {
    if (options.membershipRolesFail) {
      await fulfillProblem(route, problem(403, "Auth.PermissionDenied"))
      return true
    }
    await fulfillJson(route, [
      { id: ADMIN_ROLE, code: "org-admin", name: "Organization Admin" },
      { id: MEMBER_ROLE, code: "org-member", name: "Organization Member" },
    ])
    return true
  }
  if (path === `${org}/applications`) {
    await fulfillJson(route, [
      {
        applicationId: APP,
        applicationCode: APP_CODE,
        applicationName: "CRM",
        isActive: true,
        assignedUserCount: 0,
      },
    ])
    return true
  }
  if (path === `${org}/applications/${APP}/assignable-roles`) {
    await fulfillJson(route, [
      { id: REP_ROLE, code: "sales_rep", name: "Sales representative" },
    ])
    return true
  }
  if (path === `${org}/invitations` && method === "GET") {
    // Paged since OI-193 (D-193-1): an object, never a bare array.
    await fulfillJson(route, {
      invitations: [],
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 0,
    })
    return true
  }
  if (path === `${org}/invitations` && method === "POST") {
    recorded.invitations.push(route.request().postDataJSON())
    await fulfillJson(
      route,
      { id: "66666666-6666-6666-6666-666666666666" },
      201
    )
    return true
  }
  if (path === `${org}/members`) {
    await fulfillJson(route, {
      members: [
        {
          id: "77777777-7777-7777-7777-777777777777",
          userId: MEMBER_USER,
          email: "colleague@example.test",
          fullName: "A Colleague",
          roleId: MEMBER_ROLE,
          roleName: "Organization Member",
          isActive: true,
        },
      ],
      totalCount: 1,
      totalPages: 1,
    })
    return true
  }
  if (path === `${org}/members/${MEMBER_USER}/roles`) {
    await fulfillJson(route, [])
    return true
  }
  return false
}
