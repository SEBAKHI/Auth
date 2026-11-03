import type { Page, Route } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoCrushedContent, expectNoShellOverflow } from "../isolated/layout-overflow"
import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * S12 PR B in a real browser: the signing keyring card on the Secrets page, under
 * the console's real web.config CSP (the automatic csp fixture fails any test on a
 * violation beyond the register).
 *
 * What these prove and what they do not: the API host is the harness model, not the
 * real API. The transitions, the step-up binding, the 24-hour wait and the process
 * swap are pinned by the xUnit suite and proven live by
 * Tools/probes/signing-key-rotation.mjs. What is proven here is what only a built
 * bundle in a browser decides:
 *   - promote is closed, with its reason and its time shown beside it, until the
 *     staged key is loaded by the API process and published for 24 hours;
 *   - an emergency runs the three confirmation steps, each a dialog named by its
 *     title, bound to its action on the signing keyring, and is executed on
 *     keyring/rsa/promote, never on generate/rsa (the alias);
 *   - a leaked key this process still accepts is said in a red alert, never left to
 *     a table cell;
 *   - in Arabic the page is right to left, and neither the shell nor the card (with
 *     its 43-character key ids) spills or is crushed, at desktop and phone widths.
 *
 * Deliberate breaks this file must catch: the challenge dialog's DialogTitle replaced
 * by a plain heading in apps/console/src/pages/secrets/secret-operation-flow.tsx (the
 * dialog loses its accessible name), and the restart reason dropped from
 * signingKeyringCapabilities (promote opens on a key the process has not loaded).
 */

/** The address the seeded /me reports (e2e/isolated/mock-authenticated-api.ts). */
const ADMIN_EMAIL = "isolated@example.test"
const CODE = "123456"

const CURRENT = "Yp3yQnXc0bK1w0b0q5Zb2l1i8m4kX6s9TqvW7cJr1aA"
const STAGED = "q8H5w2n0tP4vR7yZ1aB3cD5eF7gH9iJ0kL2mN4oP6qR"
const PAST = "2001-01-01T00:00:00Z"
const FUTURE = "2099-01-01T00:00:00Z"

type Language = "en" | "ar"
const COPY = { en, ar } as const

interface Seen {
  method: string
  path: string
  body: unknown
}

type Staged = { loaded: boolean; promoteAt: string | null } | undefined

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

function signingKey(kid: string, state: string, overrides: Record<string, unknown> = {}) {
  return {
    kid,
    state,
    migrated: false,
    createdAtUtc: "2026-10-01T08:00:00Z",
    publishedAtUtc: null,
    verifyUntilUtc: null,
    compromised: false,
    inFile: true,
    loadedInProcess: true,
    acceptedInProcess: true,
    ...overrides,
  }
}

/** The keyring read: an HMAC ring that never rotated, and the signing ring under test. */
function keyringPayload(staged: Staged, leaked = false) {
  const keys: Record<string, unknown>[] = [signingKey(CURRENT, "Current")]
  if (staged) {
    keys.push(
      signingKey(STAGED, "Next", {
        createdAtUtc: "2026-11-02T08:00:00Z",
        publishedAtUtc: staged.loaded ? "2026-11-02T09:00:00Z" : null,
        loadedInProcess: staged.loaded,
        acceptedInProcess: staged.loaded,
      })
    )
  }
  if (leaked) {
    keys.push(
      signingKey("leaked-0123456789-abcdefghij-0123456789-ABCDE", "Compromised", {
        compromised: true,
        verifyUntilUtc: PAST,
      })
    )
  }
  return {
    currentKeyId: 1,
    processCurrentKeyId: 1,
    nextKeyId: null,
    processNextKeyId: null,
    inSync: true,
    legacyCutoverUtc: null,
    keys: [
      {
        id: 1,
        state: "Current",
        createdAtUtc: null,
        demotedAtUtc: null,
        verifyUntilUtc: null,
        webhookGraceUntilUtc: null,
        compromised: false,
        webhookKeyCount: 0,
        loadedInProcess: true,
      },
    ],
    rsa: {
      available: true,
      keySource: "Jwt:PrivateKeyPem",
      currentKid: CURRENT,
      processCurrentKid: CURRENT,
      nextKid: staged ? STAGED : null,
      processNextKid: staged?.loaded ? STAGED : null,
      persisted: true,
      promoteAvailableAtUtc: staged?.promoteAt ?? null,
      inSync: !staged || staged.loaded,
      keys,
    },
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
  answer: () => unknown = keyring,
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
          affectedUsers: 12,
          approvalExpiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
          requiresApiRestart: true,
          requiresGatewayReconfiguration: false,
          details: [
            { code: "usersWithLiveAccessTokens", count: 5 },
            { code: "usersWithActiveSessions", count: 12 },
          ],
        })
        return true
      }
      if (method === "POST" && path.startsWith("/api/v1/admin/secrets/keyring/rsa/")) {
        await json(route, answer())
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
  const title = page.getByText(COPY[language].secrets.signingKeyring.title, { exact: true })
  await expect(title).toBeVisible()
  return title
}

function card(page: Page, language: Language) {
  const title = page.getByText(COPY[language].secrets.signingKeyring.title, { exact: true })
  return page.locator('[data-slot="card"]').filter({ has: title }).first()
}

