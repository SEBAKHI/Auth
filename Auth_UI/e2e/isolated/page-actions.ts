import { expect, type Page } from "@playwright/test"

/** The named menu's label, in the two languages this suite drives. */
const MENU_LABEL = /^(Actions|إجراءات)$/

/** Every action the surface kept out of the menu, in contract order. */
export const PROMOTED = '[data-slot="page-action-surface-action"]'

/**
 * Clicks a record's action by name, from wherever the surface put it.
 *
 * `PageActionSurface` keeps a short row of promoted actions as buttons and puts
 * every other one behind the named menu. Which actions hold those slots is the
 * owning page's decision - Edit on a user; Save draft and Publish on a template
 * editor - so a spec that names the action it wants stays true when a page
 * changes its mind. Encoding the location instead is what made half this suite
 * fail the day the surface changed shape.
 */
export async function clickPageAction(page: Page, name: string) {
  const promoted = page.locator(PROMOTED).filter({ hasText: name })
  const menu = page.getByRole("button", { name: MENU_LABEL })

  // Deciding which half holds the action is a READ, and a read does not wait.
  // The surface only renders once the record has loaded, so without this the
  // decision is taken against an empty page and always lands on the menu.
  await expect(page.locator(PROMOTED).first().or(menu).first()).toBeVisible()

  // `getByRole` name matching is exact and normalised; `hasText` above is a
  // substring, so this re-checks before committing to the promoted branch.
  const exact = page.getByRole("button", { name, exact: true })
  if ((await promoted.count()) > 0 && (await exact.count()) > 0) {
    await exact.first().click()
    return
  }

  await menu.click()
  await page
    .getByRole("menu")
    .getByRole("menuitem", { name, exact: true })
    .click()
}
