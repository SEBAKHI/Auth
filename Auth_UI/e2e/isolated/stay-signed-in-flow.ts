import { expect, type Browser, type Page } from "@playwright/test"

import { installAnonymousApi, loginResponse, type SeenRequest } from "./accounts/mock-anonymous-api"
import { fulfillJson } from "./mock-authenticated-api"

/**
 * OI-154 on the built app, shared by the console and accounts projects: the
 * "Stay signed in" box on the shared sign-in page, what the sign-in sends, and
 * what a person finds after the browser restarts on an unticked sign-in.
 */

const USER = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "jane@crm.example.com",
  firstName: "Jane",
  lastName: "Doe",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

/** The API in cookie mode: the sign-in answers with the sentinel, the refresh as `refresh` says. */
export async function installSignInApi(
  page: Page,
  options: { seen: SeenRequest[]; refresh: () => { status: number; body: unknown }; refreshes?: string[] }
) {
  await installAnonymousApi(
    page,
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/auth/login") {
        const signedIn = loginResponse(USER)
        await fulfillJson(route, { ...signedIn, token: { ...signedIn.token, refreshToken: "__cookie__" } })
        return true
      }
      if (path === "/api/v1/auth/refresh") {
        options.refreshes?.push(path)
        const { status, body } = options.refresh()
        await fulfillJson(route, body, status)
        return true
      }
      if (path === "/api/v1/auth/me") {
        // As the API answers it: the account only for a bearer token.
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

export const REFRESH_REFUSED = { status: 404, body: { status: 404, code: "Auth.RefreshTokenNotFound" } }

export function staySignedInBox(page: Page) {
  return page.getByRole("checkbox", { name: "Stay signed in" })
}

/** Signs in from the sign-in page, ticking the box first when asked. */
export async function signIn(page: Page, seen: SeenRequest[], tick: boolean) {
  await page.goto("/login")
  await page.getByLabel("Email").fill("jane@crm.example.com")
  await page.getByLabel("Password").fill("Passw0rd!")
  if (tick) await staySignedInBox(page).check()
  await page.getByRole("button", { name: "Sign in", exact: true }).click()
  await expect.poll(() => seen.filter((request) => request.path === "/api/v1/auth/login").length).toBe(1)
  // The sign-in is adopted: the session key holds the cookie sentinel.
  await expect
    .poll(() => page.evaluate(() => window.localStorage.getItem("auth.refreshToken")))
    .toBe("__cookie__")
}

export async function expectTheBox(page: Page) {
  await page.goto("/login")
  const box = staySignedInBox(page)
  await expect(box).toBeVisible()
  await expect(box).not.toBeChecked()
  await expect(
    page.getByText("Keeps you signed in on this browser after you close it.", { exact: false })
  ).toBeVisible()
}

export async function expectTheBodyCarriesTheChoice(page: Page, tick: boolean) {
  const seen: SeenRequest[] = []
  await installSignInApi(page, { seen, refresh: () => REFRESH_REFUSED })

  await signIn(page, seen, tick)

  const login = seen.find((request) => request.path === "/api/v1/auth/login")
  expect((login?.body as { staySignedIn?: unknown }).staySignedIn).toBe(tick)
}

/**
 * Signs in without the box, then "restarts the browser": a new context made from
 * the old one's stored state, minus its cookies that had no expiry. That state
 * carries localStorage, where the app keeps its session key, and not
 * sessionStorage, exactly as a restarted browser does. The API refuses the
 * refresh because the refresh cookie, a browser-session cookie, is gone.
 */
export async function expectARestartToLandOnSignIn(page: Page, browser: Browser, baseURL: string) {
  const seen: SeenRequest[] = []
  await installSignInApi(page, { seen, refresh: () => REFRESH_REFUSED })
  await signIn(page, seen, false)

  const state = await page.context().storageState()
  expect(
    state.origins.some((origin) =>
      origin.localStorage.some((entry) => entry.name === "auth.refreshToken" && entry.value === "__cookie__")
    ),
    "the session key survives the restart, as localStorage does"
  ).toBe(true)
  const restarted = await browser.newContext({
    baseURL,
    storageState: { ...state, cookies: state.cookies.filter((cookie) => cookie.expires !== -1) },
  })
  try {
    const next = await restarted.newPage()
    const refreshes: string[] = []
    await installSignInApi(next, { seen: [], refresh: () => REFRESH_REFUSED, refreshes })

    await next.goto("/")

    await expect(next).toHaveURL(/\/login/)
    await expect(next.getByRole("heading", { name: "Sign in" })).toBeVisible()
    await expect(staySignedInBox(next)).not.toBeChecked()
    // No loop: one refresh, refused for good, and nothing after it.
    await next.waitForTimeout(1500)
    expect(refreshes.length, "the app tried its session once").toBe(1)
    await expect(next).toHaveURL(/\/login/)
    // No error toast, and no note blaming the browser's cookie settings.
    await expect(next.locator('[data-sonner-toast][data-type="error"]')).toHaveCount(0)
    await expect(next.getByTestId("cookies-blocked-note")).toHaveCount(0)
    expect(await next.evaluate(() => window.localStorage.getItem("auth.refreshToken"))).toBeNull()
  } finally {
    await restarted.close()
  }
}

/**
 * The other side, so the assertion on the note can fail: in the SAME browser
 * session, a first refresh that finds no cookie means the browser refused it,
 * and the note says so.
 */
export async function expectARefusedCookieInTheSameSessionToBeNamed(page: Page) {
  const seen: SeenRequest[] = []
  await installSignInApi(page, { seen, refresh: () => REFRESH_REFUSED })
  await signIn(page, seen, false)

  await page.goto("/")

  await expect(page).toHaveURL(/\/login/)
  await expect(page.getByTestId("cookies-blocked-note")).toBeVisible()
}