test("promote is closed, and says why and until when, until the staged key is loaded and published 24 hours", async ({ page, api }) => {
  let staged: Staged
  await serveSecrets(api, () => keyringPayload(staged), [])

  await openSecrets(page, "en")
  const signing = card(page, "en")
  const promote = signing.getByRole("button", { name: en.secrets.signingKeyring.promote, exact: true })
  await expect(promote).toBeDisabled()
  await expect(signing.getByText(en.secrets.signingKeyring.reason.noStaged)).toBeVisible()
  await expect(signing.getByRole("button", { name: en.secrets.signingKeyring.emergency })).toBeDisabled()

  // Staged in the file, not loaded: closed, and the reason names the restart.
  staged = { loaded: false, promoteAt: null }
  await page.reload()
  await expect(signing.getByText(en.secrets.signingKeyring.reason.restartToLoad)).toBeVisible()
  await expect(promote).toBeDisabled()

  // Loaded and published, before the 24 hours: closed, and the reason names the time.
  staged = { loaded: true, promoteAt: FUTURE }
  await page.reload()
  await expect(signing.getByText(/The staged key can be promoted from/)).toBeVisible()
  await expect(promote).toBeDisabled()

  staged = { loaded: true, promoteAt: PAST }
  await page.reload()
  await expect(promote).toBeEnabled()
})

test("an emergency runs the three named steps on the signing keyring endpoint, and says when it closes the leaked key", async ({ page, api }) => {
  const seen: Seen[] = []
  let leaked = false
  await serveSecrets(
    api,
    () => keyringPayload({ loaded: false, promoteAt: null }, leaked),
    seen,
    () => {
      leaked = true
      return { ...keyringPayload(undefined, true).rsa, changeAppliedInProcess: false }
    }
  )

  await openSecrets(page, "en")
  await card(page, "en").getByRole("button", { name: en.secrets.signingKeyring.emergency }).click()

  // 1. What is about to happen, named by its title.
  const confirm = page.getByRole("alertdialog", { name: en.secrets.confirmTitle })
  await expect(confirm).toBeVisible()
  await expect(confirm).toContainText(`Emergency promote of signing key ${STAGED}`)
  await expect(confirm).toContainText("the old key stays accepted here until the API restarts")
  await confirm.getByRole("button", { name: en.secrets.challengeContinue }).click()

  // 2. The emailed code, its dialog named by its title.
  const challenge = page.getByRole("dialog", { name: en.secrets.challengeTitle })
  await expect(challenge).toBeVisible()
  await challenge.locator("[data-slot=input-otp]").click()
  await page.keyboard.type(CODE)

  // 3. The cost, named by its title; the change runs once the address is typed.
  const impact = page.getByRole("alertdialog", { name: en.secrets.impactTitle })
  await expect(impact).toBeVisible()
  await expect(impact).toContainText(en.secrets.signingKeyring.impactNote)
  await impact.locator("#secret-operation-confirm").fill(ADMIN_EMAIL)
  await impact.getByRole("button", { name: en.secrets.impactExecute }).click()

  // Said in plain words, and kept on the card until the restart closes it.
  await expect(page.getByText(en.secrets.signingKeyring.closesAtRestart)).toBeVisible()
  await expect(card(page, "en").getByText(en.secrets.signingKeyring.compromisedAcceptedTitle)).toBeVisible()

  expect(seen.map(({ method, path }) => `${method} ${path}`)).toEqual([
    "POST /api/v1/admin/secrets/challenges",
    "POST /api/v1/admin/secrets/challenges/harness-challenge/verify",
    "POST /api/v1/admin/secrets/keyring/rsa/promote",
  ])
  expect(seen[0].body).toEqual({ operation: "GenerateRsaKey", keyringAction: "PromoteEmergency" })
  expect(seen[1].body).toEqual({ code: CODE, keyringAction: "PromoteEmergency" })
  expect(seen[2].body).toEqual({ challengeId: "harness-challenge", emergency: true })
})

for (const viewport of [
  { name: "desktop", width: 1280, height: 900 },
  { name: "phone", width: 390, height: 844 },
]) {
  test(`Arabic: the signing card reads right to left and nothing spills or is crushed (${viewport.name})`, async ({ page, api }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await serveSecrets(api, () => keyringPayload({ loaded: false, promoteAt: null }, true), [], undefined, "ar")

    const title = await openSecrets(page, "ar")
    expect(await title.evaluate((element) => getComputedStyle(element).direction)).toBe("rtl")
    const signing = card(page, "ar")
    await expect(signing.getByRole("button", { name: ar.secrets.signingKeyring.promote, exact: true })).toBeDisabled()
    await expect(signing.getByText(ar.secrets.signingKeyring.compromisedAcceptedTitle)).toBeVisible()

    // A floor first, so a card that matches nothing cannot pass.
    const spill = await signing.evaluate((element) => ({
      width: element.clientWidth,
      card: element.scrollWidth - element.clientWidth,
    }))
    expect(spill.width).toBeGreaterThan(0)
    expect(spill.card).toBe(0)
    await expectNoShellOverflow(page, `secrets signing keyring, Arabic, ${viewport.name}`)
    await expectNoCrushedContent(page, `secrets signing keyring, Arabic, ${viewport.name}`)
  })
}
