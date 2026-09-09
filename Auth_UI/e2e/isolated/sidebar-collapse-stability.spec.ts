import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * Collapsing the rail may change one thing: how wide the sidebar is.
 *
 * It used to change three more, and all three moved the entries the operator
 * was aiming at:
 *
 *  1. The group heading ("Platform") carried `-mt-8` in the icon state, so the
 *     whole list jumped 32px up on collapse and 32px back down on expand.
 *  2. A nav button was forced to `size-8` collapsed, 4px shorter than the `h-9`
 *     it has open, so the list also compressed by 4px per entry — nine entries
 *     in the console, so the last one moved 68px in total.
 *  3. That same rule forced `p-2` in place of `px-3`, sliding every icon 4px
 *     inward, and the header's collapse control was given `ms-0` to match.
 *
 * None of it was visible to anything we had. Every entry stayed present, named,
 * clickable and inside the viewport in both states; the overflow, crushed-content
 * and responsive checks all pass over a list that jumps. Only a measurement of
 * the same element in both states can see it, which is what this file is.
 *
 * WHAT IS ASSERTED, AND WHY IT IS EXACT. `x`, `y` and `height` of every nav
 * button, and of the icon inside it, must be identical to the pixel across the
 * toggle. Not "close": the arithmetic is exact, so any drift means a rule has
 * reintroduced a state-dependent box rather than that a browser rounded.
 *
 *   rail, collapsed             56   SIDEBAR_WIDTH_ICON = "3.5rem",
 *                                    packages/ui/src/sidebar.tsx
 *   group padding, each side     8   `p-2` on SidebarGroup
 *   button, collapsed           40   `group-data-[collapsible=icon]:w-10!`
 *                                    8 + 40 + 8 = 56, so the pill is centred
 *   button inline padding       12   `px-3`, unchanged by the collapse
 *   icon                        16   `[&_svg]:size-4`
 *   ICON START, BOTH STATES     20   8 + 12, and 12 + 16 + 12 = 40 exactly
 *
 * `width` is deliberately not asserted: the button is 40px collapsed and fills
 * the 256px sidebar open. That is the one difference collapsing is allowed.
 */
test.use({ viewport: { width: 1440, height: 900 } })

const NAV_BUTTON = '[data-sidebar="menu-button"]'
const SIDEBAR_TRIGGER = '[data-slot="sidebar-trigger"]'

/** Boxes of every nav entry and its icon, in document order. */
async function navGeometry(page: Page) {
  const buttons = page.locator(NAV_BUTTON)
  const count = await buttons.count()
  const rows = []
  for (let i = 0; i < count; i++) {
    const button = buttons.nth(i)
    const box = await button.boundingBox()
    const icon = await button.locator("svg").first().boundingBox()
    rows.push({
      href: await button.getAttribute("href"),
      x: box?.x,
      y: box?.y,
      height: box?.height,
      iconX: icon?.x,
      iconY: icon?.y,
    })
  }
  return rows
}

/**
 * The rail animates its width over 200ms (`transition-[left,right,width]`), and
 * a box read mid-animation is nobody's final layout. Waiting on the width to
 * stop changing settles it without a fixed sleep.
 */
async function settle(page: Page, expectedWidth: number) {
  await expect
    .poll(async () => {
      const box = await page
        .locator('[data-slot="sidebar-container"]')
        .boundingBox()
      return Math.round(box?.width ?? 0)
    })
    .toBe(expectedWidth)
}

test("nav entries do not move when the sidebar collapses or expands", async ({
  page,
}) => {
  await installAuthenticatedApi(page, ["users:read", "roles:read"], async (route) => {
    await fulfillJson(route, { items: [], totalCount: 0 })
    return true
  })

  await page.goto("/users")
  await expect(page.locator(NAV_BUTTON).first()).toBeVisible()
  await settle(page, 256)

  const open = await navGeometry(page)
  expect(open.length).toBeGreaterThan(1)

  await page.locator(SIDEBAR_TRIGGER).click()
  await settle(page, 56)
  expect(await navGeometry(page)).toEqual(open)

  await page.locator(SIDEBAR_TRIGGER).click()
  await settle(page, 256)
  expect(await navGeometry(page)).toEqual(open)
})

test("the collapse control does not move either, and the group is still named", async ({
  page,
}) => {
  await installAuthenticatedApi(page, ["users:read"], async (route) => {
    await fulfillJson(route, { items: [], totalCount: 0 })
    return true
  })

  await page.goto("/users")
  // The heading is gone from the page but the section still has a name: it is
  // the accessible name of the navigation landmark holding the links.
  const nav = page.getByRole("navigation", { name: "Platform" })
  await expect(nav.getByRole("link").first()).toBeVisible()

  const trigger = page.locator(SIDEBAR_TRIGGER)
  await settle(page, 256)
  const before = await trigger.boundingBox()

  await trigger.click()
  await settle(page, 56)
  expect(await trigger.boundingBox()).toEqual(before)
})
