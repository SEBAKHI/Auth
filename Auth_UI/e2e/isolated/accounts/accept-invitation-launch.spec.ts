import { expect, test, type Page } from "@playwright/test"

import {
  fulfillJson,
  installAnonymousApi,
  loginResponse,
  type SeenRequest,
} from "./mock-anonymous-api"

/**
 * OI-192, walked in the built accounts app: accepting an invitation that gives a
 * role in an application sends the person into that application, and nothing is
 * asked twice.
 *
 * - An existing account follows the link signed out, signs in once, and lands on
 *   the application's host: no second click on Accept.
 * - A new account registers and lands on the application's host: the password is
 *   typed once.
 *
 * The application's host is routed here, so the assertion is the navigation the
 * browser actually made. The launch address comes from the API answer, as it does
 * from the server, and the page checks it before following it.
 */

const TOKEN = "aW52aXRhdGlvbi10b2tlbi1mb3ItdGhlLXNwZWM"
const INVITATION_ID = "44444444-4444-4444-4444-444444444444"
const ORGANIZATION_ID = "55555555-5555-5555-5555-555555555555"
const APPLICATION_HOST = "https://crm.example.com"
const LAUNCH = `${APPLICATION_HOST}/signin/start?iss=https%3A%2F%2Fauth.example.com`
const EMAIL = "rep@example.test"
const PASSWORD = "Passw0rd!x"

const USER = {
  id: "66666666-6666-6666-6666-666666666666",
  email: EMAIL,
  firstName: "Rae",
  lastName: "Rep",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

function preview(userExists: boolean) {
  return {
    id: INVITATION_ID,
    organizationName: "Northwind",
    email: EMAIL,
    roleName: "Member",
    invitedByName: "Ada Admin",
    status: "Pending",
    expiresAt: "2099-01-01T00:00:00Z",
    isExpired: false,
    isAlreadyMember: false,
    userExists,
    applicationName: "Customer Desk",
    applicationLogoUrl: null,
    applicationLogoUrlDark: null,
    applicationRoleName: "Sales representative",
  }
}

const acceptance = {
  success: true,
  organizationId: ORGANIZATION_ID,
  organizationName: "Northwind",
  roleName: "Member",
  applicationRoleName: "Sales representative",
  applicationRoleOutcome: "Applied",
  application: {
    code: "CRM",
    name: "Customer Desk",
    logoUrl: null,
    logoUrlDark: null,
  },
  launchUrl: LAUNCH,
}

// No in-file "a test ran" counter: an afterAll hook runs only in a worker that ran
// a test of this file, so it could never see the empty run it would guard against.
// The runner refuses that run itself ("No tests found" fails without
// --pass-with-no-tests), and each walk below fails unless the browser reaches the
// application's host.

/** The relying application, as far as this walk needs it: a page that loads. */
async function routeApplicationHost(page: Page, arrived: string[]) {
  await page.route(`${APPLICATION_HOST}/**`, async (route) => {
    arrived.push(route.request().url())
    await route.fulfill({
      status: 200,
      contentType: "text/html",
      body: "<!doctype html><title>Customer Desk</title><p>Signing you in</p>",
    })
  })
}

async function installInvitationApi(
  page: Page,
  options: { userExists: boolean; seen: SeenRequest[] }
) {
  await installAnonymousApi(
    page,
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/invitations/${TOKEN.toLowerCase()}`) {
        await fulfillJson(route, preview(options.userExists))
        return true
      }
      if (path === `/api/v1/invitations/${TOKEN.toLowerCase()}/accept`) {
        await fulfillJson(route, acceptance)
        return true
      }
      if (path === `/api/v1/invitations/${TOKEN.toLowerCase()}/register`) {
        await fulfillJson(route, {
          userId: USER.id,
          email: EMAIL,
          organizationName: "Northwind",
          roleName: "Member",
          message: "Account created and invitation accepted. Please sign in.",
          organizationId: ORGANIZATION_ID,
          applicationRoleOutcome: "Applied",
          application: acceptance.application,
          launchUrl: LAUNCH,
        })
        return true
      }
      if (path === "/api/v1/auth/login") {
        await fulfillJson(route, loginResponse(USER))
        return true
      }
      if (path === "/api/v1/auth/me") {
        const bearer = await route.request().headerValue("authorization")
        await (bearer
          ? fulfillJson(route, USER)
          : fulfillJson(route, { status: 401, code: "Http.Unauthorized" }, 401))
        return true
      }
      return false
    },
    { seen: options.seen }
  )
}

test("an existing account signs in once and lands in the application, without a second Accept", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const arrived: string[] = []
  await installInvitationApi(page, { userExists: true, seen })
  await routeApplicationHost(page, arrived)

  await page.goto(`/accept-invitation?token=${TOKEN}`)
  await expect(page.getByText("Customer Desk")).toBeVisible()
  await page.getByRole("button", { name: "Sign in to accept" }).click()

  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByLabel("Email")).toHaveValue(EMAIL)
  await page.getByLabel("Password").fill(PASSWORD)
  await page.getByRole("button", { name: "Sign in", exact: true }).click()

  await page.waitForURL(`${APPLICATION_HOST}/**`)
  expect(arrived).toEqual([LAUNCH])
  expect(seen.map((request) => request.path)).toEqual([
    "/api/v1/auth/login",
    `/api/v1/invitations/${TOKEN.toLowerCase()}/accept`,
  ])
})

test("a new account registers and lands in the application, typing the password once", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const arrived: string[] = []
  const visited: string[] = []
  page.on("framenavigated", (frame) => {
    if (frame === page.mainFrame()) visited.push(new URL(frame.url()).pathname)
  })
  await installInvitationApi(page, { userExists: false, seen })
  await routeApplicationHost(page, arrived)

  await page.goto(`/accept-invitation?token=${TOKEN}`)
  await page.getByLabel("First name").fill("Rae")
  await page.getByLabel("Last name").fill("Rep")
  await page.getByLabel("Password", { exact: true }).fill(PASSWORD)
  await page.getByLabel("Confirm password").fill(PASSWORD)
  await page.getByRole("button", { name: "Create account & join" }).click()

  await page.waitForURL(`${APPLICATION_HOST}/**`)
  expect(arrived).toEqual([LAUNCH])
  expect(seen.map((request) => request.path)).toEqual([
    `/api/v1/invitations/${TOKEN.toLowerCase()}/register`,
    "/api/v1/auth/login",
  ])
  const login = seen.find((request) => request.path === "/api/v1/auth/login")
  expect(login?.body).toMatchObject({ email: EMAIL, password: PASSWORD })
  expect(visited).not.toContain("/login")
})
