import type { Page, Route } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoCrushedContent, expectNoShellOverflow } from "../isolated/layout-overflow"
import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * S11 PR B in a real browser: the HMAC keyring panel on the Secrets page, under
 * the console's real web.config CSP (the automatic csp fixture fails any test on
 * a violation beyond the register).
 *
 * What these prove and what they do not: the API host is the harness model, not
 * the real API. The transitions, the step-up binding and the lookups after a
 * rotation are pinned by the xUnit suite and proven live by
 * Tools/probes/keyring-dual-verify.mjs. What is proven here is what only a built
 * bundle in a browser decides:
 *   - promote is closed, with its reason shown beside it, until a staged key is
 *     loaded by the API process;
 *   - a promote runs the three confirmation steps, each a dialog named by its
 *     title, bound to the keyring action, and is executed on the keyring
 *     endpoint, never on generate/hmac (the emergency alias);
 *   - in Arabic the page is right to left, and neither the shell nor the panel
 *     spills or is crushed, at desktop and phone widths.
 *
 * Deliberate breaks this file must catch: the challenge dialog's DialogTitle
 * replaced by a plain heading in apps/console/src/pages/secrets/
 * secret-operation-flow.tsx (the dialog loses its accessible name), and the
 * promote reason dropped from keyringCapabilities (promote opens on a key the
 * process has not loaded).
 */

/** The address the seeded /me reports (e2e/isolated/mock-authenticated-api.ts). */
const ADMIN_EMAIL = "isolated@example.test"
const CODE = "123456"

type Language = "en" | "ar"
const COPY = { en, ar } as const

interface Seen {
  method: string
  path: string
  body: unknown
}

function statusPayload() {
  return {
    secretFileExists: true,
    secretFilePath: "C:/secrets/secrets.dpapi",
    machineName: "HARNESS",
    schemaVersion: 1,
    secrets: {
      JwtPrivateKeyPem: "Configured",
      JwtPublicKeyPem: "Configured",
      RefreshTokenHmacKey: "Configured",
      GatewayToken: "Configured",
      AccountDeletionIdentifierHmacKey: "Configured",
      PasswordPepper: "Configured",
      SmtpPassword: "NotConfigured",
      "ConnectionStrings.AuthDb": "NotConfigured",
    },
  }
}

const original = {
  id: 1,
  state: "Current",
  createdAtUtc: null,
  demotedAtUtc: null,
  verifyUntilUtc: null,
  webhookGraceUntilUtc: null,
  compromised: false,
  webhookKeyCount: 3,
  loadedInProcess: true,
}

/** A ring that never rotated; with `staged`, key 2 staged, loaded or not. */
function keyringPayload(staged?: { loaded: boolean }) {
  const keys: Record<string, unknown>[] = [original]
  if (staged) {
    keys.push({
      ...original,
      id: 2,
      state: "Next",
      createdAtUtc: "2026-10-20T08:00:00Z",
      verifyUntilUtc: "2099-01-01T00:00:00Z",
      webhookKeyCount: 0,
      loadedInProcess: staged.loaded,
    })
  }
  return {
    currentKeyId: 1,
    processCurrentKeyId: 1,
    nextKeyId: staged ? 2 : null,
    processNextKeyId: staged?.loaded ? 2 : null,
    inSync: true,
    legacyCutoverUtc: null,
    keys,
  }
}

async function json(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) })
}

/** The Secrets page's API, recording every write the page sends. */
async function serveSecrets(
  api: HarnessApi,
  keyring: () => unknown,
  seen: Seen[],
  preferredLanguage: Language = "en"
) {
  await api.useAuthenticated(
    ["secrets.manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (method !== "GET") {
        seen.push({ method, path, body: route.request().postDataJSON() })
      }
      if (method === "GET" && path === "/api/v1/admin/secrets/status") {
        await json(route, statusPayload())
        return true
      }
      if (method === "GET" && path === "/api/v1/admin/secrets/keyring") {
        await json(route, keyring())
        return true
      }
      if (method === "POST" && path === "/api/v1/admin/secrets/challenges") {
        await json(route, {
          challengeId: "harness-challenge",
          maskedEmail: "i***@example.test",
          expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
        })
        return true
      }
      if (method === "POST" && path === "/api/v1/admin/secrets/challenges/harness-challenge/verify") {
        await json(route, {
          affectedUsers: 0,
          approvalExpiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
          requiresApiRestart: false,
          requiresGatewayReconfiguration: false,
          details: [
            { code: "usersBeyondOverlap", count: 0 },
            { code: "ssoSessionsBeyondOverlap", count: 0 },
          ],
        })
        return true
      }
      if (method === "POST" && path.startsWith("/api/v1/admin/secrets/keyring/hmac/")) {
        await json(route, keyring())
        return true
      }
      return false
    },
    { preferredLanguage }
  )
}

