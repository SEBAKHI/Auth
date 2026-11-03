import { expect, test, type BrowserContext, type Page, type Route } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * S12: a token the API refuses before it expires — its signing key was
 * withdrawn, its session ended — costs one /refresh, after which requests carry
 * the new token. Until S12 the console kept the refused token until it expired
 * (15 minutes by default, up to a day), and every request failed meanwhile.
 *
 * Real Chromium tabs of one context, so the Web Lock, the BroadcastChannel and
 * localStorage are the browser's own: with two tabs refused on the same token,
 * one refreshes and the other adopts its result. The API is mocked: the real one
 * refuses an unexpired token only after a key change, which arrives with the
 * signing ring (S12 PR B, whose real run proves the path end to end).
 *
 * Which request meets the refused token first is the page's business (a
 * background query, or the save): the assertions hold whichever it is.
 */

const USER_ID = "77777777-7777-7777-7777-777777777777"

const user = {
  id: USER_ID,
  email: "forced-refresh@example.test",
  displayName: "Forced Refresh",
  firstName: "Forced",
  lastName: "Refresh",
  status: "Active",
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  preferredLanguage: "en",
  timeZone: "UTC",
  createdAt: "2026-08-22T08:00:00Z",
}

type Answered = { tab: string; path: string; method: string; bearer: string; status: number }

/** The API's side: mints a new token per refresh, refuses the ones it is told to, records every answer. */
class Api {
  refreshes = 0
  readonly refused = new Set<string>()
  readonly answered: Answered[] = []
  private readonly tokens: string[] = []

  /** The token the last refresh minted. */
  get latest(): string {
    return this.tokens[this.tokens.length - 1]
  }

  mint(): string {
    const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString("base64url")
    const token = `${encode({ alg: "none", typ: "JWT" })}.${encode({
      jti: `forced-refresh-${this.tokens.length + 1}`,
      // An hour ahead: the console never takes it for expired.
      exp: Math.floor(Date.now() / 1000) + 3600,
    })}.signature`
    this.tokens.push(token)
    return token
  }

  /** The saves `tab` made that the API accepted. */
  acceptedSaves(tab: string): Answered[] {
    return this.answered.filter((a) => a.tab === tab && a.method === "PUT" && a.status === 200)
  }

  /** Every request answered 401, from the moment `since` answers had been recorded. */
  refusals(since: number): Answered[] {
    return this.answered.slice(since).filter((a) => a.status === 401)
  }
}

function bearerOf(route: Route): string {
  return route.request().headers()["authorization"]?.replace(/^Bearer /, "") ?? ""
}

/** The user page, signed in, over `api`: answered before the shared defaults (routes run newest first). */
async function openUserPage(page: Page, api: Api, tab: string) {
  await installAuthenticatedApi(page, ["users:read", "users:update"], async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === `/api/v1/users/${USER_ID}`) {
      await fulfillJson(route, user)
      return true
    }
    if (path === `/api/v1/users/${USER_ID}/organizations`) {
      await fulfillJson(route, [])
      return true
    }
    return false
  })

  await page.route("**/api/v1/**", async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname.toLowerCase()
    if (path === "/api/v1/auth/refresh") {
      api.refreshes += 1
      await fulfillJson(route, { accessToken: api.mint(), refreshToken: "rotated-isolated-refresh" })
      return
    }

    const bearer = bearerOf(route)
    if (bearer && api.refused.has(bearer)) {
      api.answered.push({ tab, path, method: request.method(), bearer, status: 401 })
      await fulfillJson(route, { status: 401, code: "Http.Unauthenticated" }, 401)
      return
    }
    if (path === `/api/v1/users/${USER_ID}` && request.method() === "PUT") {
      api.answered.push({ tab, path, method: "PUT", bearer, status: 200 })
      await fulfillJson(route, user)
      return
    }
    api.answered.push({ tab, path, method: request.method(), bearer, status: 0 })
    await route.fallback()
  })

  await page.goto(`/users/${USER_ID}`)
  await expect(page.getByRole("button", { name: "Edit", exact: true })).toBeVisible()
}

/**
 * Edits and saves until the API accepts a save from `tab`. A save refused on the
 * old token leaves the dialog open with the change in it, so it is saved again.
 */
async function saveUntilAccepted(page: Page, api: Api, tab: string) {
  await page.getByRole("button", { name: "Edit", exact: true }).click()
  const dialog = page.getByRole("dialog", { name: "Edit user" })
  await dialog.getByRole("textbox", { name: "First name" }).fill("Changed")
  const save = dialog.getByRole("button", { name: "Save" })
  await save.click()

  await expect
    .poll(
      async () => {
        if (api.acceptedSaves(tab).length > 0) return true
        if (await save.isEnabled().catch(() => false)) await save.click().catch(() => undefined)
        return false
      },
      { timeout: 15_000 }
    )
    .toBe(true)
}

async function signedInTab(context: BrowserContext, api: Api, tab: string): Promise<Page> {
  const page = await context.newPage()
  await page.setViewportSize({ width: 1440, height: 900 })
  await openUserPage(page, api, tab)
  return page
}

test("a refused, unexpired token costs one /refresh, and the next request carries the new token", async ({ context }) => {
  const api = new Api()
  const page = await signedInTab(context, api, "A")
  const refreshesBefore = api.refreshes
  const answeredBefore = api.answered.length
  const refusedToken = api.latest
  api.refused.add(refusedToken)

  await saveUntilAccepted(page, api, "A")

  expect(api.refreshes).toBe(refreshesBefore + 1)
  const refusals = api.refusals(answeredBefore)
  expect(refusals.length).toBeGreaterThan(0)
  expect(refusals.every((a) => a.bearer === refusedToken)).toBe(true)
  expect(api.latest).not.toBe(refusedToken)
  expect(api.acceptedSaves("A").map((a) => a.bearer)).toEqual([api.latest])
  await expect(page).toHaveURL(new RegExp(`/users/${USER_ID}$`))
})

test("two tabs refused on the same token cost one /refresh between them", async ({ context }) => {
  const api = new Api()
  const tabA = await signedInTab(context, api, "A")
  const tabB = await signedInTab(context, api, "B")
  const refreshesBefore = api.refreshes
  const answeredBefore = api.answered.length
  // Both tabs hold the last token minted: each rotation is broadcast to the other.
  const refusedToken = api.latest
  api.refused.add(refusedToken)

  await Promise.all([saveUntilAccepted(tabA, api, "A"), saveUntilAccepted(tabB, api, "B")])

  expect(api.refreshes).toBe(refreshesBefore + 1)
  expect(api.refusals(answeredBefore).every((a) => a.bearer === refusedToken)).toBe(true)
  expect(api.acceptedSaves("A").map((a) => a.bearer)).toEqual([api.latest])
  expect(api.acceptedSaves("B").map((a) => a.bearer)).toEqual([api.latest])
  await expect(tabA).toHaveURL(new RegExp(`/users/${USER_ID}$`))
  await expect(tabB).toHaveURL(new RegExp(`/users/${USER_ID}$`))
})
