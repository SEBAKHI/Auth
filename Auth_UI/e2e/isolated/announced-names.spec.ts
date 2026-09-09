import { expect, test } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { fr } from "../../packages/i18n/src/locales/fr"
import { zh } from "../../packages/i18n/src/locales/zh"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The shell's unpainted names, in the medium that computes them.
 *
 * The nav control is a bare glyph and the phone drawer has no visible title,
 * so each one's entire name lives in text nobody can see. That is what let all
 * three sit in English long after the seven visible catalogues were complete:
 * an operator reading the console in Arabic has no way to notice, and so no
 * way to report it. A unit test proves the string was passed; only the browser
 * computes what a screen reader is actually handed, which is why the check
 * lives here.
 *
 * The expectations come from the catalogues themselves, so a literal written
 * back into the primitive fails, and a wording change does not.
 */
const CATALOGUES = { en, ar, fr, zh }

test.describe.configure({ mode: "parallel" })

for (const [code, catalogue] of Object.entries(CATALOGUES)) {
  test(`${code}: the nav control and drawer are named in the reader's language`, async ({
    page,
  }) => {
    await installAuthenticatedApi(
      page,
      ["users:read"],
      async (route) => {
        await fulfillJson(
          route,
          Object.assign([], {
            users: [],
            items: [],
            totalCount: 0,
            totalPages: 0,
            pageNumber: 1,
            pageSize: 20,
          })
        )
        return true
      },
      { preferredLanguage: code }
    )
    // Phone width: the only width where both names exist at once, because the
    // drawer is what the control opens here.
    await page.setViewportSize({ width: 390, height: 720 })
    await page.goto("/users")

    const trigger = page.locator('[data-slot="sidebar-trigger"]')
    await expect(trigger).toHaveAccessibleName(catalogue.nav.toggleSidebar)

    await trigger.click()
    await expect(page.getByRole("dialog")).toHaveAccessibleName(
      catalogue.nav.sidebar
    )
  })
}
