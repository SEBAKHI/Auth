import type { Frame, Page } from "@playwright/test"
import { mkdirSync } from "node:fs"

import { fulfillJson } from "../isolated/mock-authenticated-api"
import { HOSTS, ORIGINS, expect, test, type HarnessApi } from "./fixtures"
import { headersFor } from "./web-config-model"

/**
 * The account bar of linked applications (owner decisions of 2026-11-02) under the headers
 * IIS sends, on real same-site HTTPS origins: a linked application on a sibling subdomain
 * (app.example.com) adds the loader and the element, the bar's frames come from the accounts
 * app with the policy of <location path="embed">, and they read the person from the API by
 * the single sign-on cookie (SameSite=Lax) alone. The csp fixture fails the test on any
 * violation not declared, in the application's page and in every frame.
 *
 * What a unit test cannot show, and this does: the browser sends the Lax cookie from a frame
 * inside a sibling subdomain (same-site all the way up), the bar's frames run under their
 * strict policy (style-src 'self', no inline style), the loader's styles survive the
 * application's own strict CSP, and sign-out leaves through the application's address, the
 * accounts confirmation and the end-session endpoint, back to the application with the
 * single sign-on session over.
 *
 * Deliberate breaks: drop frame-ancestors' site from web.config (the bar never draws); have
 * the frames read the bar through the app's API client (a refresh shows up in the log);
 * remove the "Embed Unknown Path" rule (the /embed/nothing check turns red).
 */

const SCREENSHOTS = process.env.ACCOUNT_BAR_SCREENSHOTS
const POST_LOGOUT = `${ORIGINS.linkedApp}/signed-out`
const RETURN_HASH = "b".repeat(64)
const SIGN_OUT = `${ORIGINS.api}/api/v1/auth/end-session?client_id=crm&state=s1&post_logout_redirect_uri=${encodeURIComponent(POST_LOGOUT)}`

const PERSON = {
  name: "Maya Haddad",
  email: "maya@example.com",
  profileImageUrl: null,
  preferredLanguage: null,
  theme: null,
  applications: [
    { id: "a1", name: "Crm", logoUrl: null, logoUrlDark: null, launchUrl: `${ORIGINS.linkedApp}/` },
    { id: "a2", name: "Wiki", logoUrl: null, logoUrlDark: null, launchUrl: "https://wiki.example.com/" },
    { id: "a3", name: "Billing Portal", logoUrl: null, logoUrlDark: null, launchUrl: "https://billing.example.com/" },
  ],
}

/**
 * The linked application's page: a header with the element, under a CSP as strict as an
 * application's may be. It names the accounts origin for the loader and its frames, and
 * allows no inline style or script at all.
 */
