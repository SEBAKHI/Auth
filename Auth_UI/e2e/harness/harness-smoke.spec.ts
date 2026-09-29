import { ORIGINS, expect, test } from "./fixtures"
import { KNOWN_CSP_VIOLATIONS } from "./expected-csp-violations"

/**
 * /login of each application, built as it ships, under the CSP and headers its
 * web.config sends, on HTTPS same-site origins: zero violations beyond the
 * register in expected-csp-violations.ts.
 *
 * Where the sign-in form renders, every register entry is REQUIRED (csp.expect),
 * so a fixed cause cannot leave a stale excuse behind. A signed-in console never
 * renders /login - it moves on into the shell - so there the register is only
 * tolerated (the fixture's default).
 */

test("console /login, signed out, renders the sign-in form under its real CSP", async ({ page, api, csp }) => {
  for (const known of KNOWN_CSP_VIOLATIONS) csp.expect(known)
  await api.useAnonymous()
  await page.goto(`${ORIGINS.console}/login`)
  await expect(page.locator('input[type="email"]')).toBeVisible()
  await page.waitForLoadState("networkidle")
})

test("console /login, signed in, moves into the shell under its real CSP", async ({ page, api }) => {
  await api.useAuthenticated([])
  await page.goto(`${ORIGINS.console}/login`)
  await expect(page).not.toHaveURL(/\/login$/)
  await expect(page.locator("#root")).not.toBeEmpty()
  await page.waitForLoadState("networkidle")
})

test("accounts /login, signed out, renders the sign-in form under its real CSP", async ({ page, api, csp }) => {
  for (const known of KNOWN_CSP_VIOLATIONS) csp.expect(known)
  await api.useAnonymous()
  await page.goto(`${ORIGINS.accounts}/login`)
  await expect(page.locator('input[type="email"]')).toBeVisible()
  await page.waitForLoadState("networkidle")
})
