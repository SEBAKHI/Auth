import { expect, test } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * On a phone a passkey row's text column is narrow, and an ItemDescription is
 * clamped to two lines. The sentence for a passkey whose counter went backwards
 * ends with its way out (remove it, add a new one), which is the part a clamp would
 * cut, with no scroll bar to say so. Measured on the element that clips.
 *
 * Deliberate break: remove `line-clamp-none` from the sentence (red in both languages).
 */

test.use({ viewport: { width: 375, height: 812 } })

const PASSKEY = {
  id: "aaaaaaaa-0000-0000-0000-000000000001",
  name: "Work laptop",
  createdAt: "2026-10-01T09:00:00Z",
  lastUsedAt: null,
  isBackupEligible: false,
  isBackedUp: false,
  lastCounterRegressionAt: "2026-11-02T09:00:00Z",
  credentialId: "Y3JlZC1sYXB0b3A",
}

for (const language of ["en", "ar"] as const) {
  test(`a passkey's counter sentence is not cut off at 375 px in ${language}`, async ({ page }) => {
    await installAuthenticatedApi(
      page,
      ["users:read"],
      async (route, url) => {
        const path = url.pathname.toLowerCase()
        if (path === "/api/v1/users/me") {
          await fulfillJson(route, {
            id: "99999999-9999-9999-9999-999999999999",
            email: "ada@example.test",
            firstName: "Ada",
            lastName: "Lovelace",
            status: "Active",
            emailConfirmed: true,
            hasPassword: true,
            twoFactorEnabled: true,
            totpEnabled: true,
            qualifyingMfa: true,
            emailCodeAvailable: true,
            passkeyCount: 1,
            preferredLanguage: language,
            timeZone: "UTC",
            roles: [],
            permissions: [],
          })
          return true
        }
        if (path === "/api/v1/auth/passkeys") {
          await fulfillJson(route, { rpId: "localhost", userHandle: "dXNlci1oYW5kbGU", passkeys: [PASSKEY] })
          return true
        }
        if (path === "/api/v1/auth/2fa/status") {
          await fulfillJson(route, { recoveryCodesRemaining: 8 })
          return true
        }
        return false
      },
      { preferredLanguage: language }
    )

    await page.goto("/profile?tab=security")
    const sentence = page.getByTestId("passkey-counter-regressed")
    await expect(sentence).toBeVisible()

    const { clientHeight, scrollHeight, width } = await sentence.evaluate((element) => ({
      clientHeight: element.clientHeight,
      scrollHeight: element.scrollHeight,
      width: element.getBoundingClientRect().width,
    }))
    // A floor: a row wide enough to hold the sentence in two lines would prove nothing.
    expect(width, "the row is not narrow: this measures nothing").toBeLessThan(300)
    expect(clientHeight, "the sentence takes more than two lines here").toBeGreaterThan(40)
    expect(scrollHeight).toBeLessThanOrEqual(clientHeight)
  })
}
