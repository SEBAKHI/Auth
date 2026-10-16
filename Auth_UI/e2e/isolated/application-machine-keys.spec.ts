import { readFile } from "node:fs/promises"

import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * A generated machine key's private half is shown once (OI-128, T9).
 *
 * The console holds it in the result dialog's state and nowhere else: closing
 * the dialog drops it, and opening the create dialog again shows a fresh form.
 * That is a browser behaviour (what the DOM, the storage and the URL hold after
 * a close), so it is asserted here, on the production build, and not only in
 * the unit test. The presence check before the close is the floor: an absence
 * check that never saw the key could not fail.
 */

const APP_ID = "33333333-3333-3333-3333-333333333333"
const KID = "q0Wd9Ywm0y8Kx5pWQZ1tQ8e3Sx0cVd1Jg2mN4kq7rTU"
const PEM_MARKER =
  "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgIsolatedOnly"
const PEM = `-----BEGIN PRIVATE KEY-----\n${PEM_MARKER}\n-----END PRIVATE KEY-----\n`

const application = {
  id: APP_ID,
  name: "Machine Test",
  code: "machine-test",
  isActive: true,
  accessMode: "Restricted",
  allowSelfRegistration: false,
  requireTwoFactor: false,
  requireEmailVerification: false,
  sessionTimeoutMinutes: 60,
  redirectUris: [],
  createdAt: "2026-07-19T16:32:00Z",
}

async function installMachineKeys(page: Page, preferredLanguage = "en") {
  const keys: Array<Record<string, unknown>> = []
  const creations: unknown[] = []

  await installAuthenticatedApi(
    page,
    ["applications:read", "apikeys:read", "apikeys:create", "apikeys:revoke"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/applications/${APP_ID}`) {
        await fulfillJson(route, application)
        return true
      }
      if (path === `/api/v1/applications/${APP_ID}/machine-keys`) {
        if (route.request().method() === "POST") {
          creations.push(route.request().postDataJSON())
          keys.unshift({
            id: "44444444-4444-4444-4444-444444444444",
            applicationId: APP_ID,
            kid: KID,
            name: null,
            algorithm: "ES256",
            createdAt: "2026-10-11T08:00:00Z",
            isRevoked: false,
          })
          await fulfillJson(
            route,
            {
              id: "44444444-4444-4444-4444-444444444444",
              kid: KID,
              clientId: "machine-test",
              issuer: "https://auth.example.com",
              tokenEndpoint: "https://auth.example.com/api/v1/auth/token",
              algorithm: "ES256",
              privateKeyPem: PEM,
              createdAt: "2026-10-11T08:00:00Z",
            },
            201
          )
          return true
        }
        await fulfillJson(route, keys)
        return true
      }
      // The page's other tabs each load a list; one empty envelope serves them.
      await fulfillJson(
        route,
        Object.assign([], {
          users: [],
          items: [],
          organizations: [],
          totalCount: 0,
          totalPages: 1,
          pageNumber: 1,
          pageSize: 20,
        })
      )
      return true
    },
    { preferredLanguage }
  )

  return { creations }
}

/** Everything that could still hold the key: the DOM, form values, storage and the address. */
async function everywhere(page: Page) {
  return page.evaluate(() => {
    const values = [...document.querySelectorAll("textarea, input")].map(
      (element) => (element as HTMLInputElement).value
    )
    const storage = [localStorage, sessionStorage].flatMap((store) =>
      Object.keys(store).map((key) => store.getItem(key) ?? "")
    )
    return [
      document.documentElement.outerHTML,
      ...values,
      ...storage,
      location.href,
    ].join("\n")
  })
}

test("a generated private key is shown once, downloads as <kid>.pem, and is gone after the dialog closes", async ({
  page,
}) => {
  const { creations } = await installMachineKeys(page)
  await page.goto(`/applications/${APP_ID}?tab=machine-keys`)

  await page.getByRole("button", { name: "Generate key" }).click()
  const form = page.getByRole("dialog", { name: "New application key" })
  await form.getByRole("button", { name: "Create" }).click()

  const result = page.getByRole("dialog", { name: "Application key created" })
  await expect(result).toBeVisible()
  await expect(
    result.getByLabel("Private key (PEM)", { exact: true })
  ).toHaveValue(PEM)
  await expect(result.getByLabel("Key ID (kid)", { exact: true })).toHaveValue(
    KID
  )
  expect(creations).toEqual([{ name: null, publicKeyJwk: null }])

  // The floor: the key is on the page now, so its absence later means something.
  expect(await everywhere(page)).toContain(PEM_MARKER)
  // Even while shown, it is never written to storage or the address.
  const persisted = await page.evaluate(() =>
    [
      ...Object.keys(localStorage).map(
        (key) => localStorage.getItem(key) ?? ""
      ),
      ...Object.keys(sessionStorage).map(
        (key) => sessionStorage.getItem(key) ?? ""
      ),
      location.href,
    ].join("\n")
  )
  expect(persisted).not.toContain(PEM_MARKER)

  const downloadPromise = page.waitForEvent("download")
  await result.getByRole("button", { name: "Download" }).click()
  const download = await downloadPromise
  expect(download.suggestedFilename()).toBe(`${KID}.pem`)
  expect(await readFile((await download.path())!, "utf8")).toBe(PEM)

  await result.getByRole("button", { name: "Close" }).last().click()
  await expect(result).toBeHidden()
  expect(await everywhere(page)).not.toContain(PEM_MARKER)

  // The new key is listed, by its kid, without any key material.
  await expect(page.getByText(KID)).toBeVisible()

  // Reopening shows a fresh form, never the key.
  await page.getByRole("button", { name: "Generate key" }).click()
  await expect(
    page.getByRole("dialog", { name: "New application key" })
  ).toBeVisible()
  expect(await everywhere(page)).not.toContain(PEM_MARKER)
  await expect(
    page.getByRole("dialog", { name: "Application key created" })
  ).toHaveCount(0)
})

test("Arabic: the machine keys tab and its dialog read right to left", async ({
  page,
}) => {
  // The language comes from the profile, as for a real operator.
  await installMachineKeys(page, "ar")
  await page.goto(`/applications/${APP_ID}?tab=machine-keys`)

  // The owner's step names the controls by these labels (ar.ts).
  await expect(page.getByRole("tab", { name: "مفاتيح الآلة" })).toBeVisible()
  await page.getByRole("button", { name: "إنشاء مفتاح" }).click()
  const form = page.getByRole("dialog", { name: "مفتاح آلة جديد" })
  await expect(form).toBeVisible()
  expect(
    await form.evaluate((element) => getComputedStyle(element).direction)
  ).toBe("rtl")
})