async function openSecrets(page: Page, language: Language) {
  await page.goto(`${ORIGINS.console}/admin/secrets`)
  await expect(page.locator("html")).toHaveAttribute("dir", language === "ar" ? "rtl" : "ltr")
  const title = page.getByText(COPY[language].secrets.keyring.title, { exact: true })
  await expect(title).toBeVisible()
  return title
}

test("promote is closed, and says why, until the API process holds a staged key", async ({ page, api }) => {
  let staged: { loaded: boolean } | undefined
  await serveSecrets(api, () => keyringPayload(staged), [])

  await openSecrets(page, "en")
  const promote = page.getByRole("button", { name: en.secrets.keyring.promote })
  await expect(promote).toBeDisabled()
  await expect(page.getByText(en.secrets.keyring.reason.noStaged).first()).toBeVisible()
  await expect(page.getByRole("button", { name: en.secrets.keyring.stage })).toBeEnabled()

  // Staged in the file, not loaded by this process: still closed, and the
  // reason names the restart that would load it.
  staged = { loaded: false }
  await page.reload()
  await expect(page.getByText(/Key 2 is staged in the secrets file but this process has not loaded it/)).toBeVisible()
  await expect(page.getByRole("button", { name: en.secrets.keyring.promote })).toBeDisabled()

  staged = { loaded: true }
  await page.reload()
  await expect(page.getByRole("button", { name: en.secrets.keyring.promote })).toBeEnabled()
})

test("a promote runs the three named steps, bound to its action, on the keyring endpoint", async ({ page, api }) => {
  const seen: Seen[] = []
  await serveSecrets(api, () => keyringPayload({ loaded: true }), seen)

  await openSecrets(page, "en")
  await page.getByRole("button", { name: en.secrets.keyring.promote }).click()

  // 1. What is about to happen, named by its title.
  const confirm = page.getByRole("alertdialog", { name: en.secrets.confirmTitle })
  await expect(confirm).toBeVisible()
  await expect(confirm).toContainText("Promote staged HMAC key 2")
  await expect(confirm).toContainText("nobody is signed out")
  await confirm.getByRole("button", { name: en.secrets.challengeContinue }).click()

  // 2. The emailed code, its dialog named by its title.
  const challenge = page.getByRole("dialog", { name: en.secrets.challengeTitle })
  await expect(challenge).toBeVisible()
  await challenge.locator("[data-slot=input-otp]").click()
  await page.keyboard.type(CODE)

  // 3. The cost, named by its title; the change runs once the address is typed.
  const impact = page.getByRole("alertdialog", { name: en.secrets.impactTitle })
  await expect(impact).toBeVisible()
  await expect(impact).toContainText(en.secrets.keyring.impactNote)
  const execute = impact.getByRole("button", { name: en.secrets.impactExecute })
  await expect(execute).toBeDisabled()
  await impact.locator("#secret-operation-confirm").fill(ADMIN_EMAIL)
  await execute.click()

  await expect(page.getByText(en.secrets.keyring.done)).toBeVisible()
  const writes = seen.map(({ method, path }) => `${method} ${path}`)
  expect(writes).toEqual([
    "POST /api/v1/admin/secrets/challenges",
    "POST /api/v1/admin/secrets/challenges/harness-challenge/verify",
    "POST /api/v1/admin/secrets/keyring/hmac/promote",
  ])
  expect(seen[0].body).toEqual({ operation: "GenerateHmacKey", keyringAction: "Promote" })
  expect(seen[1].body).toEqual({ code: CODE, keyringAction: "Promote" })
  expect(seen[2].body).toEqual({ challengeId: "harness-challenge", emergency: false })
})

for (const viewport of [
  { name: "desktop", width: 1280, height: 900 },
  { name: "phone", width: 390, height: 844 },
]) {
  test(`Arabic: the panel reads right to left and nothing spills or is crushed (${viewport.name})`, async ({ page, api }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await serveSecrets(api, () => keyringPayload({ loaded: false }), [], "ar")

    const title = await openSecrets(page, "ar")
    expect(await title.evaluate((element) => getComputedStyle(element).direction)).toBe("rtl")
    await expect(page.getByRole("button", { name: ar.secrets.keyring.promote })).toBeDisabled()
    await expect(page.getByRole("button", { name: ar.secrets.keyring.stage })).toBeDisabled()

    // A floor first, so a card that matches nothing cannot pass.
    const card = page.locator('[data-slot="card"]').filter({ has: title }).first()
    const spill = await card.evaluate((element) => ({
      width: element.clientWidth,
      card: element.scrollWidth - element.clientWidth,
    }))
    expect(spill.width).toBeGreaterThan(0)
    expect(spill.card).toBe(0)
    await expectNoShellOverflow(page, `secrets, Arabic, ${viewport.name}`)
    await expectNoCrushedContent(page, `secrets, Arabic, ${viewport.name}`)
  })
}
