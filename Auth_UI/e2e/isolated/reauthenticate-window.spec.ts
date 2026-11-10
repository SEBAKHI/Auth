import { expect, test, type Page, type Route } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The window a signed-in person proves themself in (owner decision, 2026-11-09), on the built
 * console. What only a browser decides: it fits a phone in Arabic with every control reachable,
 * the page behind it is not lost, and a success leaves the person on the page with the change
 * going on, with no sign-out. A refresh that fails after the proof (REVIEW-FIXES 152 F33) keeps
 * the window, with its sentence and a retry, through the real client.
 *
 * Deliberate break this file must catch, seen red: the window signing out as it opens (the
 * person is taken away, so it is never seen).
 */

const STALE = {
  title: "Refused",
  status: 403,
  code: "Auth.ReauthenticationRequired",
  detail: "For your security, sign in again to change two-factor authentication.",
}

interface Server {
  proofs: unknown[]
  options: Array<{ proved: boolean }>
  logouts: number
}

const PROFILE = {
  id: "99999999-9999-9999-9999-999999999999",
  email: "isolated@example.test",
  firstName: "Isolated",
  lastName: "Operator",
  status: "Active",
  emailConfirmed: true,
  hasPassword: true,
  twoFactorEnabled: false,
  totpEnabled: false,
  qualifyingMfa: false,
  emailCodeEnabled: false,
  emailCodeAvailable: true,
  passkeyCount: 0,
}

const PASSKEY_REGISTRATION = {
  options: { challenge: "c2hvcnQ", rp: { id: "localhost", name: "Example" }, user: { id: "aGFuZGxl", name: "x", displayName: "x" }, pubKeyCredParams: [{ type: "public-key", alg: -7 }] },
  expiresAt: "2099-01-01T00:00:00Z",
  emailCodeRequired: false,
}

async function install(page: Page, server: Server, language: "en" | "ar") {
  let proved = false

  await installAuthenticatedApi(
    page,
    [],
    async (route: Route, url: URL) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()

      if (path === "/api/v1/users/me" && method === "GET") {
        await fulfillJson(route, { ...PROFILE, preferredLanguage: language })
        return true
      }
      if (path === "/api/v1/auth/passkeys" && method === "GET") {
        await fulfillJson(route, { rpId: "localhost", userHandle: null, passkeys: [] })
        return true
      }
      if (path === "/api/v1/auth/passkeys/registration/options" && method === "POST") {
        server.options.push({ proved })
        if (proved) await fulfillJson(route, PASSKEY_REGISTRATION)
        else await route.fulfill({ status: 403, contentType: "application/problem+json", body: JSON.stringify(STALE) })
        return true
      }
      if (path === "/api/v1/auth/reauthentication" && method === "POST") {
        server.proofs.push(route.request().postDataJSON())
        proved = true
        await route.fulfill({ status: 204 })
        return true
      }
      if (path === "/api/v1/auth/logout") {
        server.logouts += 1
        await route.fulfill({ status: 204 })
        return true
      }
      return false
    },
    { preferredLanguage: language }
  )
}

test.describe("the window that proves a signed-in person", () => {
  test.use({ viewport: { width: 375, height: 667 } })

  test("fits a phone in Arabic, and adding a passkey goes on from it with nothing signed out", async ({ page }) => {
    const server: Server = { proofs: [], options: [], logouts: 0 }
    await install(page, server, "ar")

    await page.goto("/profile?tab=security")
    await page.getByRole("button", { name: "إضافة مفتاح مرور" }).click()

    const proof = page.getByRole("dialog", { name: "أكّد أنك أنت" })
    await expect(proof).toBeVisible()
    await expect(proof).toContainText("تحتاج إضافة مفتاح مرور أو حذفه أن تؤكّد أنك أنت")

    // Inside the phone, nothing past the edges, and every control can be reached.
    const fit = await proof.evaluate((element) => {
      const box = element.getBoundingClientRect()
      return {
        left: box.left,
        right: box.right,
        width: window.innerWidth,
        spills: element.scrollWidth - element.clientWidth,
        direction: getComputedStyle(element).direction,
      }
    })
    expect(fit.left).toBeGreaterThanOrEqual(0)
    expect(fit.right).toBeLessThanOrEqual(fit.width)
    expect(fit.spills).toBe(0)
    expect(fit.direction).toBe("rtl")

    const confirm = proof.getByRole("button", { name: "تأكيد", exact: true })
    // An account without the app: the password alone, as a sign-in would ask.
    await proof.getByLabel("كلمة المرور", { exact: true }).fill("Isolated1!")
    await expect(proof.getByLabel("رمز التحقق", { exact: true })).toHaveCount(0)
    await confirm.scrollIntoViewIfNeeded()
    await expect(confirm).toBeInViewport()
    await confirm.click()

    // The person is where they were, and the dialog that adds the passkey is open.
    await expect(proof).toHaveCount(0)
    await expect(page.getByRole("dialog", { name: "إضافة مفتاح مرور" })).toBeVisible()
    await expect(page).toHaveURL(/\/profile\?tab=security$/)
    expect(server.proofs).toEqual([
      { password: "Isolated1!", emailCode: null, secondStepCode: null, useRecoveryCode: false },
    ])
    expect(server.options).toEqual([{ proved: false }, { proved: true }])
    expect(server.logouts).toBe(0)
  })

  test("says on a phone, in Arabic, when the page could not be updated after the proof, and goes on from a retry", async ({ page }) => {
    // REVIEW-FIXES 152 F33, through the real client: a refresh that answers 503 keeps the session
    // and its old token, so the window stays, with the proof accepted, and asks for nothing twice.
    const server: Server = { proofs: [], options: [], logouts: 0 }
    await install(page, server, "ar")
    let refreshFails = false
    // Registered after the mock's, so it answers first: Playwright runs the last route first. The
    // client calls `/api/v1/Auth/refresh`, and a glob matches case-sensitively, so the path is
    // compared as the mock compares it.
    await page.route((url) => url.pathname.toLowerCase() === "/api/v1/auth/refresh", async (route) => {
      if (refreshFails) await route.fulfill({ status: 503 })
      else await route.fallback()
    })

    await page.goto("/profile?tab=security")
    await page.getByRole("button", { name: "إضافة مفتاح مرور" }).click()
    const proof = page.getByRole("dialog", { name: "أكّد أنك أنت" })
    await proof.getByLabel("كلمة المرور", { exact: true }).fill("Isolated1!")
    refreshFails = true
    await proof.getByRole("button", { name: "تأكيد", exact: true }).click()

    await expect(proof.getByText("تم قبول تأكيدك، لكن تعذّر تحديث هذه الصفحة حتى الآن.")).toBeVisible()
    const fit = await proof.evaluate((element) => ({
      spills: element.scrollWidth - element.clientWidth,
      direction: getComputedStyle(element).direction,
    }))
    expect(fit).toEqual({ spills: 0, direction: "rtl" })
    const retry = proof.getByRole("button", { name: "إعادة المحاولة" })
    await expect(retry).toBeInViewport()
    await expect(proof.getByRole("button", { name: "إلغاء" })).toBeInViewport()
    expect(server.options).toEqual([{ proved: false }])

    refreshFails = false
    await retry.click()

    await expect(proof).toHaveCount(0)
    await expect(page.getByRole("dialog", { name: "إضافة مفتاح مرور" })).toBeVisible()
    expect(server.proofs).toHaveLength(1)
    expect(server.options).toEqual([{ proved: false }, { proved: true }])
    expect(server.logouts).toBe(0)
  })
})
