import { expect, test, type Page } from "@playwright/test"

import {
  fulfillJson,
  installAnonymousApi,
  type SeenRequest,
} from "./mock-anonymous-api"
import { fulfillProblem, problem } from "../problem"

/**
 * OI-63: the page an application's authorize request lands on when the user
 * owns no organization set up for it, walked in a real browser against the
 * production build. Every API call is fulfilled here and every write is
 * recorded, so the assertions cover what was SENT and where the browser WENT,
 * not only what was drawn.
 */

const AUTHORIZE =
  "https://auth.example.com/api/v1/auth/authorize?response_type=code&client_id=EDIS" +
  `&redirect_uri=${encodeURIComponent("https://edis.example.com/callback")}` +
  "&code_challenge=abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ&code_challenge_method=S256" +
  "&state=xyz&prompt=create&create_organization=true"
const PAGE = `/create-organization?returnTo=${encodeURIComponent(AUTHORIZE)}`

interface SetupState {
  canCreate: boolean
  limit: number
  ownedOrganizations: { id: string; name: string }[]
  email: string
}

const OPEN: SetupState = {
  canCreate: true,
  limit: 1,
  ownedOrganizations: [],
  email: "exhibitor@expo.example",
}

/** The step's two endpoints, the app's branding, and the authorize landing. */
async function installSetupApi(
  page: Page,
  seen: SeenRequest[],
  answer: SetupState | ReturnType<typeof problem>,
  landed: string[]
) {
  await installAnonymousApi(
    page,
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (path === "/api/v1/applications/edis/public-branding") {
        await fulfillJson(route, { name: "EDIS", logoUrl: null })
        return true
      }
      if (path === "/api/v1/auth/organization-setup" && method === "GET") {
        if ("status" in answer) {
          await fulfillProblem(route, answer)
        } else {
          await fulfillJson(route, answer)
        }
        return true
      }
      if (path === "/api/v1/auth/organization-setup" && method === "POST") {
        await fulfillJson(route, { organizationId: "22222222-2222-2222-2222-222222222222" })
        return true
      }
      if (path === "/api/v1/auth/authorize") {
        // The browser came back to the authorize endpoint: stop here.
        landed.push(url.toString())
        await route.fulfill({ status: 200, contentType: "text/html", body: "<p>authorize</p>" })
        return true
      }
      return false
    },
    { seen }
  )
}

const posts = (seen: SeenRequest[]) =>
  seen.filter((request) => request.path === "/api/v1/auth/organization-setup")

test("creating the organization posts the name once and returns to the same authorize request", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const landed: string[] = []
  await installSetupApi(page, seen, OPEN, landed)

  await page.goto(PAGE)
  await expect(page.getByRole("heading", { name: "Create your organization" })).toBeVisible()
  await expect(page.getByText("Signed in as exhibitor@expo.example")).toBeVisible()
  await expect(page.getByText("EDIS works with organizations.", { exact: false })).toBeVisible()

  await page.getByRole("textbox", { name: "Organization name" }).fill("Expo House")
  await page.getByRole("button", { name: "Create organization" }).dblclick()

  await expect.poll(() => landed.length).toBeGreaterThan(0)
  expect(landed[0]).toBe(AUTHORIZE)
  expect(posts(seen)).toHaveLength(1)
  expect(posts(seen)[0].body).toEqual({ clientId: "EDIS", name: "Expo House" })
})

test("at the limit, an owned organization is offered and using it posts its id", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const landed: string[] = []
  const owned = { id: "33333333-3333-3333-3333-333333333333", name: "Existing Org" }
  await installSetupApi(
    page,
    seen,
    { ...OPEN, canCreate: false, ownedOrganizations: [owned] },
    landed
  )

  await page.goto(PAGE)
  await expect(page.getByText(/as many organizations as you may create yourself \(1\)/)).toBeVisible()
  await expect(page.getByRole("textbox", { name: "Organization name" })).toHaveCount(0)

  await page.getByRole("button", { name: "Use Existing Org" }).click()

  await expect.poll(() => landed.length).toBeGreaterThan(0)
  expect(landed[0]).toBe(AUTHORIZE)
  expect(posts(seen).map((request) => request.body)).toEqual([
    { clientId: "EDIS", organizationId: owned.id },
  ])
})

test("continuing without an organization removes only create_organization", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const landed: string[] = []
  await installSetupApi(page, seen, OPEN, landed)

  await page.goto(PAGE)
  await page.getByRole("button", { name: "Continue without an organization" }).click()

  await expect.poll(() => landed.length).toBeGreaterThan(0)
  const back = new URL(landed[0])
  const expected = new URL(AUTHORIZE)
  expected.searchParams.delete("create_organization")

  expect(back.searchParams.has("create_organization")).toBe(false)
  expect([...back.searchParams.entries()]).toEqual([...expected.searchParams.entries()])
  expect([...back.searchParams.keys()].length).toBeGreaterThan(5)
  expect(back.pathname).toBe("/api/v1/auth/authorize")
  expect(posts(seen)).toHaveLength(0)
})

test("an application that cannot create organizations still leaves a way back", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const landed: string[] = []
  await installSetupApi(
    page,
    seen,
    problem(403, "Organization.CreationFromApplicationUnavailable"),
    landed
  )

  await page.goto(PAGE)
  await expect(page.getByRole("alert")).toBeVisible()
  await expect(page.getByRole("textbox", { name: "Organization name" })).toHaveCount(0)
  await expect(
    page.getByRole("button", { name: "Continue without an organization" })
  ).toBeVisible()
})

test("without a single sign-on session the page sends the visitor to sign in for the same request", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const landed: string[] = []
  await installSetupApi(page, seen, problem(401, "Http.Unauthenticated"), landed)

  await page.goto(PAGE)

  await page.waitForURL("**/login?**")
  expect(new URL(page.url()).searchParams.get("returnTo")).toBe(AUTHORIZE)
})

test("without a pending request the page offers the user's organizations instead of a form", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  await installSetupApi(page, seen, OPEN, [])

  await page.goto("/create-organization")
  await expect(page.getByRole("link", { name: "Go to my organizations" })).toBeVisible()
  await expect(page.getByRole("textbox")).toHaveCount(0)
})

test("the page renders right-to-left in Arabic with its own copy", async ({ page }, testInfo) => {
  const seen: SeenRequest[] = []
  await installSetupApi(
    page,
    seen,
    { ...OPEN, ownedOrganizations: [{ id: "44444444-4444-4444-4444-444444444444", name: "مؤسسة قائمة" }] },
    []
  )
  await page.addInitScript(() => localStorage.setItem("auth.language", "ar"))

  await page.goto(PAGE)
  await expect(page.getByRole("heading", { name: "أنشئ منظمتك" })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.dir)).toBe("rtl")
  await expect(page.getByRole("textbox", { name: "اسم المنظمة" })).toBeVisible()
  await expect(page.getByRole("button", { name: "استعمال مؤسسة قائمة" })).toBeVisible()
  await expect(page.getByRole("button", { name: "المتابعة دون منظمة" })).toBeVisible()

  await page.screenshot({ path: testInfo.outputPath("create-organization-ar.png"), fullPage: true })
})

test("the English page, for the record", async ({ page }, testInfo) => {
  await installSetupApi(
    page,
    [],
    { ...OPEN, ownedOrganizations: [{ id: "55555555-5555-5555-5555-555555555555", name: "Existing Org" }] },
    []
  )

  await page.goto(PAGE)
  await expect(page.getByRole("heading", { name: "Create your organization" })).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath("create-organization-en.png"), fullPage: true })
})
