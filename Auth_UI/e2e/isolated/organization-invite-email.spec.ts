import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { installAuthenticatedApi } from "./mock-authenticated-api"
import {
  answerOrganizationOwner,
  MEMBER_ROLE,
  ORG,
  OWNER_ORG_PERMISSIONS,
  ownerApiRecord,
  type OwnerApiRecord,
} from "./organization-owner-api"

/**
 * The invite dialog never judges an address while it is typed (owner,
 * 2026-10-31). The verdict appears when the field is left or a submit is
 * tried, and goes away on the next edit; the button stays enabled, and a
 * malformed address shows the error instead of being sent. Arabic and English.
 */

const catalogs = { en, ar } as const

async function signedInOwner(
  page: Page,
  language: keyof typeof catalogs
): Promise<OwnerApiRecord> {
  const recorded = ownerApiRecord()
  await installAuthenticatedApi(
    page,
    [],
    (route, url) => answerOrganizationOwner(route, url, recorded),
    { organizationPermissions: OWNER_ORG_PERMISSIONS, preferredLanguage: language }
  )
  return recorded
}

for (const language of ["en", "ar"] as const) {
  test(`an unfinished address shows no error while typed; a submit or leaving the field shows it (${language})`, async ({
    page,
  }, testInfo) => {
    const t = catalogs[language]
    const recorded = await signedInOwner(page, language)
    // The link chooses the role, so the address is the only open field.
    await page.goto(`/organizations/${ORG}?invite=1&role=org-member`)

    const dialog = page.getByRole("dialog", { name: t.organizations.inviteMember })
    await expect(dialog).toBeVisible()
    const email = dialog.getByLabel(t.organizations.inviteEmail)
    const send = dialog.getByRole("button", { name: t.organizations.inviteMember })
    const error = dialog.getByText(t.validation.email)

    // Typing, one key at a time: no verdict, and the button stays usable.
    await email.pressSequentially("rep@exam")
    await expect(error).toHaveCount(0)
    await expect(email).toHaveAttribute("aria-invalid", "false")
    await expect(send).toBeEnabled()
    await page.screenshot({ path: testInfo.outputPath(`invite-typing-${language}.png`) })

    // A submit with a malformed address shows the error and sends nothing.
    await send.click()
    await expect(error).toBeVisible()
    await expect(email).toHaveAttribute("aria-invalid", "true")
    await expect(email).toBeFocused()
    expect(recorded.invitations).toEqual([])
    await page.screenshot({ path: testInfo.outputPath(`invite-submit-error-${language}.png`) })

    // The next edit clears it; leaving the field with a malformed address brings it back.
    await email.pressSequentially("p")
    await expect(error).toHaveCount(0)
    await page.keyboard.press("Tab")
    await expect(error).toBeVisible()

    // A complete address is sent.
    await email.fill("rep@example.test")
    await expect(error).toHaveCount(0)
    await send.click()
    await expect(dialog).toBeHidden()
    expect(recorded.invitations).toEqual([
      { email: "rep@example.test", roleId: MEMBER_ROLE, applicationRole: null },
    ])
  })
}
