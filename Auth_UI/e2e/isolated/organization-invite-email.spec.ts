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
 * The invite dialog times its address check like shadcn's forms, that is like
 * react-hook-form's defaults (owner, 2026-10-31): nothing is judged while
 * typing or on leaving the field before the first submit; a malformed submit
 * shows the error, focuses the field and sends nothing; from then on the
 * verdict is live. The button stays enabled throughout. Arabic and English.
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
  test(`nothing is judged before the first submit; after it, the verdict is live (${language})`, async ({
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
    // Leaving the field before any submit: still no verdict.
    await page.keyboard.press("Tab")
    await expect(error).toHaveCount(0)
    await page.screenshot({ path: testInfo.outputPath(`invite-typing-${language}.png`) })

    // A submit with a malformed address shows the error and sends nothing.
    await send.click()
    await expect(error).toBeVisible()
    await expect(email).toHaveAttribute("aria-invalid", "true")
    await expect(email).toBeFocused()
    expect(recorded.invitations).toEqual([])
    await page.screenshot({ path: testInfo.outputPath(`invite-submit-error-${language}.png`) })

    // Live from here: still malformed keeps it, well-formed drops it at once.
    await email.pressSequentially("ple")
    await expect(error).toBeVisible()
    await email.pressSequentially(".test")
    await expect(email).toBeFocused()
    await expect(error).toHaveCount(0)
    await expect(email).toHaveAttribute("aria-invalid", "false")

    // The complete address is sent.
    await send.click()
    await expect(dialog).toBeHidden()
    expect(recorded.invitations).toEqual([
      { email: "rep@example.test", roleId: MEMBER_ROLE, applicationRole: null },
    ])
  })
}
