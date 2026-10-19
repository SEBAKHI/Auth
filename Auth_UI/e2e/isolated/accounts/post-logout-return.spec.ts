import { createHash } from "node:crypto"

import { expect, test, type Page, type Route } from "@playwright/test"

import {
  fulfillJson,
  installAnonymousApi,
  type SeenRequest,
} from "./mock-anonymous-api"

/**
 * OI-151: RP-initiated logout with a registered return
 * (`post_logout_redirect_uri`), walked in the built accounts app.
 *
 * The end-session endpoint is answered here the way the server answers the
 * relying party: a 302 to the confirmation page carrying the return as its hash
 * (`plr`), never the address. The page's own request after a confirmed 204 is
 * the walk's last navigation, and it is intercepted there: what the server does
 * with it (a 302 to the registered address with `state`, or back to the
 * confirmation while a session lives) is pinned at the real endpoint by
 * EndSessionReturnEndpointTests. Playwright does not route a request that a
 * fulfilled redirect leads to, so a stand-in relying party could not be served
 * here anyway. What is asserted is the walk: every navigation, in order, and
 * that no accounts URL in it holds a query value with `://` (a web application
 * firewall may refuse such a page).
 */

/** The API origin the test build is made for. */
const API = "https://auth.example.com"
const ACCOUNTS = "http://localhost:4176"
const RETURN = "https://shop.example.com/signed-out"
const HASH = createHash("sha256").update(RETURN, "utf8").digest("hex")
/** The page names the application only through its logo's text alternative. */
const LOGO_PATH = "/uploads/images/partner-shop.png"
// A 1x1 PNG: enough for the browser to decode.
const PNG = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=",
  "base64"
)

/** Every document request the browser makes, redirects included, in order. */
function recordNavigations(page: Page): string[] {
  const navigations: string[] = []
  page.on("request", (request) => {
    if (request.isNavigationRequest() && request.frame() === page.mainFrame()) {
      navigations.push(request.url())
    }
  })
  page.on("framenavigated", (frame) => {
    if (frame === page.mainFrame()) navigations.push(frame.url())
  })
  return navigations
}

function accountsQueryValuesWithAnAddress(urls: string[]): string[] {
  return urls
    .map((raw) => new URL(raw))
    .filter((url) => url.origin === ACCOUNTS)
    .flatMap((url) => [...url.searchParams.values()])
    .filter((value) => value.includes("://"))
}

async function installEndSession(
  page: Page,
  seen: SeenRequest[],
  options: { confirmation: number } = { confirmation: 204 }
) {
  await installAnonymousApi(
    page,
    async (route: Route, url: URL) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/applications/shop/public-branding") {
        await fulfillJson(route, {
          name: "Partner Shop",
          logoUrl: `${API}${LOGO_PATH}`,
          homeUrl: null,
        })
        return true
      }
      if (path === LOGO_PATH) {
        await route.fulfill({
          status: 200,
          contentType: "image/png",
          body: PNG,
        })
        return true
      }
      if (path !== "/api/v1/auth/end-session") return false

      if (route.request().method() === "POST") {
        await route.fulfill({ status: options.confirmation })
        return true
      }

      // The server's two answers, keyed by what the request carried.
      const query = url.searchParams
      const state = query.get("state") ?? ""
      if (
        query.get("post_logout_redirect_uri") === RETURN &&
        query.get("client_id") === "SHOP"
      ) {
        const confirmation = new URLSearchParams({
          client_id: "SHOP",
          state,
          plr: HASH,
        })
        await route.fulfill({
          status: 302,
          headers: { location: `${ACCOUNTS}/logout?${confirmation}` },
        })
        return true
      }
      if (
        query.get("plr") === HASH &&
        query.get("client_id") === "SHOP" &&
        !query.has("post_logout_redirect_uri")
      ) {
        await route.fulfill({
          status: 200,
          contentType: "text/html",
          body: "<!doctype html><title>end-session</title><h1>Back at end-session with the hash</h1>",
        })
        return true
      }
      await route.fulfill({
        status: 302,
        headers: { location: `${ACCOUNTS}/signed-out?client_id=SHOP` },
      })
      return true
    },
    { seen }
  )
}

function relyingPartyStart(): string {
  const query = new URLSearchParams({
    client_id: "SHOP",
    state: "xyz",
    post_logout_redirect_uri: RETURN,
  })
  return `${API}/api/v1/auth/end-session?${query}`
}

test("confirm: the final navigation is the end-session return by the hash, after the 204", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const navigations = recordNavigations(page)
  await installEndSession(page, seen)

  await page.goto(relyingPartyStart())

  await expect(page).toHaveURL(
    `${ACCOUNTS}/logout?client_id=SHOP&state=xyz&plr=${HASH}`
  )
  await expect(
    page.getByRole("heading", {
      name: "End your sign-in to your account on this browser?",
    })
  ).toBeVisible()
  // The title names no application; the logo's text alternative does.
  await expect(page.getByRole("img", { name: "Partner Shop" })).toBeVisible()
  await page
    .getByRole("button", { name: "End sign-in on this browser" })
    .click()

  const pageReturn = `${API}/api/v1/auth/end-session?client_id=SHOP&state=xyz&plr=${HASH}`
  await expect(page).toHaveURL(pageReturn)
  await expect(
    page.getByRole("heading", { name: "Back at end-session with the hash" })
  ).toBeVisible()

  // The confirmation is the unchanged bodiless POST.
  expect(seen).toEqual([
    { method: "POST", path: "/api/v1/auth/end-session", body: undefined },
  ])
  expect(navigations.at(-1)).toBe(pageReturn)
  expect(navigations.some((url) => url.startsWith(RETURN))).toBe(false)

  const accountsPages = navigations.filter((url) => url.startsWith(ACCOUNTS))
  expect(accountsPages.length).toBeGreaterThan(0)
  expect(accountsQueryValuesWithAnAddress(navigations)).toEqual([])
})

test("confirm answered with a failure: the signed-out page, never the registered address", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const navigations = recordNavigations(page)
  await installEndSession(page, seen, { confirmation: 429 })

  await page.goto(relyingPartyStart())
  await page
    .getByRole("button", { name: "End sign-in on this browser" })
    .click()

  await expect(page).toHaveURL(
    `${ACCOUNTS}/signed-out?client_id=SHOP&state=xyz`
  )
  expect(navigations.some((url) => url.startsWith(RETURN))).toBe(false)
  expect(
    navigations.some(
      (url) =>
        url.includes("plr=") &&
        url.startsWith(API) &&
        url !== relyingPartyStart()
    )
  ).toBe(false)
  expect(accountsQueryValuesWithAnAddress(navigations)).toEqual([])
})

test("Cancel: the profile, never the registered address and nothing posted", async ({
  page,
}) => {
  const seen: SeenRequest[] = []
  const navigations = recordNavigations(page)
  await installEndSession(page, seen)

  await page.goto(relyingPartyStart())
  await expect(page).toHaveURL(/\/logout\?/)
  await page
    .getByRole("link", { name: "Keep me signed in on this browser" })
    .click()

  await expect
    .poll(() => navigations.some((url) => new URL(url).pathname === "/profile"))
    .toBe(true)
  expect(navigations.some((url) => url.startsWith(RETURN))).toBe(false)
  expect(seen).toEqual([])
  expect(accountsQueryValuesWithAnAddress(navigations)).toEqual([])
})