function linkedAppPage(title: string, attributes = `signout-href="${SIGN_OUT}"`) {
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>${title}</title>
<script async src="${ORIGINS.accounts}/embed/v1/account-bar.js"></script></head>
<body><header><strong>${title}</strong> <iam-account-bar ${attributes.replaceAll("&", "&amp;")}></iam-account-bar></header>
<main><p>A linked application on a subdomain of the IAM site.</p></main></body></html>`
}

const APP_CSP = {
  "content-security-policy": `default-src 'none'; script-src ${ORIGINS.accounts}; frame-src ${ORIGINS.accounts}; style-src 'none'; base-uri 'none'`,
}

/**
 * The API's side: the bar answers the accounts origin only, with the cookie; end-session
 * runs the real round trip (the confirmation page, then back to the registered address).
 */
async function serveIdentityProvider(api: HarnessApi, seen: { endSession: number }) {
  await api.useAnonymous(async (route, url) => {
    const request = route.request()
    const path = url.pathname.toLowerCase()
    const headers = request.headers()
    const signedIn = /(?:^|;\s*)auth_idp=live(?:;|$)/.test(headers.cookie ?? "")
    if (path === "/api/v1/auth/account-bar" && request.method() === "GET") {
      if (headers.origin !== ORIGINS.accounts) {
        await fulfillJson(route, { code: "Auth.FirstPartyOriginRequired" }, 403)
      } else if (signedIn) {
        await route.fulfill({ status: 200, contentType: "application/json", headers: { "cache-control": "no-store" }, body: JSON.stringify(PERSON) })
      } else {
        await fulfillJson(route, { code: "Http.Unauthenticated" }, 401)
      }
      return true
    }
    if (path === "/api/v1/auth/end-session" && request.method() === "GET") {
      // The confirmation first (with the registered address named by its hash); back to the
      // application once the session is over.
      const location = url.searchParams.get("plr") === RETURN_HASH && !signedIn
        ? `${POST_LOGOUT}?state=s1`
        : `${ORIGINS.accounts}/logout?client_id=crm&state=s1&plr=${RETURN_HASH}`
      await route.fulfill({ status: 302, headers: { location }, body: "" })
      return true
    }
    if (path === "/api/v1/auth/end-session" && request.method() === "POST") {
      expect(headers.origin).toBe(ORIGINS.accounts)
      seen.endSession += signedIn ? 1 : 0
      await route.fulfill({
        status: 204,
        headers: { "set-cookie": "auth_idp=; Path=/; Max-Age=0; Secure; HttpOnly; SameSite=Lax" },
        body: "",
      })
      return true
    }
    return false
  })
}

async function signIn(page: Page) {
  await page.context().addCookies([
    { name: "auth_idp", value: "live", url: ORIGINS.api, secure: true, httpOnly: true, sameSite: "Lax" },
  ])
}

/** A frame of the bar; the element's shadow root is closed, so frames are found by address. */
async function frameAt(page: Page, path: RegExp): Promise<Frame> {
  await expect.poll(() => page.frames().some((frame) => path.test(frame.url()))).toBe(true)
  return page.frames().find((frame) => path.test(frame.url()))!
}

async function shot(page: Page, name: string) {
  if (!SCREENSHOTS) return
  mkdirSync(SCREENSHOTS, { recursive: true })
  // The panels open with a fade and a zoom: the picture is of the finished panel.
  for (const frame of page.frames()) {
    await frame
      .evaluate(() => Promise.all(document.getAnimations().map((animation) => animation.finished)))
      .catch(() => undefined)
  }
  await page.screenshot({ path: `${SCREENSHOTS}/${name}.png` })
}

test("serves the bar's files with their own headers, and nothing else under /embed", async ({ harness, request }) => {
  const policyOf = (path: string) =>
    Object.fromEntries(headersFor(harness.models.accounts, path).map(([name, value]) => [name.toLowerCase(), value]))

  for (const path of ["/embed/v1/bar.html", "/embed/v1/panel.html"]) {
    const served = await request.get(`${ORIGINS.accounts}${path}`)
    expect(served.status()).toBe(200)
    const headers = served.headers()
    expect(headers["content-security-policy"]).toBe(policyOf(path)["content-security-policy"])
    expect(headers["content-security-policy"]).toContain("frame-ancestors https://*.example.com")
    expect(headers["content-security-policy"]).toContain("script-src 'self';")
    expect(headers["content-security-policy"]).toContain("style-src 'self';")
    expect(headers["x-frame-options"]).toBeUndefined()
    expect(headers["content-security-policy-report-only"]).toBeUndefined()
  }

  const loader = await request.get(`${ORIGINS.accounts}/embed/v1/account-bar.js`)
  expect(loader.status()).toBe(200)
  expect(loader.headers()["content-type"]).toMatch(/javascript/)

  // The SPA never answers under /embed: it would be framable there.
  expect((await request.get(`${ORIGINS.accounts}/embed/v1/profile`)).status()).toBe(404)
  expect((await request.get(`${ORIGINS.accounts}/embed`)).status()).toBe(404)

  // Every other page still refuses every frame.
  const profile = (await request.get(`${ORIGINS.accounts}/profile`)).headers()
  expect(profile["x-frame-options"]).toBe("DENY")
  expect(profile["content-security-policy"]).toContain("frame-ancestors 'none'")
})

test("draws inside a linked application, opens both panels, and signs out through the application", async ({
  page,
  api,
  attacker,
  requests,
}) => {
  const seen = { endSession: 0 }
  await serveIdentityProvider(api, seen)
  await signIn(page)
  await page.setViewportSize({ width: 1280, height: 720 })
  if (process.env.ACCOUNT_BAR_DEBUG) {
    page.on("console", (message) => console.log(`[console ${message.type()}] ${message.location().url}: ${message.text()}`))
    page.on("pageerror", (error) => console.log(`[pageerror] ${error.message}`))
    page.on("requestfailed", (request) => console.log(`[requestfailed] ${request.url()} ${request.failure()?.errorText}`))
    page.on("response", (response) => console.log(`[response] ${response.status()} ${response.url()}`))
  }
  await page.goto(attacker.serve(HOSTS.linkedApp, "/", linkedAppPage("Crm"), APP_CSP))
  attacker.serve(HOSTS.linkedApp, "/signed-out", linkedAppPage("Crm, signed out"), APP_CSP)

  // The bar: the grid button and the avatar, at the platform's header size.
  const bar = await frameAt(page, /\/embed\/v1\/bar\.html/)
  const grid = bar.getByRole("button", { name: "Applications" })
  const avatar = bar.getByRole("button", { name: "Maya Haddad" })
  await expect(grid).toBeVisible()
  await expect(avatar).toBeVisible()
  expect(await grid.evaluate((element) => element.getBoundingClientRect().height)).toBe(40)

  // The read came from the frame, same-site, with the Lax cookie, and nothing refreshed.
  const reads = requests.to(HOSTS.api).filter((entry) => entry.path === "/api/v1/Auth/account-bar")
  expect(reads.length).toBeGreaterThan(0)
  for (const read of reads) {
    expect(read.origin).toBe(ORIGINS.accounts)
    expect(read.secFetchSite).toBe("same-site")
    expect(read.cookie).toContain("auth_idp=live")
  }
  expect(requests.to(HOSTS.api).some((entry) => /refresh/i.test(entry.path))).toBe(false)
  await shot(page, "1-bar-in-linked-app")

  // The applications panel, under the bar.
  await grid.click()
  const apps = await frameAt(page, /\/embed\/v1\/panel\.html\?.*panel=apps/)
  await expect(apps.getByRole("link", { name: /Crm/ })).toBeVisible()
  await expect(grid).toHaveAttribute("aria-expanded", "true")
  const appsFrame = (await apps.frameElement())!
  await expect.poll(() => appsFrame.isVisible()).toBe(true)
  const barBox = (await (await bar.frameElement())!.boundingBox())!
  const appsBox = (await appsFrame.boundingBox())!
  // The panel's card begins just below the bar (its frame keeps 16px for the shadow).
  expect(appsBox.y + 16).toBeGreaterThan(barBox.y + barBox.height - 8)
  expect(appsBox.x + appsBox.width - 16).toBeLessThanOrEqual(1280 - 8 + 1)
  await shot(page, "2-apps-panel")

  // Escape closes it and gives the grid button the focus back.
  await page.keyboard.press("Escape")
  await expect.poll(() => appsFrame.isVisible()).toBe(false)
  await expect(grid).toHaveAttribute("aria-expanded", "false")

  // The account panel: name, email, Profile, Sign out.
  await avatar.click()
  const account = await frameAt(page, /\/embed\/v1\/panel\.html\?.*panel=account/)
  await expect(account.getByText("maya@example.com")).toBeVisible()
  await expect(account.getByRole("menuitem", { name: /Profile/ })).toBeVisible()
  const accountFrame = (await account.frameElement())!
  await expect.poll(() => accountFrame.isVisible()).toBe(true)
  await expect.poll(async () => (await accountFrame.boundingBox())?.height ?? 0).toBeGreaterThan(120)
  await shot(page, "3-account-panel")

  // A press on the application's own page closes it.
  await page.mouse.click(200, 400)
  await expect.poll(async () => (await account.frameElement())!.isVisible()).toBe(false)

  // Sign out: the application's address, the accounts confirmation, back to the application.
  await avatar.click()
  await account.getByRole("menuitem", { name: "Sign out" }).click()
  await page.waitForURL(`${ORIGINS.accounts}/logout?**`)
  await shot(page, "4-accounts-sign-out-confirmation")
  await page.getByRole("button", { name: "End sign-in on this browser" }).click()
  await page.waitForURL(`${POST_LOGOUT}?state=s1`)

  expect(seen.endSession).toBe(1)
  const cookies = await page.context().cookies(ORIGINS.api)
  expect(cookies.find((cookie) => cookie.name === "auth_idp")).toBeUndefined()

  // Back on the application with no single sign-on session: Sign in, and no grid.
  const after = await frameAt(page, /\/embed\/v1\/bar\.html/)
  await expect(after.getByRole("button", { name: "Sign in" })).toBeVisible()
  await expect(after.getByRole("button", { name: "Applications" })).toHaveCount(0)
  await shot(page, "5-back-on-the-app-signed-out")
})

test("is not drawn by the apex or by a cross-site page", async ({ page, api, attacker, requests, csp }) => {
  csp.tolerate({
    directive: "frame-ancestors",
    blocked: /^https:\/\/accounts\.example\.com\//,
    reason: "negative check: the apex and a cross-site page may not frame the account bar",
  })
  await serveIdentityProvider(api, { endSession: 0 })
  await signIn(page)
  for (const host of [HOSTS.apex, HOSTS.attacker] as const) {
    requests.clear()
    await page.goto(attacker.serve(host, "/", linkedAppPage("Not a linked application", "")))
    // The loader asked for the frame...
    await expect
      .poll(() => requests.to(HOSTS.accounts).some((entry) => entry.path === "/embed/v1/bar.html"))
      .toBe(true)
    // ...and the browser refused to show it (frame-ancestors): no document of ours runs in
    // it, so nothing reads the person.
    await page.waitForTimeout(1500)
    expect(requests.to(HOSTS.api).filter((entry) => entry.path === "/api/v1/Auth/account-bar")).toEqual([])
    for (const frame of page.frames().filter((candidate) => candidate !== page.mainFrame())) {
      await expect(frame.getByRole("button")).toHaveCount(0)
    }
  }
})

test("follows the application's colour mode and the person's stored language, with a transparent frame", async ({
  page,
  api,
  attacker,
  context,
}) => {
  await serveIdentityProvider(api, { endSession: 0 })
  await signIn(page)
  // The person chose Arabic in the accounts app: the frames read that app's own storage,
  // which a same-site frame shares.
  await context.addInitScript((origin: string) => {
    if (location.origin === origin) localStorage.setItem("auth.language", "ar")
  }, ORIGINS.accounts)
  const darkPage = linkedAppPage("Crm", `theme="dark" signout-href="${SIGN_OUT}"`).replace(
    "<head>",
    '<head><style>html{background:#111;color:#eee;direction:rtl}header{display:flex;justify-content:space-between;align-items:center}</style>'
  )
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.goto(
    attacker.serve(HOSTS.linkedApp, "/dark", darkPage, {
      "content-security-policy": `default-src 'none'; script-src ${ORIGINS.accounts}; frame-src ${ORIGINS.accounts}; style-src 'unsafe-inline'`,
    })
  )

  const bar = await frameAt(page, /\/embed\/v1\/bar\.html/)
  const grid = bar.getByRole("button", { name: "التطبيقات" })
  await expect(grid).toBeVisible()
  expect(await bar.evaluate(() => ({ dir: document.documentElement.dir, dark: document.documentElement.classList.contains("dark") }))).toEqual({
    dir: "rtl",
    dark: true,
  })
  // Transparent: the page's dark header shows through the frame around the buttons.
  const corner = await page.screenshot({ clip: { ...(await (await bar.frameElement())!.boundingBox())!, width: 2, height: 2 } })
  expect(corner.length).toBeGreaterThan(0)
  const pixel = await page.evaluate(async (bytes: number[]) => {
    const bitmap = await createImageBitmap(new Blob([new Uint8Array(bytes)], { type: "image/png" }))
    const canvas = new OffscreenCanvas(bitmap.width, bitmap.height)
    const draw = canvas.getContext("2d")!
    draw.drawImage(bitmap, 0, 0)
    return [...draw.getImageData(0, 0, 1, 1).data.slice(0, 3)]
  }, [...corner])
  expect(Math.max(...pixel)).toBeLessThan(40)

  await grid.click()
  const apps = await frameAt(page, /\/embed\/v1\/panel\.html\?.*panel=apps/)
  await expect(apps.getByRole("link", { name: /Crm/ })).toBeVisible()
  const appsFrame = (await apps.frameElement())!
  await expect.poll(() => appsFrame.isVisible()).toBe(true)
  // In a right-to-left page the bar is at the header's end, the left, and the panel opens
  // inward from it: their left edges line up (the bar's buttons sit 4px inside its frame,
  // the panel's card 16px inside its own).
  const barBox = (await (await bar.frameElement())!.boundingBox())!
  const appsBox = (await appsFrame.boundingBox())!
  expect(barBox.x).toBeLessThan(100)
  expect(Math.abs(appsBox.x + 16 - (barBox.x + 4))).toBeLessThanOrEqual(1)
  await shot(page, "6-dark-arabic-apps-panel")
})
