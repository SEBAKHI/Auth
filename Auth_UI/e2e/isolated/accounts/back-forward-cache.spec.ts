import { expect, test } from "@playwright/test"

import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * OI-150: the organization page, brought back with the Back button, asks the
 * server again instead of coming back alive from the back/forward cache with
 * the previous person's organizations and working buttons.
 *
 * Playwright launches Chromium with the back/forward cache switched off, and
 * its default headless shell has no such cache at all, so a Back there always
 * reloads and this test would pass with or without the fix. This file runs the
 * full Chromium build (the new headless mode) with the switch removed, and the
 * test checks that the page really did come back from the cache before trusting
 * what it measures. With the switch removed but the headless shell kept, a probe
 * found the page not restored, for a reason the browser reports as "masked".
 */
test.use({
  channel: "chromium",
  launchOptions: { ignoreDefaultArgs: ["--disable-back-forward-cache"] },
})

const HANDLE = "0123456789abcdef".repeat(4)
const PAGE = `/create-organization?client_id=crm&auth_request=${HANDLE}`

/**
 * Set by an init script when the page is shown from the cache, in session
 * storage, so it survives the reload the fix performs.
 */
const RESTORED_KEY = "oi150.restored-from-cache"

test("Back to the organization page asks the server for its state again", async ({
  page,
}) => {
  const reads: string[] = []
  await page.addInitScript((key) => {
    window.addEventListener("pageshow", (event) => {
      if (event.persisted) sessionStorage.setItem(key, "yes")
    })
  }, RESTORED_KEY)
  await installAnonymousApi(page, async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/applications/crm/public-branding") {
      await fulfillJson(route, { name: "CRM", logoUrl: null })
      return true
    }
    if (
      path === "/api/v1/auth/organization-setup" &&
      route.request().method() === "GET"
    ) {
      reads.push(url.search)
      await fulfillJson(route, {
        canCreate: true,
        limit: 1,
        ownedOrganizations: [
          { id: "66666666-6666-6666-6666-666666666666", name: "Existing Org" },
        ],
        email: "person@example.com",
        emailConfirmed: true,
      })
      return true
    }
    return false
  })

  await page.goto(PAGE)
  await expect(
    page.getByRole("button", { name: "Use Existing Org" })
  ).toBeVisible()
  expect(reads).toHaveLength(1)

  // Somewhere else, then Back. Through the page's own history, not goBack():
  // a page shown from the cache fires no load event, which goBack() waits for,
  // so without the fix the test would end on a timeout instead of the
  // assertion that says what went wrong.
  await page.goto("/sign-in-expired")
  await page.evaluate(() => history.back())

  await expect(
    page.getByRole("button", { name: "Use Existing Org" })
  ).toBeVisible()
  // The floor: the page came back from the cache. Without it, a Back that
  // reloads for any other reason would pass this test whatever the page does.
  await expect
    .poll(() =>
      page.evaluate((key) => sessionStorage.getItem(key), RESTORED_KEY)
    )
    .toBe("yes")
  // And it asked the server again, with the same request.
  await expect.poll(() => reads.length).toBe(2)
  expect(reads[1]).toBe(reads[0])
})
