import { expect, test } from "@playwright/test"

import {
  answerOrganizationOwner,
  ORG,
  OWNER_ORG_PERMISSIONS,
  ownerApiRecord,
} from "../organization-owner-api"
import { fulfillJson } from "../mock-authenticated-api"
import { installAnonymousApi, loginResponse } from "./mock-anonymous-api"

/**
 * OI-193 on the built accounts app: an organization's invitations tab asks the
 * server for one page of the statuses its filter names, shows each status in
 * the page's language, shows who accepted, and offers Resend only where the
 * server says it works. The mock answers by the `status` values the tab sends,
 * the way the API filters, so switching the filter is proven by what was ASKED
 * as much as by what is shown.
 */

const OWNER = {
  id: "99999999-9999-9999-9999-999999999999",
  email: "owner@example.test",
  firstName: "Org",
  lastName: "Owner",
  // Arabic once signed in: a raw enum word on a badge would then stand out.
  preferredLanguage: "ar",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

// The C# InvitationStatus values the generated client sends.
const STATUS = {
  Pending: 0,
  Accepted: 1,
  Declined: 2,
  Expired: 3,
  Cancelled: 4,
}

const invitation = (email: string, overrides: Record<string, unknown>) => ({
  id: `00000000-0000-0000-0000-${email.length.toString().padStart(12, "0")}`,
  organizationId: ORG,
  email,
  roleName: "Organization Member",
  status: "Pending",
  expiresAt: "2026-12-01T00:00:00Z",
  isExpired: false,
  canResend: true,
  createdAt: "2026-10-01T00:00:00Z",
  ...overrides,
})

const ROWS = [
  invitation("waiting@example.test", {}),
  invitation("gone-for-good@example.test", {
    status: "Expired",
    isExpired: true,
    canResend: false,
  }),
  invitation("joined@example.test", {
    status: "Accepted",
    canResend: false,
    acceptedAt: "2026-10-10T09:00:00Z",
    acceptedByUserName: "Ada Accepter",
  }),
]

test("the tab filters on the server, shows the accepter, and offers Resend only where it works", async ({
  page,
}) => {
  const asked: string[][] = []
  await installAnonymousApi(page, async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/login") {
      await fulfillJson(route, loginResponse(OWNER, OWNER_ORG_PERMISSIONS))
      return true
    }
    if (path === "/api/v1/auth/me") {
      const bearer = await route.request().headerValue("authorization")
      await (bearer
        ? fulfillJson(route, OWNER)
        : fulfillJson(route, { status: 401, code: "Http.Unauthorized" }, 401))
      return true
    }
    if (
      path === `/api/v1/organizations/${ORG}/invitations` &&
      route.request().method() === "GET"
    ) {
      const statuses = url.searchParams.getAll("status")
      asked.push(statuses)
      const wanted = new Set(statuses.map(Number))
      const rows = ROWS.filter((row) =>
        wanted.has(STATUS[row.status as keyof typeof STATUS])
      )
      await fulfillJson(route, {
        invitations: rows,
        totalCount: rows.length,
        pageNumber: Number(url.searchParams.get("pageNumber")),
        pageSize: Number(url.searchParams.get("pageSize")),
        totalPages: 1,
      })
      return true
    }
    return answerOrganizationOwner(route, url, ownerApiRecord())
  })

  await page.goto(`/organizations/${ORG}?tab=invitations`)
  await page.getByLabel("Email").fill(OWNER.email)
  await page.getByLabel("Password").fill("Passw0rd!")
  await page.getByRole("button", { name: "Sign in", exact: true }).click()

  // The default view: pending and expired, asked of the server.
  const table = page.getByRole("table")
  await expect(table.getByText("waiting@example.test")).toBeVisible()
  expect(asked.at(-1)).toEqual([String(STATUS.Pending), String(STATUS.Expired)])

  const waiting = table
    .getByRole("row")
    .filter({ hasText: "waiting@example.test" })
  const gone = table
    .getByRole("row")
    .filter({ hasText: "gone-for-good@example.test" })
  await expect(
    waiting.getByRole("button", { name: "إعادة الإرسال" })
  ).toBeVisible()
  await expect(gone.getByText("منتهية", { exact: true })).toBeVisible()
  await expect(gone.getByRole("button", { name: "إعادة الإرسال" })).toHaveCount(
    0
  )
  await expect(table.getByText("joined@example.test")).toHaveCount(0)

  // Switch the filter to Accepted only.
  // The faceted filter's trigger, not the column header of the same name.
  await page
    .locator('[data-slot="popover-trigger"]', { hasText: "الحالة" })
    .click()
  await page.getByRole("option", { name: "معلّقة" }).click()
  await page.getByRole("option", { name: "منتهية" }).click()
  await page.getByRole("option", { name: "مقبولة" }).click()
  await page.keyboard.press("Escape")
  // Clearing both on the way passes through "every status"; wait for the last ask.
  await expect(page).toHaveURL(/invitations\.status=Accepted(&|$)/)
  await expect.poll(() => asked.at(-1)).toEqual([String(STATUS.Accepted)])

  const joined = table
    .getByRole("row")
    .filter({ hasText: "joined@example.test" })
  await expect(joined.getByText("مقبولة", { exact: true })).toBeVisible()
  await expect(joined).toContainText("قبِلها Ada Accepter في")
  await expect(
    joined.getByRole("button", { name: "إعادة الإرسال" })
  ).toHaveCount(0)
  await expect(table.getByText("waiting@example.test")).toHaveCount(0)

  // No badge on the page carries the raw enum word.
  for (const raw of ["Pending", "Expired", "Accepted"]) {
    await expect(
      page.locator('[data-slot="badge"]', { hasText: raw })
    ).toHaveCount(0)
  }
  expect(asked.length).toBeGreaterThan(1)
})

test("a member who may only read members sees no Invitations tab, even through a link", async ({
  page,
}) => {
  const member = { ...OWNER, preferredLanguage: "en" }
  const listReads: string[] = []
  await installAnonymousApi(page, async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/login") {
      await fulfillJson(
        route,
        loginResponse(member, [
          `${ORG}:org:members:read`,
          `${ORG}:org:apps:read`,
        ])
      )
      return true
    }
    if (path === "/api/v1/auth/me") {
      const bearer = await route.request().headerValue("authorization")
      await (bearer
        ? fulfillJson(route, member)
        : fulfillJson(route, { status: 401, code: "Http.Unauthorized" }, 401))
      return true
    }
    if (path === `/api/v1/organizations/${ORG}/invitations`) {
      listReads.push(url.search)
    }
    return answerOrganizationOwner(route, url, ownerApiRecord())
  })

  await page.goto(`/organizations/${ORG}?tab=invitations`)
  await page.getByLabel("Email").fill(member.email)
  await page.getByLabel("Password").fill("Passw0rd!")
  await page.getByRole("button", { name: "Sign in", exact: true }).click()

  const members = page.getByRole("tab", { name: "Members" })
  await expect(members).toHaveAttribute("aria-selected", "true")
  await expect(page.getByText("colleague@example.test")).toBeVisible()
  await expect(page.getByRole("tab", { name: "Invitations" })).toHaveCount(0)
  await expect(page.getByRole("button", { name: "Invite member" })).toHaveCount(0)
  expect(listReads, "the list is never asked for").toEqual([])
})
